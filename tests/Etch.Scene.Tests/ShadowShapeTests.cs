using System;
using System.Threading.Tasks;
using Etch.Geometry;

namespace Etch.Scene.Tests;

internal sealed class ShadowShapeTests
{
    [Test]
    public async Task Resolve_Rect_HasNoCorner()
    {
        var b = BezPathBuilder.Begin(6);
        b.MoveTo(new Point(10, 20));
        b.LineTo(new Point(110, 20));
        b.LineTo(new Point(110, 70));
        b.LineTo(new Point(10, 70));
        b.Close();

        await Assert.That(ShadowShape.TryResolve(b.Build(), out var rect, out double corner)).IsTrue();
        await Assert.That(rect).IsEqualTo(new Rect(10, 20, 110, 70));
        await Assert.That(corner).IsEqualTo(0.0);
    }

    [Test]
    public async Task Resolve_RoundedRect_RecoversCorner()
    {
        // Same construction as CascadeUI's rounded rect: line, corner cubic, ... starting at (x + r, y).
        const double x = 0, y = 0, w = 100, h = 60, r = 12, k = 0.5522847498 * r;
        var b = BezPathBuilder.Begin(10);
        b.MoveTo(new Point(x + r, y));
        b.LineTo(new Point(x + w - r, y));
        b.CubicTo(new Point(x + w - r + k, y), new Point(x + w, y + r - k), new Point(x + w, y + r));
        b.LineTo(new Point(x + w, y + h - r));
        b.CubicTo(new Point(x + w, y + h - r + k), new Point(x + w - r + k, y + h), new Point(x + w - r, y + h));
        b.LineTo(new Point(x + r, y + h));
        b.CubicTo(new Point(x + r - k, y + h), new Point(x, y + h - r + k), new Point(x, y + h - r));
        b.LineTo(new Point(x, y + r));
        b.CubicTo(new Point(x, y + r - k), new Point(x + r - k, y), new Point(x + r, y));
        b.Close();

        await Assert.That(ShadowShape.TryResolve(b.Build(), out var rect, out double corner)).IsTrue();
        await Assert.That(rect).IsEqualTo(new Rect(0, 0, 100, 60));
        await Assert.That(Math.Abs(corner - r)).IsLessThan(1e-9);
    }

    [Test]
    public async Task Resolve_Circle_CornerIsRadius()
    {
        const double cx = 50, cy = 50, r = 20, k = 0.5522847498 * r;
        var b = BezPathBuilder.Begin(6);
        b.MoveTo(new Point(cx + r, cy));
        b.CubicTo(new Point(cx + r, cy - k), new Point(cx + k, cy - r), new Point(cx, cy - r));
        b.CubicTo(new Point(cx - k, cy - r), new Point(cx - r, cy - k), new Point(cx - r, cy));
        b.CubicTo(new Point(cx - r, cy + k), new Point(cx - k, cy + r), new Point(cx, cy + r));
        b.CubicTo(new Point(cx + k, cy + r), new Point(cx + r, cy + k), new Point(cx + r, cy));
        b.Close();

        await Assert.That(ShadowShape.TryResolve(b.Build(), out var rect, out double corner)).IsTrue();
        await Assert.That(rect).IsEqualTo(new Rect(30, 30, 70, 70));
        await Assert.That(corner).IsEqualTo(20.0);
    }

    [Test]
    public async Task Coverage_InsideEdgeAndOutside()
    {
        var rect = new Rect(0, 0, 200, 200);
        const double sigma = 4;

        await Assert.That(ShadowShape.Coverage(rect, 0, sigma, 100, 100)).IsGreaterThan(0.99);
        await Assert.That(Math.Abs(ShadowShape.Coverage(rect, 0, sigma, 0, 100) - 0.5)).IsLessThan(0.01);
        await Assert.That(ShadowShape.Coverage(rect, 0, sigma, -ShadowShape.Extent(sigma), 100)).IsLessThan(0.005);
    }

    [Test]
    public async Task Coverage_ConservesArea()
    {
        // Blurring redistributes coverage without creating or losing any: the sum over the
        // 3σ-inflated bounds equals the shape's area (rounded corners remove r²(4 − π)).
        var rect = new Rect(20, 20, 80, 60);
        const double sigma = 6, corner = 10;
        double extent = ShadowShape.Extent(sigma);
        double sum = 0;
        for (double py = rect.MinY - extent; py < rect.MaxY + extent; py++)
        {
            for (double px = rect.MinX - extent; px < rect.MaxX + extent; px++)
            {
                sum += ShadowShape.Coverage(rect, corner, sigma, px + 0.5, py + 0.5);
            }
        }
        double area = rect.Width * rect.Height - corner * corner * (4 - Math.PI);

        await Assert.That(Math.Abs(sum - area) / area).IsLessThan(0.02);
    }
}
