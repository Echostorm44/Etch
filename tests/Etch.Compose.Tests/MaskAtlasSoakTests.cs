using Etch.Compose.Cpu;
using Etch.Geometry;
using Etch.Scene;

namespace Etch.Compose.Tests;

/// <summary>
/// The mask atlas stays bounded through long scrolls and animations: scrolled content reuses its
/// masks (keys are anchored to whole pixels), masks no frame uses are evicted, and the atlas shrinks
/// back to its page budget. Steady scrolling allocates nothing.
/// </summary>
[NotInParallel]
internal sealed class MaskAtlasSoakTests
{
    private const int Width = 640;
    private const int Height = 480;

    // Scrolled content full of masks: star paths (fills), stroked paths, and nested rounded clips
    // (a combined clip mask), 40 rows tall.
    private static DrawRecording Content()
    {
        var layer = new DrawRecording();
        layer.SetTransform(Affine.Identity);
        for (int row = 0; row < 40; row++)
        {
            float y = row * 60;
            layer.FillPath(Star(40, y + 30, 22, 9, 5 + row % 3), FillRule.NonZero, ComposePaint.Solid(new ComposeColor(1, 0.5f, 0, 1)));
            layer.PushClipRoundedRect(100, y + 5, 300, 50, 14);
            layer.PushClipRoundedRect(120, y, 300, 60, 25);
            layer.FillRect(0, y, Width, 60, ComposePaint.Solid(new ComposeColor(0.2f, 0.4f, 1, 1)));
            layer.PopClip();
            layer.PopClip();
        }
        return layer;
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

    [Test]
    public async Task LongScrollAndAnimation_KeepTheAtlasBounded()
    {
        var content = Content();
        using var composer = new CpuComposer();
        var builder = new DrawListBuilder();
        var list = new DrawList();
        var frame = new CpuFramebuffer();
        var main = new DrawRecording();

        void Render(float offset)
        {
            main.Clear();
            main.SetTransform(Affine.Identity);
            main.FillRect(0, 0, Width, Height, ComposePaint.Solid(new ComposeColor(1, 1, 1, 1)));
            main.Layer(content, 0, offset, 1f);
            composer.ResetAtlasesIfExhausted(force: false);
            builder.Begin(list, Width, Height, composer.Masks, composer.MonoAtlas, composer.ColorAtlas);
            builder.Replay(main);
            builder.End();
            list.Parameters = new ComposeParameters { TextGamma = 1.5f, LightWeight = 1f };
            composer.RenderIncremental(list, frame);
        }

        // Whole-pixel scrolling, down and back, twice: after the first pass every mask is reused.
        int maxPages = 0;
        for (int pass = 0; pass < 2; pass++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread() + composer.WorkerAllocatedBytes;
            for (int i = 0; i < 400; i++)
            {
                Render(-(i < 200 ? i * 9 : (400 - i) * 9));
                maxPages = Math.Max(maxPages, composer.Masks.PageCount);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() + composer.WorkerAllocatedBytes - before;
            if (pass == 1)
            {
                await Assert.That(allocated).IsEqualTo(0L).Because("a second scroll over the same content reuses every mask");
            }
        }
        await Assert.That(maxPages).IsLessThanOrEqualTo(MaskAtlas.BudgetPages);

        // Fractional animation: every frame's offset is new, so masks are re-rasterized; the atlas
        // must evict rather than grow.
        for (int i = 0; i < 1000; i++)
        {
            Render(-i * 1.37f);
            maxPages = Math.Max(maxPages, composer.Masks.PageCount);
        }
        await Assert.That(maxPages).IsLessThanOrEqualTo(MaskAtlas.BudgetPages);
        // Evicted masks leave: the atlas holds at most half a page of masks the frame no longer uses.
        await Assert.That(composer.Masks.Count).IsLessThan(2000);

        // Hidden: Trim releases the pages.
        composer.Masks.Trim();
        await Assert.That(composer.Masks.PageCount).IsEqualTo(0);
    }
}
