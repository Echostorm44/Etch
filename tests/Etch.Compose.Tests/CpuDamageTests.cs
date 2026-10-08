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
    public async Task TwoNearbyBlurs_FollowEachOthersDamage()
    {
        // Blur B is drawn first; a change beside blur A (drawn later) dirties tiles in A's reach,
        // some of which B covers — so B's whole reach must render again too, or B reads its
        // clean neighbours' final pixels instead of their state at B's phase (a seam).
        using var incremental = new Pipeline();
        using var full = new Pipeline();
        for (int frame = 0; frame < 4; frame++)
        {
            var rec = new DrawRecording();
            rec.SetTransform(Affine.Identity);
            rec.FillRect(0, 0, 640, 320, ComposePaint.Solid(new ComposeColor(1, 1, 1, 1)));
            for (int i = 0; i < 40; i++)
            {
                rec.FillRect(i * 16, 0, 8, 320, ComposePaint.Solid(new ComposeColor(i % 2, 0.3f, 1 - i % 2, 1)));
            }
            rec.BackdropBlur(60, 40, 150, 120, 10, 10, new ComposeColor(1, 1, 1, 0.1f));
            rec.FillRect(150, 10, 60, 20, ComposePaint.Solid(new ComposeColor(0, 0, 0, 1)));
            rec.BackdropBlur(230, 40, 150, 120, 10, 10, new ComposeColor(1, 1, 1, 0.1f));
            rec.FillRect(400 + frame * 9, 60, 40, 40, ComposePaint.Solid(new ComposeColor(1, 0.8f, 0, 1)));
            byte[] a = incremental.RenderIncremental(rec, 640, 320, out _);
            byte[] b = full.RenderFull(rec, 640, 320);
            await Assert.That(FirstDifference(a, b)).IsEqualTo(-1).Because($"frame {frame}");
        }
    }

    [Test]
    public async Task FrameThatThrows_DoesNotCorruptTheHistory()
    {
        using var incremental = new Pipeline();
        using var full = new Pipeline();
        // One thread: the tiles after the failing one are never rendered.
        incremental.Composer.MaxDegreeOfParallelism = 1;
        var good = UiFrameScene.Build(Width, Height, 1f, caret: true);
        incremental.RenderIncremental(good, Width, Height, out _);

        // An image whose pixel array is shorter than its size: sampling it throws mid-render.
        var broken = UiFrameScene.Build(Width, Height, 1f, caret: false, titleVariant: 3);
        broken.Image(99, new ComposeImage(new byte[16], 64, 64), 300, 300, 200, 200, 1f);
        // A change in a tile rendered after the failing one (tiles render in order on one thread).
        broken.FillRect(1200, 680, 40, 20, ComposePaint.Solid(new ComposeColor(1, 0, 0, 1)));
        await Assert.That(() => incremental.RenderIncremental(broken, Width, Height, out _)).Throws<Exception>();

        var next = UiFrameScene.Build(Width, Height, 1f, caret: false, titleVariant: 3);
        next.FillRect(1200, 680, 40, 20, ComposePaint.Solid(new ComposeColor(1, 0, 0, 1)));
        byte[] a = incremental.RenderIncremental(next, Width, Height, out _);
        byte[] b = full.RenderFull(next, Width, Height);
        await Assert.That(FirstDifference(a, b)).IsEqualTo(-1);
    }

    [Test]
    public async Task ZeroSizedFrame_RendersNothing()
    {
        using var pipeline = new Pipeline();
        byte[] rgba = pipeline.RenderIncremental(UiFrameScene.Build(Width, Height, 1f), Width, 0, out var dirty);
        await Assert.That(dirty.Count).IsEqualTo(0);
        await Assert.That(rgba.Length).IsEqualTo(0);
    }

    [Test]
    public async Task Trim_ReleasesTheHistory_NextFrameIsFull()
    {
        using var incremental = new Pipeline();
        using var full = new Pipeline();
        var frame = UiFrameScene.Build(Width, Height, 1f, caret: true);
        incremental.RenderIncremental(frame, Width, Height, out _);
        incremental.Composer.Trim();
        byte[] a = incremental.RenderIncremental(frame, Width, Height, out var dirty);
        await Assert.That(dirty.Sum(r => (long)r.Width * r.Height)).IsEqualTo((long)Width * Height);
        await Assert.That(FirstDifference(a, full.RenderFull(frame, Width, Height))).IsEqualTo(-1);
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
        // This thread's and the composer's worker threads' allocations (other threads in the test
        // process allocate too, so a process-wide count would be flaky).
        long before = GC.GetAllocatedBytesForCurrentThread() + pipeline.Composer.WorkerAllocatedBytes;
        for (int i = 0; i < 6; i++)
        {
            pipeline.BuildAndRenderIncremental(i % 2 == 0 ? on : off, Width, Height);
        }
        long after = GC.GetAllocatedBytesForCurrentThread() + pipeline.Composer.WorkerAllocatedBytes;
        await Assert.That(after - before).IsEqualTo(0L);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DamageRects_CoverExactlyTheChangedTiles(bool checkerboard)
    {
        // Every other tile column changes (stripes merge into full-height rects), or a checkerboard
        // (nothing merges): the rects are disjoint and are the changed tiles, no more, no fewer.
        const int Tile = CpuComposer.TileSize;
        int tilesX = Width / Tile;
        int tilesY = (Height + Tile - 1) / Tile;
        using var pipeline = new Pipeline();
        var white = new DrawRecording();
        white.SetTransform(Affine.Identity);
        white.FillRect(0, 0, Width, Height, ComposePaint.Solid(new ComposeColor(1, 1, 1, 1)));
        pipeline.RenderIncremental(white, Width, Height, out _);

        var changed = new DrawRecording();
        changed.SetTransform(Affine.Identity);
        changed.FillRect(0, 0, Width, Height, ComposePaint.Solid(new ComposeColor(1, 1, 1, 1)));
        var expected = new bool[tilesX * tilesY];
        for (int ty = 0; ty < tilesY; ty++)
        {
            for (int tx = 0; tx < tilesX; tx++)
            {
                if ((checkerboard ? tx + ty : tx) % 2 == 0)
                {
                    expected[ty * tilesX + tx] = true;
                    changed.FillRect(tx * Tile + 4, ty * Tile + 4, 6, 6, ComposePaint.Solid(new ComposeColor(1, 0, 0, 1)));
                }
            }
        }
        pipeline.RenderIncremental(changed, Width, Height, out var dirty);

        var covered = new int[tilesX * tilesY];
        foreach (var rect in dirty)
        {
            for (int y = rect.Y; y < rect.Y + rect.Height; y += Tile)
            {
                for (int x = rect.X; x < rect.X + rect.Width; x += Tile)
                {
                    covered[(y / Tile) * tilesX + x / Tile]++;
                }
            }
        }
        for (int i = 0; i < covered.Length; i++)
        {
            await Assert.That(covered[i]).IsEqualTo(expected[i] ? 1 : 0).Because($"tile {i}");
        }
        await Assert.That(dirty.Count).IsEqualTo(checkerboard ? expected.Count(e => e) : tilesX / 2);
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
