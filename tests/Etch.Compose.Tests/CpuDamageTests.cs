using Etch.Bench.Compose;
using Etch.Compose.Cpu;
using Etch.Geometry;

namespace Etch.Compose.Tests;

/// <summary>
/// Incremental rendering: a frame rendered into the previous frame's framebuffer, re-rendering only
/// the tiles whose draws changed, is identical to rendering it from scratch; the reported damage
/// is small when the change is (a caret blink touches one or two tiles); and it allocates nothing.
/// </summary>
[NotInParallel(nameof(CpuDamageTests))]
internal sealed class CpuDamageTests
{
    private const int Width = 1280;
    private const int Height = 720;

    [Test]
    public async Task IncrementalFramesEqualFullFrames()
    {
        // A sequence of frames: unchanged, caret on/off, a title edit, a scale change of the same
        // content (everything moves), back again.
        var frames = new List<DrawRecording>
        {
            UiFrameScene.Build(Width, Height, 1f),
            UiFrameScene.Build(Width, Height, 1f),
            UiFrameScene.Build(Width, Height, 1f, caret: true),
            UiFrameScene.Build(Width, Height, 1f, caret: false),
            UiFrameScene.Build(Width, Height, 1f, caret: true, titleVariant: 1),
            UiFrameScene.Build(Width, Height, 1.25f, caret: true, titleVariant: 1),
            UiFrameScene.Build(Width, Height, 1f, caret: true, titleVariant: 2),
        };
        using var incremental = new Pipeline();
        using var full = new Pipeline();
        for (int i = 0; i < frames.Count; i++)
        {
            byte[] a = incremental.RenderIncremental(frames[i], Width, Height, out _);
            byte[] b = full.RenderFull(frames[i], Width, Height);
            await Assert.That(FirstDifference(a, b)).IsEqualTo(-1).Because($"frame {i}");
        }
    }

    [Test]
    public async Task BlursReRenderWhenWhatTheyReadChanges()
    {
        // A blur panel over content that changes beside (not under) its rect, within its reach.
        using var incremental = new Pipeline();
        using var full = new Pipeline();
        for (int frame = 0; frame < 4; frame++)
        {
            var rec = new DrawRecording();
            rec.SetTransform(Affine.Identity);
            rec.FillRect(0, 0, 400, 300, ComposePaint.Solid(new ComposeColor(1, 1, 1, 1)));
            rec.FillRect(60 + frame * 7, 90, 30, 30, ComposePaint.Solid(new ComposeColor(1, 0, 0, 1)));
            rec.BackdropBlur(100, 80, 150, 100, 12, 8, new ComposeColor(1, 1, 1, 0.2f));
            rec.FillRect(300, 200, 20, 20 + frame, ComposePaint.Solid(new ComposeColor(0, 0, 1, 1)));
            byte[] a = incremental.RenderIncremental(rec, 400, 300, out _);
            byte[] b = full.RenderFull(rec, 400, 300);
            await Assert.That(FirstDifference(a, b)).IsEqualTo(-1).Because($"frame {frame}");
        }
    }

    [Test]
    public async Task CaretBlinkDamagesOneOrTwoTiles()
    {
        using var incremental = new Pipeline();
        incremental.RenderIncremental(UiFrameScene.Build(Width, Height, 1f, caret: false), Width, Height, out _);
        incremental.RenderIncremental(UiFrameScene.Build(Width, Height, 1f, caret: true), Width, Height, out var dirty);
        long pixels = dirty.Sum(r => (long)r.Width * r.Height);
        await Assert.That(pixels).IsLessThanOrEqualTo(2L * CpuComposer.TileSize * CpuComposer.TileSize);
        await Assert.That(pixels).IsGreaterThan(0L);

        incremental.RenderIncremental(UiFrameScene.Build(Width, Height, 1f, caret: true), Width, Height, out dirty);
        await Assert.That(dirty.Count).IsEqualTo(0).Because("an unchanged frame damages nothing");
    }

    [Test]
    [NotInParallel]
    [Arguments(1)]
    [Arguments(-1)]
    public async Task IncrementalFrameAllocatesNothing(int threads)
    {
        // A caret blink and a text edit (more tiles, so worker threads run too).
        var on = UiFrameScene.Build(Width, Height, 1f, caret: true);
        var off = UiFrameScene.Build(Width, Height, 1.25f, caret: false);
        using var pipeline = new Pipeline();
        pipeline.Composer.MaxDegreeOfParallelism = threads;
        for (int i = 0; i < 4; i++)
        {
            pipeline.BuildAndRenderIncremental(i % 2 == 0 ? on : off, Width, Height);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 6; i++)
        {
            pipeline.BuildAndRenderIncremental(i % 2 == 0 ? on : off, Width, Height);
        }
        await Assert.That(GC.GetAllocatedBytesForCurrentThread() - before).IsEqualTo(0L);
    }

    private static int FirstDifference(byte[] a, byte[] b)
    {
        int n = a.AsSpan().CommonPrefixLength(b);
        return n == a.Length && a.Length == b.Length ? -1 : n / 4;
    }

    private sealed class Pipeline : IDisposable
    {
        private readonly DrawListBuilder builder = new();
        private readonly DrawList list = new();
        private readonly CpuFramebuffer framebuffer = new();

        public CpuComposer Composer { get; } = new();

        public byte[] RenderIncremental(DrawRecording recording, int width, int height, out List<CpuDirtyRect> dirty)
        {
            Build(recording, width, height);
            dirty = Composer.RenderIncremental(list, framebuffer).ToArray().ToList();
            return Rgba(width, height);
        }

        public byte[] RenderFull(DrawRecording recording, int width, int height)
        {
            Build(recording, width, height);
            Composer.Render(list, framebuffer);
            return Rgba(width, height);
        }

        public void BuildAndRenderIncremental(DrawRecording recording, int width, int height)
        {
            Build(recording, width, height);
            Composer.RenderIncremental(list, framebuffer);
        }

        private void Build(DrawRecording recording, int width, int height)
        {
            builder.Begin(list, (uint)width, (uint)height, Composer.Masks, Composer.MonoAtlas, Composer.ColorAtlas);
            builder.Replay(recording);
            builder.End();
            list.Parameters = new ComposeParameters { TextGamma = 1.5f, LightWeight = 1f };
        }

        private byte[] Rgba(int width, int height)
        {
            var rgba = new byte[width * height * 4];
            framebuffer.CopyToRgba(rgba);
            return rgba;
        }

        public void Dispose() => Composer.Dispose();
    }
}
