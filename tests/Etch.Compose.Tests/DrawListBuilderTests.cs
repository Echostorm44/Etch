using Etch.Compose.Coverage;
using Etch.Geometry;
using Etch.Gpu;
using Etch.Scene;
using Etch.Text.Atlas;

namespace Etch.Compose.Tests;

/// <summary>
/// The replay rules: which primitive each draw becomes under which transform, how the clip stack
/// normalizes into clip-table entries, how layers are offset and faded, how gradients map, and that
/// masks are rasterized once and reused.
/// </summary>
internal sealed class DrawListBuilderTests
{
    private static readonly ComposeColor Red = new(1, 0, 0, 1);

    private sealed class Harness : IDisposable
    {
        public readonly DrawList List = new();
        public readonly DrawListBuilder Builder = new();
        public readonly MaskAtlas Masks = new();
        public readonly GlyphAtlas Mono = new(1024, TextureFormat.R8Unorm, 128, 1);
        public readonly GlyphAtlas Color = new(1024, TextureFormat.Rgba8UnormSrgb, 128, 1);

        public DrawList Build(DrawRecording recording, uint width = 200, uint height = 100)
        {
            Builder.Begin(List, width, height, Masks, Mono, Color);
            Builder.Replay(recording);
            Builder.End();
            return List;
        }

        public void Dispose()
        {
            Masks.Dispose();
            Mono.Dispose();
            Color.Dispose();
        }
    }

    private static DrawList BuildOnce(DrawRecording recording)
    {
        using var h = new Harness();
        return h.Build(recording);
    }

    [Test]
    public async Task AxisAlignedRect_StaysAHardEdgedRect_InDevicePixels()
    {
        var rec = new DrawRecording();
        rec.SetTransform(new Affine(2, 0, 0, 2, 10, 5));
        rec.FillRect(1, 2, 10, 4, ComposePaint.Solid(Red));

        var list = BuildOnce(rec);

        await Assert.That(list.Shapes.Count).IsEqualTo(1);
        var s = list.Shapes[0];
        await Assert.That(s.Type).IsEqualTo(ShapeType.Rect);
        await Assert.That(s.P0 == 12 && s.P1 == 9 && s.P2 == 32 && s.P3 == 17).IsTrue();
        await Assert.That(s.MinX == 12 && s.MaxX == 32).IsTrue();
    }

    [Test]
    public async Task RotatedRoundedRect_IsAnalyticInItsLocalFrame()
    {
        var rec = new DrawRecording();
        rec.SetTransform(Affine.Translate(100, 50) * Affine.Rotate(0.4));
        rec.FillRoundedRect(-20, -10, 40, 20, 5, ComposePaint.Solid(Red));

        var list = BuildOnce(rec);

        var s = list.Shapes[0];
        await Assert.That(s.Type).IsEqualTo(ShapeType.RoundedRect);
        await Assert.That(Math.Abs(s.Scale - 1f)).IsLessThan(1e-5f);
        // The device centre maps back to the local origin.
        float lx = s.FrameA * 100 + s.FrameB * 50 + s.FrameTx;
        float ly = s.FrameC * 100 + s.FrameD * 50 + s.FrameTy;
        await Assert.That(Math.Abs(lx) + Math.Abs(ly)).IsLessThan(1e-3f);
    }

    [Test]
    public async Task SkewedCircle_BecomesAMask()
    {
        var rec = new DrawRecording();
        rec.SetTransform(new Affine(2, 0.5, 0, 1, 50, 50));
        rec.FillCircle(0, 0, 10, ComposePaint.Solid(Red));

        using var h = new Harness();
        var list = h.Build(rec);

        await Assert.That(list.Shapes.Count).IsGreaterThan(0);
        await Assert.That(list.Shapes.All(s => s.Type == ShapeType.Mask)).IsTrue();
        await Assert.That(h.Masks.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task ClipStack_NormalizesToRectRoundOrMask()
    {
        var rec = new DrawRecording();
        rec.SetTransform(Affine.Identity);
        rec.PushClipRect(10, 10, 100, 60);
        rec.FillRect(0, 0, 200, 100, ComposePaint.Solid(Red));
        rec.PushClipRoundedRect(20, 20, 50, 30, 8);
        rec.FillRect(0, 0, 200, 100, ComposePaint.Solid(Red));
        rec.PushClipRoundedRect(30, 25, 50, 30, 8);
        rec.FillRect(0, 0, 200, 100, ComposePaint.Solid(Red));
        rec.PopClip();
        rec.PopClip();
        rec.FillRect(0, 0, 200, 100, ComposePaint.Solid(Red));
        rec.PopClip();

        var list = BuildOnce(rec);
        var clips = list.Clips.ToArray();
        var shapes = list.Shapes;

        var rectOnly = clips[(int)shapes[0].ClipIndex];
        await Assert.That(rectOnly.MinX == 10 && rectOnly.MaxX == 110 && rectOnly.HasRound == 0 && rectOnly.HasMask == 0).IsTrue();
        var round = clips[(int)shapes[1].ClipIndex];
        await Assert.That(round.HasRound == 1 && round.HasMask == 0 && round.RoundRadius == 8).IsTrue();
        var twoRounds = clips[(int)shapes[2].ClipIndex];
        await Assert.That(twoRounds.HasMask).IsEqualTo(1u);
        // Popping restores the rect-only clip (the same entry, not a new one).
        await Assert.That(shapes[3].ClipIndex).IsEqualTo(shapes[0].ClipIndex);
        // Quads never extend past the hard clip rect.
        await Assert.That(shapes.All(s => s.MinX >= 10 && s.MaxX <= 110)).IsTrue();
    }

    [Test]
    public async Task LayerReplay_OffsetsGeometryAndFades()
    {
        var layer = new DrawRecording();
        layer.SetTransform(Affine.Identity);
        layer.FillRect(10, 10, 20, 20, ComposePaint.Solid(Red));
        layer.BackdropBlur(5, 5, 10, 10, 2, 3, Red);

        var main = new DrawRecording();
        main.SetTransform(Affine.Identity);
        main.Layer(layer, 7, -3, 0.5f);

        var list = BuildOnce(main);

        var s = list.Shapes[0];
        await Assert.That(s.P0 == 17 && s.P1 == 7).IsTrue();
        await Assert.That(s.A0).IsEqualTo(0.5f);
        var b = list.Blurs[0];
        await Assert.That(b.MinX == 12 && b.MinY == 2 && b.Opacity == 0.5f).IsTrue();
    }

    [Test]
    public async Task LinearGradient_MapsLocalEndpointsThroughTheTransform()
    {
        var rec = new DrawRecording();
        rec.SetTransform(new Affine(2, 0, 0, 2, 10, 0));
        var stops = new[] { new ComposeGradientStop(0, Red), new ComposeGradientStop(1, new ComposeColor(0, 0, 1, 1)) };
        rec.FillRect(0, 0, 50, 10, ComposePaint.Linear(0, 0, 50, 0, stops));

        var list = BuildOnce(rec);

        var g = list.Gradients[(int)list.Shapes[0].PaintIndex];
        // Device x = 10 → t 0, device x = 110 → t 1.
        await Assert.That(Math.Abs(g.A * 10 + g.Tx)).IsLessThan(1e-5f);
        await Assert.That(Math.Abs(g.A * 110 + g.Tx - 1)).IsLessThan(1e-5f);
        await Assert.That(g.StopCount).IsEqualTo(2u);
        // Stops are premultiplied.
        var stop = list.GradientStops[(int)g.StopStart];
        await Assert.That(stop.R == 1 && stop.A == 1).IsTrue();
    }

    [Test]
    public async Task PathMasks_AreRasterizedOncePerFrameAndReusedAcrossFrames()
    {
        var builder = BezPathBuilder.Begin(8);
        builder.MoveTo(new Point(10, 10));
        builder.LineTo(new Point(60, 15));
        builder.LineTo(new Point(30, 70));
        builder.Close();
        var path = builder.Build();
        var rec = new DrawRecording();
        rec.SetTransform(Affine.Identity);
        rec.FillPath(path, FillRule.NonZero, ComposePaint.Solid(Red));
        rec.FillPath(path, FillRule.NonZero, ComposePaint.Solid(Red));

        using var h = new Harness();
        h.Build(rec);
        int afterFirstFrame = h.Masks.Count;
        h.Build(rec);

        await Assert.That(afterFirstFrame).IsEqualTo(1);
        await Assert.That(h.Masks.Count).IsEqualTo(1);
    }

    [Test]
    public async Task UnbalancedRecording_DoesNotLeakClipsIntoItsCaller()
    {
        var layer = new DrawRecording();
        layer.SetTransform(Affine.Identity);
        layer.PushClipRect(0, 0, 5, 5);

        var main = new DrawRecording();
        main.SetTransform(Affine.Identity);
        main.Layer(layer, 0, 0, 1);
        main.FillRect(0, 0, 50, 50, ComposePaint.Solid(Red));

        var list = BuildOnce(main);

        await Assert.That(list.Shapes[0].ClipIndex).IsEqualTo(0u);
    }
}
