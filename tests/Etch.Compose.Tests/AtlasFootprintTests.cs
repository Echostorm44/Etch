using Etch.Bench.Compose;
using Etch.Compose.Cpu;
using Etch.Geometry;
using Etch.Scene;

namespace Etch.Compose.Tests;

/// <summary>
/// The composers' resident memory follows what they draw: a fresh composer holds small glyph
/// atlases and no mask page or frame buffers, a small surface of text stays in them, atlases grow
/// (mid-frame, invisibly) when a frame needs more, the GPU composer's uploads are batched and
/// flushed with the frame, and trimming or disposing releases the memory.
/// </summary>
[NotInParallel(nameof(AtlasFootprintTests))]
internal sealed class AtlasFootprintTests
{
    private static readonly ComposeParameters Parameters = new() { TextGamma = 1.5f, LightWeight = 1f };

    private static ComposeColor Gray(float v, float a = 1f) => new(v, v, v, a);

    // A tray menu: nine rows of 14 px text with shortcuts, two separators, a highlight.
    private static DrawRecording Menu(int width, int height)
    {
        var rec = new DrawRecording();
        rec.FillRoundedRect(0, 0, width, height, 8, ComposePaint.Solid(Gray(0.95f)));
        rec.Border(0, 0, width, height, 8, 1, Gray(0f, 0.1f));
        string[] items = ["Open", "Settings…", "Pause monitoring", "Clear history", "Pinned items", "Start with Windows", "Check for updates", "About", "Quit"];
        float y = 8;
        for (int i = 0; i < items.Length; i++)
        {
            if (i == 3 || i == 7)
            {
                rec.FillRect(8, y + 4, width - 16, 1, ComposePaint.Solid(Gray(0f, 0.1f)));
                y += 9;
            }
            if (i == 1)
            {
                rec.FillRoundedRect(6, y, width - 12, 32, 6, ComposePaint.Solid(new ComposeColor(0.004f, 0.23f, 1f, 0.14f)));
            }
            rec.Glyphs(UiFrameScene.Run(items[i], 16, y + 21, Gray(0.01f), 1f, 14));
            rec.Glyphs(UiFrameScene.Run("Ctrl+" + (char)('A' + i), width - 90, y + 21, Gray(0.15f), 1f, 12));
            y += 36;
        }
        return rec;
    }

    // Text at many sizes: several hundred distinct glyph bitmaps, more than the initial atlas holds.
    private static DrawRecording ManySizes(int width, int height, string text = "Sphinx of black quartz, judge my vow 0123456789", int firstSize = 9)
    {
        var rec = new DrawRecording();
        rec.FillRect(0, 0, width, height, ComposePaint.Solid(Gray(1f)));
        float y = 20;
        for (int size = firstSize; size <= firstSize + 31; size += 3)
        {
            rec.Glyphs(UiFrameScene.Run(text, 10, y + size, Gray(0.02f), 1f, size));
            y += size + 6;
        }
        return rec;
    }

    private static byte[] RenderCpu(CpuComposer composer, DrawRecording recording, int width, int height, CpuFramebuffer? framebuffer = null)
    {
        var builder = new DrawListBuilder();
        var list = new DrawList();
        framebuffer ??= new CpuFramebuffer();
        composer.ResetAtlasesIfExhausted(false);
        builder.Begin(list, (uint)width, (uint)height, composer.Masks, composer.MonoAtlas, composer.ColorAtlas);
        builder.Replay(recording);
        builder.End();
        list.Parameters = Parameters;
        composer.Render(list, framebuffer);
        var rgba = new byte[width * height * 4];
        framebuffer.CopyToRgba(rgba);
        return rgba;
    }

    [Test]
    public async Task FreshCpuComposer_HoldsOnlySmallAtlases()
    {
        using var composer = new CpuComposer();
        var usage = composer.MemoryUsage();

        await Assert.That(usage.MonoAtlasBytes).IsEqualTo((long)CpuComposer.InitialGlyphAtlasSize * CpuComposer.InitialGlyphAtlasSize);
        await Assert.That(usage.ColorAtlasBytes).IsEqualTo((long)CpuComposer.InitialColorGlyphAtlasSize * CpuComposer.InitialColorGlyphAtlasSize * 4);
        await Assert.That(usage.MaskAtlasBytes).IsEqualTo(0L);
        await Assert.That(usage.FrameBytes).IsEqualTo(0L);
        await Assert.That(usage.TotalBytes).IsLessThanOrEqualTo(128L * 1024);
    }

    [Test]
    public async Task SmallMenu_StaysInTheInitialAtlases_AndAllocatesNoBlurSnapshot()
    {
        const int width = 300, height = 450;
        using var composer = new CpuComposer { MaxDegreeOfParallelism = 1 };
        RenderCpu(composer, Menu(width, height), width, height);

        var usage = composer.MemoryUsage();
        await Assert.That(composer.MonoAtlas.Dimension).IsEqualTo(CpuComposer.InitialGlyphAtlasSize);
        await Assert.That(composer.MonoAtlas.GlyphCount).IsGreaterThan(30);
        await Assert.That(composer.ColorAtlas.Dimension).IsEqualTo(CpuComposer.InitialColorGlyphAtlasSize);
        // Bins and scratch only: the whole-frame copy is for backdrop blurs, and the menu has none.
        await Assert.That(usage.FrameBytes).IsLessThan((long)width * height);
        await Assert.That(usage.TotalBytes).IsLessThanOrEqualTo(256L * 1024);
    }

    [Test]
    public async Task CpuAtlasGrowingMidFrame_ChangesNoPixel()
    {
        const int width = 900, height = 520;
        var scene = ManySizes(width, height);

        // A fresh composer grows its atlas while this frame is built...
        using var fresh = new CpuComposer();
        byte[] grown = RenderCpu(fresh, scene, width, height);
        await Assert.That(fresh.MonoAtlas.Dimension).IsGreaterThan(CpuComposer.InitialGlyphAtlasSize);

        // ...and must draw what a composer whose atlas grew earlier (other glyphs, other places) draws.
        using var warmed = new CpuComposer();
        RenderCpu(warmed, ManySizes(width, height, "The five boxing wizards jump quickly! @#%", 10), width, height);
        int warmedDimension = warmed.MonoAtlas.Dimension;
        byte[] reference = RenderCpu(warmed, scene, width, height);

        await Assert.That(warmedDimension).IsGreaterThan(CpuComposer.InitialGlyphAtlasSize);
        await Assert.That(grown.AsSpan().SequenceEqual(reference)).IsTrue();
    }

    [Test]
    public async Task TrimGlyphAtlases_AndDispose_ReleaseTheCpuComposersMemory()
    {
        const int width = 900, height = 520;
        var composer = new CpuComposer();
        var framebuffer = new CpuFramebuffer();
        byte[] before = RenderCpu(composer, ManySizes(width, height), width, height, framebuffer);
        await Assert.That(composer.MonoAtlas.Dimension).IsGreaterThan(CpuComposer.InitialGlyphAtlasSize);

        composer.TrimGlyphAtlases();
        composer.Trim();
        await Assert.That(composer.MonoAtlas.Dimension).IsEqualTo(CpuComposer.InitialGlyphAtlasSize);
        await Assert.That(composer.MemoryUsage().FrameBytes).IsEqualTo(0L);

        // The next frame rasterizes its glyphs again and draws the same image.
        byte[] after = RenderCpu(composer, ManySizes(width, height), width, height, framebuffer);
        await Assert.That(after.AsSpan().SequenceEqual(before)).IsTrue();

        composer.Dispose();
        await Assert.That(composer.MemoryUsage().TotalBytes).IsEqualTo(0L);
    }

    [Test]
    public async Task FreshGpuComposer_HoldsOnlySmallAtlases()
    {
        const uint width = 300, height = 450;
        using var harness = ParityHarness.TryCreate(width, height, ParityAdapter.Reference, out string reason);
        if (harness is null)
        {
            Skip.Test($"Needs the reference adapter: {reason}");
            return;
        }
        var usage = harness.Gpu.ResourceUsage();
        long atlases = (long)GpuComposer.InitialGlyphAtlasSize * GpuComposer.InitialGlyphAtlasSize
            + (long)GpuComposer.InitialColorGlyphAtlasSize * GpuComposer.InitialColorGlyphAtlasSize * 4;
        // The atlases, the 1×1 stand-in mask and the framebuffer copy glyphs read their background from.
        await Assert.That(usage.TextureBytes).IsEqualTo(atlases + 1 + (long)width * height * 4);
    }

    [Test]
    public async Task GpuAtlasGrowingMidFrame_ChangesNoPixel_AndUploadsAreFlushed()
    {
        const uint width = 900, height = 520;
        using var harness = ParityHarness.TryCreate(width, height, ParityAdapter.Reference, out string reason);
        if (harness is null)
        {
            Skip.Test($"Needs the reference adapter: {reason}");
            return;
        }
        var scene = ManySizes((int)width, (int)height);
        // Paths and a rounded clip as well: the mask atlas grows and batches its uploads too.
        var layered = new DrawRecording();
        layered.Layer(scene, 0, 0, 1f);
        layered.PushClipRoundedRect(600, 40, 260, 400, 30);
        for (int i = 0; i < 12; i++)
        {
            layered.FillPath(Star(640 + i % 4 * 60, 80 + i / 4 * 120, 26, 10, 5 + i % 3), FillRule.NonZero, ComposePaint.Solid(new ComposeColor(1f, 0.4f, 0f, 1f)));
        }
        layered.PopClip();

        byte[] first = harness.RenderGpu(layered, width, height, Parameters);
        int grownTo = harness.Gpu.MonoAtlas.Dimension;
        await Assert.That(grownTo).IsGreaterThan(GpuComposer.InitialGlyphAtlasSize);
        await Assert.That(harness.Gpu.Masks.PageCount).IsGreaterThan(0);
        await Assert.That(harness.Gpu.MonoAtlas.PendingUploads).IsEqualTo(0);
        await Assert.That(harness.Gpu.Masks.PendingUploads).IsEqualTo(0);

        // The second frame finds every glyph and mask cached: nothing grows, nothing uploads.
        byte[] second = harness.RenderGpu(layered, width, height, Parameters);
        await Assert.That(harness.Gpu.MonoAtlas.Dimension).IsEqualTo(grownTo);
        await Assert.That(first.AsSpan().SequenceEqual(second)).IsTrue();

        // And the CPU composer, growing in its own first frame, matches as every parity scene does.
        byte[] cpu = harness.RenderCpu(layered, width, height, Parameters);
        await Assert.That(ParityStats.Compare(first, cpu, (int)width, (int)height).Max).IsLessThanOrEqualTo(4);

        // Trimmed atlases rebuild the same frame.
        harness.Gpu.TrimGlyphAtlases();
        harness.Gpu.Trim();
        await Assert.That(harness.Gpu.MonoAtlas.Dimension).IsEqualTo(GpuComposer.InitialGlyphAtlasSize);
        byte[] rebuilt = harness.RenderGpu(layered, width, height, Parameters);
        await Assert.That(rebuilt.AsSpan().SequenceEqual(first)).IsTrue();
    }

    private static BezPath Star(double cx, double cy, double outer, double inner, int points)
    {
        var b = BezPathBuilder.Begin(points * 2 + 2);
        for (int i = 0; i < points * 2; i++)
        {
            double angle = Math.PI * i / points - Math.PI / 2;
            double r = i % 2 == 0 ? outer : inner;
            var p = new Point(cx + r * Math.Cos(angle), cy + r * Math.Sin(angle));
            if (i == 0)
            {
                b.MoveTo(p);
            }
            else
            {
                b.LineTo(p);
            }
        }
        b.Close();
        return b.Build();
    }
}
