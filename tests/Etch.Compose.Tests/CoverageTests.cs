using Etch.Compose.Coverage;
using Etch.Geometry;
using Etch.Scene;

namespace Etch.Compose.Tests;

/// <summary>
/// The exact-area rasterizer, the flattener, the stroker and shape recognition: coverage equals
/// geometric area (in both axes), fill rules behave, strokes have the area their caps, joins and
/// dashes imply.
/// </summary>
internal sealed class CoverageTests
{
    private static byte[] Rasterize(FlatPath path, int width, int height, FillRule rule = FillRule.NonZero)
    {
        var rasterizer = new CoverageRasterizer();
        rasterizer.Reset(width, height);
        rasterizer.AddPath(path, 0, 0);
        var coverage = new byte[width * height];
        rasterizer.Resolve(coverage, rule);
        return coverage;
    }

    private static double Area(byte[] coverage) => coverage.Sum(c => c / 255.0);

    private static FlatPath Rect(float x0, float y0, float x1, float y1)
    {
        var path = new FlatPath();
        path.MoveTo(x0, y0);
        path.LineTo(x1, y0);
        path.LineTo(x1, y1);
        path.LineTo(x0, y1);
        path.Close();
        return path;
    }

    [Test]
    public async Task FractionalRect_CoversExactlyItsAreaPerPixel()
    {
        var coverage = Rasterize(Rect(1.25f, 2.5f, 3.75f, 4f), 6, 6);

        // Pixel (1, 2): x overlap 0.75, y overlap 0.5.
        await Assert.That(coverage[2 * 6 + 1]).IsEqualTo((byte)Math.Round(0.375 * 255));
        // Pixel (2, 3): fully inside.
        await Assert.That(coverage[3 * 6 + 2]).IsEqualTo((byte)255);
        // Pixel (3, 2): x overlap 0.75, y overlap 0.5.
        await Assert.That(coverage[2 * 6 + 3]).IsEqualTo((byte)Math.Round(0.375 * 255));
        await Assert.That(Math.Abs(Area(coverage) - 2.5 * 1.5)).IsLessThan(0.02);
    }

    [Test]
    public async Task HalfPixelTallLine_HasHalfCoverage()
    {
        // A one-sample-per-row rasterizer drops this entirely (its row centre is outside it).
        var coverage = Rasterize(Rect(0f, 1f, 4f, 1.5f), 4, 3);

        for (int x = 0; x < 4; x++)
        {
            await Assert.That(coverage[1 * 4 + x]).IsEqualTo((byte)128);
            await Assert.That(coverage[x]).IsEqualTo((byte)0);
        }
    }

    [Test]
    public async Task Triangle_TotalCoverageEqualsArea()
    {
        var path = new FlatPath();
        path.MoveTo(1.3f, 0.7f);
        path.LineTo(18.1f, 3.9f);
        path.LineTo(6.6f, 15.2f);
        path.Close();

        var coverage = Rasterize(path, 20, 20);

        double area = Math.Abs((18.1 - 1.3) * (15.2 - 0.7) - (6.6 - 1.3) * (3.9 - 0.7)) / 2;
        await Assert.That(Math.Abs(Area(coverage) - area) / area).IsLessThan(0.002);
    }

    [Test]
    public async Task EdgesLeftOfTheMask_StillCountForPixelsRightOfThem()
    {
        var coverage = Rasterize(Rect(-10f, 0f, 2.5f, 2f), 4, 2);

        await Assert.That(coverage[0]).IsEqualTo((byte)255);
        await Assert.That(coverage[1]).IsEqualTo((byte)255);
        await Assert.That(coverage[2]).IsEqualTo((byte)128);
        await Assert.That(coverage[3]).IsEqualTo((byte)0);
    }

    [Test]
    public async Task OverlappingContours_NonZeroUnions_EvenOddCutsHoles()
    {
        var path = Rect(0, 0, 4, 4);
        path.MoveTo(1, 1);
        path.LineTo(3, 1);
        path.LineTo(3, 3);
        path.LineTo(1, 3);
        path.Close();

        var nonZero = Rasterize(path, 4, 4, FillRule.NonZero);
        var evenOdd = Rasterize(path, 4, 4, FillRule.EvenOdd);

        await Assert.That(nonZero[1 * 4 + 1]).IsEqualTo((byte)255);
        await Assert.That(evenOdd[1 * 4 + 1]).IsEqualTo((byte)0);
        await Assert.That(evenOdd[0]).IsEqualTo((byte)255);
    }

    [Test]
    public async Task Flattener_KeepsSubpathsSeparateAndWithinTolerance()
    {
        var builder = BezPathBuilder.Begin(16);
        builder.MoveTo(new Point(0, 0));
        builder.CubicTo(new Point(0, 50), new Point(100, 50), new Point(100, 0));
        builder.Close();
        builder.MoveTo(new Point(200, 0));
        builder.LineTo(new Point(210, 10));
        var path = builder.Build();

        var flat = new FlatPath();
        PathFlattener.Flatten(path, Affine.Identity, 0.1f, flat);

        await Assert.That(flat.Contours.Length).IsEqualTo(2);
        await Assert.That(flat.Contours[0].Closed).IsTrue();
        await Assert.That(flat.Contours[1].Closed).IsFalse();

        // Every flattened point lies on the curve (they are evaluated on it), and the chord midpoints
        // stay within the tolerance of the curve: check the apex (t = 0.5 → y = 37.5).
        float maxY = flat.Y.Slice(0, flat.Contours[0].Count).ToArray().Max();
        await Assert.That(Math.Abs(maxY - 37.5f)).IsLessThan(0.1f);
    }

    private static double StrokedArea(FlatPath input, StrokeParameters stroke, int size = 64)
    {
        var output = new FlatPath();
        new PathStroker().Stroke(input, stroke, 0.02f, output);
        return Area(Rasterize(output, size, size));
    }

    private static FlatPath Segment(float x0, float y0, float x1, float y1)
    {
        var path = new FlatPath();
        path.MoveTo(x0, y0);
        path.LineTo(x1, y1);
        path.EndContour(closed: false);
        return path;
    }

    [Test]
    [Arguments(StrokeCap.Butt, 40.0)]
    [Arguments(StrokeCap.Square, 44.0)]
    [Arguments(StrokeCap.Round, 40.0 + Math.PI)]
    public async Task LineStroke_AreaMatchesItsCaps(StrokeCap cap, double expected)
    {
        // 20 long, 2 wide; square caps add 1 × 2 at each end, round caps a disc of radius 1 in all.
        double area = StrokedArea(Segment(12, 20, 32, 20), StrokeParameters.Solid(2f, cap));

        await Assert.That(Math.Abs(area - expected) / expected).IsLessThan(0.01);
    }

    [Test]
    public async Task ClosedSquareStroke_IsARingWithMiterCorners()
    {
        var square = new FlatPath();
        square.MoveTo(10, 10);
        square.LineTo(40, 10);
        square.LineTo(40, 40);
        square.LineTo(10, 40);
        square.Close();

        double area = StrokedArea(square, StrokeParameters.Solid(4f));

        // Outer 34×34 minus inner 26×26.
        double expected = 34.0 * 34.0 - 26.0 * 26.0;
        await Assert.That(Math.Abs(area - expected) / expected).IsLessThan(0.005);
    }

    [Test]
    public async Task Dashes_CoverTheOnFraction()
    {
        var stroke = new StrokeParameters(2f, StrokeCap.Butt, StrokeJoin.Miter, 4f, 3f, 2f, 0f);

        double area = StrokedArea(Segment(5, 20, 55, 20), stroke);

        // 50 long: ten 3-unit dashes, 2 wide.
        await Assert.That(Math.Abs(area - 60.0)).IsLessThan(0.6);
    }

    [Test]
    public async Task SharpTurn_TranslucentStrokeHasNoDoubleCoveredJoint()
    {
        // Inner corners overlap; the non-zero fill must clamp them to full coverage, not 2×.
        var zigzag = new FlatPath();
        zigzag.MoveTo(5, 40);
        zigzag.LineTo(25, 5);
        zigzag.LineTo(45, 40);
        zigzag.EndContour(closed: false);
        var output = new FlatPath();
        new PathStroker().Stroke(zigzag, StrokeParameters.Solid(6f, StrokeCap.Butt, StrokeJoin.Round), 0.02f, output);

        var coverage = Rasterize(output, 64, 64);

        await Assert.That(coverage.Max()).IsEqualTo((byte)255);
        await Assert.That(coverage[20 * 64 + 25]).IsEqualTo((byte)0);
    }

    [Test]
    public async Task PathShapes_RecognizeTheBuildersExactly()
    {
        // Circle as Cascade builds it (kappa rounded to 0.5522847498).
        const double k = 0.5522847498;
        double r = 10, cx = 50, cy = 40;
        byte[] circleVerbs = [0, 3, 3, 3, 3, 4];
        double[] circleCoords =
        [
            cx + r, cy,
            cx + r, cy - k * r, cx + k * r, cy - r, cx, cy - r,
            cx - k * r, cy - r, cx - r, cy - k * r, cx - r, cy,
            cx - r, cy + k * r, cx - k * r, cy + r, cx, cy + r,
            cx + k * r, cy + r, cx + r, cy + k * r, cx + r, cy,
        ];
        bool isCircle = PathShapes.TryCircle(circleVerbs, circleCoords, out float rcx, out float rcy, out float rr);
        await Assert.That(isCircle).IsTrue();
        await Assert.That(Math.Abs(rcx - 50f) + Math.Abs(rcy - 40f) + Math.Abs(rr - 10f)).IsLessThan(1e-4f);

        // Nudge one handle: no longer a circle, drawn as the path it is.
        circleCoords[3] += 0.5;
        await Assert.That(PathShapes.TryCircle(circleVerbs, circleCoords, out _, out _, out _)).IsFalse();

        byte[] rectVerbs = [0, 1, 1, 1, 1, 4];
        double[] rectCoords = [2, 3, 12, 3, 12, 8, 2, 8, 2, 3];
        await Assert.That(PathShapes.TryRect(rectVerbs, rectCoords, out float x, out float y, out float w, out float h)).IsTrue();
        await Assert.That(x == 2 && y == 3 && w == 10 && h == 5).IsTrue();

        byte[] skewVerbs = [0, 1, 1, 1, 4];
        double[] skewCoords = [0, 0, 10, 0, 12, 5, 2, 5];
        await Assert.That(PathShapes.TryRect(skewVerbs, skewCoords, out _, out _, out _, out _)).IsFalse();
    }

    [Test]
    public async Task PathShapes_RecognizeARoundedRect()
    {
        const double k = 0.5522847498;
        double x = 10, y = 20, w = 60, h = 30, r = 6;
        byte[] verbs = [0, 1, 3, 1, 3, 1, 3, 1, 3, 4];
        double[] coords =
        [
            x + r, y,
            x + w - r, y,
            x + w - r + r * k, y, x + w, y + r - r * k, x + w, y + r,
            x + w, y + h - r,
            x + w, y + h - r + r * k, x + w - r + r * k, y + h, x + w - r, y + h,
            x + r, y + h,
            x + r - r * k, y + h, x, y + h - r + r * k, x, y + h - r,
            x, y + r,
            x, y + r - r * k, x + r - r * k, y, x + r, y,
        ];

        bool ok = PathShapes.TryRoundedRect(verbs, coords, out float rx, out float ry, out float rw, out float rh, out float radius);

        await Assert.That(ok).IsTrue();
        await Assert.That(rx == 10 && ry == 20 && rw == 60 && rh == 30).IsTrue();
        await Assert.That(Math.Abs(radius - 6f)).IsLessThan(1e-4f);
    }
}
