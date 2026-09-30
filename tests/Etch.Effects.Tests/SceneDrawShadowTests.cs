using Etch.Geometry;
using Etch.Scene;
using Etch.Testing;
using TUnit;

namespace Etch.Effects.Tests;

/// <summary>
/// <see cref="SceneOpcode.DrawShadow"/> through the CPU renderer (analytic, via <see cref="ShadowShape"/>).
/// </summary>
public sealed class SceneDrawShadowTests
{
    private const int Size = 200;

    [Test]
    public async Task RectShadow_Offset10_Blur8_DarkensCentroid()
    {
        // EFF-007 acceptance 1: 100×100 rect, offset (10, 10), blur 8 → centroid ≥ 60/255.
        byte[] rgba = Render((ref SceneBuilder sb) => DrawShadow(ref sb, new Rect(40, 40, 140, 140), new Vec2(10, 10), 8f));

        await Assert.That(Alpha(rgba, 100, 100)).IsGreaterThanOrEqualTo(60);
        // The falloff is centred on the offset shape: its left edge (x = 50) is about half covered,
        // and nothing reaches 3σ past it.
        await Assert.That(Math.Abs(Alpha(rgba, 50, 100) - 128)).IsLessThan(12);
        await Assert.That(Alpha(rgba, 50 - 25, 100)).IsLessThan(3);
    }

    [Test]
    public async Task ZeroBlur_IsSharpOffsetCopy()
    {
        byte[] rgba = Render((ref SceneBuilder sb) => DrawShadow(ref sb, new Rect(40, 40, 140, 140), new Vec2(10, 10), 0f));

        await Assert.That(Alpha(rgba, 51, 100)).IsGreaterThan(250);
        await Assert.That(Alpha(rgba, 48, 100)).IsLessThan(5);
    }

    [Test]
    public async Task Shadow_RespectsClip()
    {
        byte[] rgba = Render((ref SceneBuilder sb) =>
        {
            var clip = BezPathBuilder.Begin(6);
            clip.MoveTo(new Point(0, 0));
            clip.LineTo(new Point(100, 0));
            clip.LineTo(new Point(100, Size));
            clip.LineTo(new Point(0, Size));
            clip.Close();
            sb.PushClip(sb.AddPath(clip.Build()), FillRule.NonZero);
            DrawShadow(ref sb, new Rect(40, 40, 160, 160), default, 4f);
            sb.PopClip();
        });

        await Assert.That(Alpha(rgba, 90, 100)).IsGreaterThan(240);
        await Assert.That(Alpha(rgba, 110, 100)).IsEqualTo(0);
    }

    // SceneBuilder is a struct: pass it by ref or the commands land in a copy.
    private delegate void DrawScene(ref SceneBuilder sb);

    private static void DrawShadow(ref SceneBuilder sb, Rect rect, Vec2 offset, float sigma)
    {
        var path = BezPathBuilder.Begin(6);
        path.MoveTo(new Point(rect.MinX, rect.MinY));
        path.LineTo(new Point(rect.MaxX, rect.MinY));
        path.LineTo(new Point(rect.MaxX, rect.MaxY));
        path.LineTo(new Point(rect.MinX, rect.MaxY));
        path.Close();
        int pathId = sb.AddPath(path.Build());
        int paintId = sb.AddPaint(Paint.Solid(0xFF000000));
        sb.DrawShadow(pathId, paintId, 0, offset, sigma, 0xFF000000);
    }

    private static byte[] Render(DrawScene draw)
    {
        var sb = SceneBuilder.Begin(256);
        sb.BeginFrame();
        sb.AddTransform(Affine.Identity);
        draw(ref sb);
        sb.EndFrame();
        using var scene = sb.End();
        return SceneCpuRenderer.RenderToRgba8(scene, Size, Size);
    }

    private static int Alpha(byte[] rgba, int x, int y) => rgba[(y * Size + x) * 4 + 3];
}
