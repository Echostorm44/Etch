using Etch.Bench.Compose;
using Etch.Compose.Cpu;

namespace Etch.Compose.Tests;

/// <summary>
/// The CPU composer's tiling, threading and fast paths change nothing: every scene renders to the
/// same bytes as the single-threaded per-pixel path. And a steady-state frame allocates nothing.
/// </summary>
[NotInParallel(nameof(CpuComposerTests))]
internal sealed class CpuComposerTests
{
    public static IEnumerable<(string, float)> Scenes()
    {
        foreach (string scene in ParityScenes.Names)
        {
            foreach (float scale in new[] { 1f, 1.25f, 1.5f, 2f })
            {
                yield return (scene, scale);
            }
        }
        yield return ("ui", 1f);
        yield return ("ui", 1.5f);
    }

    [Test]
    [MethodDataSource(nameof(Scenes))]
    public async Task FastPathsAndThreadsReproduceThePerPixelPath(string scene, float scale)
    {
        using var cpu = new CpuRender(scene, scale);
        byte[] reference = cpu.Render(threads: 1, fastPaths: false);
        byte[] fast = cpu.Render(threads: 1, fastPaths: true);
        byte[] parallel = cpu.Render(threads: -1, fastPaths: true);
        byte[] three = cpu.Render(threads: 3, fastPaths: true);

        await Assert.That(FirstDifference(reference, fast)).IsEqualTo(-1).Because("fast paths must not change a pixel");
        await Assert.That(FirstDifference(reference, parallel)).IsEqualTo(-1).Because("parallel tiles must not change a pixel");
        await Assert.That(FirstDifference(reference, three)).IsEqualTo(-1).Because("the thread count must not change a pixel");
    }

    // Single-threaded frames are measured on this thread alone; parallel frames process-wide, so
    // this test runs exclusively.
    [Test]
    [NotInParallel]
    [Arguments(1)]
    [Arguments(-1)]
    public async Task SteadyStateFrameAllocatesNothing(int threads)
    {
        using var cpu = new CpuRender("ui", 1f);
        cpu.Composer.MaxDegreeOfParallelism = threads;
        for (int i = 0; i < 3; i++)
        {
            cpu.BuildAndRender();
        }
        long before = threads == 1 ? GC.GetAllocatedBytesForCurrentThread() : GC.GetTotalAllocatedBytes(precise: true);
        for (int i = 0; i < 5; i++)
        {
            cpu.BuildAndRender();
        }
        long allocated = (threads == 1 ? GC.GetAllocatedBytesForCurrentThread() : GC.GetTotalAllocatedBytes(precise: true)) - before;
        await Assert.That(allocated).IsEqualTo(0L);
    }

    private static int FirstDifference(byte[] a, byte[] b)
    {
        int n = a.AsSpan().CommonPrefixLength(b);
        return n == a.Length && a.Length == b.Length ? -1 : n / 4;
    }

    /// <summary>A CPU composer with one scene's recording.</summary>
    private sealed class CpuRender : IDisposable
    {
        private readonly DrawRecording recording;
        private readonly DrawListBuilder builder = new();
        private readonly DrawList list = new();
        private readonly CpuFramebuffer framebuffer = new();
        private readonly uint width;
        private readonly uint height;

        public CpuRender(string scene, float scale)
        {
            if (scene == "ui")
            {
                width = (uint)Math.Round(1920 * scale / 1.5f);
                height = (uint)Math.Round(1080 * scale / 1.5f);
                recording = UiFrameScene.Build((int)width, (int)height, scale);
            }
            else
            {
                width = (uint)Math.Round(ParityScenes.Width * scale);
                height = (uint)Math.Round(ParityScenes.Height * scale);
                recording = ParityScenes.Build(scene, scale);
            }
        }

        public CpuComposer Composer { get; } = new();

        public void BuildAndRender()
        {
            builder.Begin(list, width, height, Composer.Masks, Composer.MonoAtlas, Composer.ColorAtlas);
            builder.Replay(recording);
            builder.End();
            list.Parameters = new ComposeParameters { TextGamma = 1.5f, LightWeight = 1f };
            Composer.Render(list, framebuffer);
        }

        public byte[] Render(int threads, bool fastPaths)
        {
            Composer.MaxDegreeOfParallelism = threads;
            Composer.FastPaths = fastPaths;
            BuildAndRender();
            var rgba = new byte[width * height * 4];
            framebuffer.CopyToRgba(rgba);
            return rgba;
        }

        public void Dispose() => Composer.Dispose();
    }
}
