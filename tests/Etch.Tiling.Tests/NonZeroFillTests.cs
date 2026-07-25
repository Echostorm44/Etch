using System;
using Etch.Geometry;
using Etch.Scene;
using Etch.Tiling;
using Etch.Tiling.Classify;
using Etch.Tiling.Strips;
using TUnit;

namespace Etch.Tiling.Tests;

internal sealed class NonZeroFillTests
{
    [Test]
    public void NonZeroFill_FigureEight_BothLobesFilled()
    {
        var grid = new TileGrid<TTile16>(64, 64);
        var path = CreateFigureEightPath();

        var sb = SceneBuilder.Begin(4);
        sb.BeginFrame();
        int pathId = sb.AddPath(path);
        int paintId = sb.AddPaint(Paint.Solid(0xFFFF0000));
        int transformId = sb.AddTransform(Affine.Identity);
        sb.FillPath(pathId, paintId, transformId, FillRule.NonZero);
        sb.EndFrame();
        var scene = sb.End();
        sb.Dispose();

        var accum = new ClassificationAccumulator(4096);
        BBoxClassifier.Classify(scene, grid, ref accum);
        var entries = accum.Finish().ToArray();
        var classified = ClassificationMerge.Merge([entries], grid);

        var buffer = StripEmitter.Emit(scene, classified, grid);

        if (buffer.StripCount == 0)
            throw new InvalidOperationException("Expected strips for figure-eight path");
    }

    [Test]
    public void NonZeroFill_ConvexRectangle_MatchesEvenOdd()
    {
        var grid = new TileGrid<TTile16>(64, 64);
        var path = CreateRectanglePath(10, 10, 50, 50);

        var nonZeroBuffer = CreateStripBuffer(grid, path, FillRule.NonZero);
        var evenOddBuffer = CreateStripBuffer(grid, path, FillRule.EvenOdd);

        int nonZeroStrips = nonZeroBuffer.StripCount;
        int evenOddStrips = evenOddBuffer.StripCount;

        if (nonZeroStrips != evenOddStrips)
            throw new InvalidOperationException($"Strip counts differ: NonZero={nonZeroStrips}, EvenOdd={evenOddStrips}");
    }

    [Test]
    public void NonZeroFill_Rectangle_HasFullCoverage()
    {
        var edges = new (Point, Point)[]
        {
            (new Point(0, 4), new Point(16, 4)),
            (new Point(16, 4), new Point(16, 12)),
            (new Point(16, 12), new Point(0, 12)),
            (new Point(0, 12), new Point(0, 4))
        };

        var coverage = new byte[16];
        AnalyticCoverage.ComputeColumnCoverageNonZero(edges, coverage, 0, 0, 16, 8);

        for (int col = 0; col < 16; col++)
        {
            if (coverage[col] == 0)
                throw new InvalidOperationException($"Expected non-zero coverage at column {col}, got 0");
        }
    }

    [Test]
    public void NonZeroFill_HorizontalBand_HasCoverage()
    {
        var edges = new (Point, Point)[]
        {
            (new Point(4, 0), new Point(8, 0)),
            (new Point(8, 0), new Point(16, 8)),
            (new Point(16, 8), new Point(16, 12)),
            (new Point(16, 12), new Point(8, 4)),
            (new Point(8, 4), new Point(0, 12)),
            (new Point(0, 12), new Point(0, 8)),
            (new Point(0, 8), new Point(4, 0))
        };

        int totalCoverage = 0;
        for (int row = 0; row < 16; row++)
        {
            var coverage = new byte[16];
            AnalyticCoverage.ComputeColumnCoverageNonZero(edges, coverage, 0, 0, 16, row);
            for (int col = 0; col < 16; col++)
            {
                totalCoverage += coverage[col];
            }
        }

        if (totalCoverage == 0)
            throw new InvalidOperationException("Non-zero fill should have non-zero coverage for closed path");
    }

    [Test]
    public void NonZeroFill_DiagonalEdge_ProducesFractionalCoverage()
    {
        // Small triangle: (0,0) -> (1.5,0) -> (1.5,1.5)
        // At rowY = 0.5, crossings are at x = 0.5 and x = 1.5
        // Column 1 ([1, 2]) should have overlap = 0.5 -> coverage = 127
        var edges = new (Point, Point)[]
        {
            (new Point(0, 0), new Point(1.5, 0)),
            (new Point(1.5, 0), new Point(1.5, 1.5)),
            (new Point(1.5, 1.5), new Point(0, 0))
        };

        var coverage = new byte[4];
        AnalyticCoverage.ComputeColumnCoverageNonZero(edges, coverage, 0, 0, 4, 0);

        bool hasFractional = false;
        for (int col = 0; col < 4; col++)
        {
            if (coverage[col] > 0 && coverage[col] < 255)
            {
                hasFractional = true;
                break;
            }
        }

        if (!hasFractional)
        {
            throw new InvalidOperationException(
                $"Expected fractional coverage for diagonal edge, got [{coverage[0]}, {coverage[1]}, {coverage[2]}, {coverage[3]}]");
        }
    }

    [Test]
    public void NonZeroFill_PieSector_ProducesFractionalCoverageInStripBuffer()
    {
        // Build a pie-sector-like path: center (8,8), radius 8, 0 to 45 degrees
        // This spans tiles and should produce fractional coverage at the arc edge
        var grid = new TileGrid<TTile16>(32, 32);
        var path = CreatePieSectorPath(8, 8, 8, 0, Math.PI / 4);

        var buffer = CreateStripBuffer(grid, path, FillRule.NonZero);

        bool hasFractional = false;
        foreach (var strip in buffer.Strips)
        {
            var coverage = buffer.CoverageForStrip(strip);
            foreach (byte c in coverage)
            {
                if (c > 0 && c < 255)
                {
                    hasFractional = true;
                    break;
                }
            }
            if (hasFractional) break;
        }

        if (!hasFractional)
        {
            throw new InvalidOperationException(
                "Expected fractional coverage in strip buffer for pie sector path, but all coverage values were 0 or 255");
        }
    }

    [Test]
    public void NonZeroFill_PieSector_TTile8_ProducesFractionalCoverageInStripBuffer()
    {
        // Same as above but with TTile8 (8x8 tiles) to match GPU pipeline
        var grid = new TileGrid<TTile8>(32, 32);
        var path = CreatePieSectorPath(8, 8, 8, 0, Math.PI / 4);

        var buffer = CreateStripBuffer(grid, path, FillRule.NonZero);

        bool hasFractional = false;
        foreach (var strip in buffer.Strips)
        {
            var coverage = buffer.CoverageForStrip(strip);
            foreach (byte c in coverage)
            {
                if (c > 0 && c < 255)
                {
                    hasFractional = true;
                    break;
                }
            }
            if (hasFractional) break;
        }

        if (!hasFractional)
        {
            throw new InvalidOperationException(
                "Expected fractional coverage in strip buffer for pie sector path with TTile8, but all coverage values were 0 or 255. " +
                $"Strips={buffer.StripCount}");
        }
    }

    [Test]
    public void NonZeroFill_PieSector_TTile8_CoverageShape_IsSectorNotCircle()
    {
        // Verify that the strip coverage actually forms a sector shape, not a full circle.
        var grid = new TileGrid<TTile8>(32, 32);
        var path = CreatePieSectorPath(8, 8, 8, 0, Math.PI / 4);

        var buffer = CreateStripBuffer(grid, path, FillRule.NonZero);

        // Check coverage in tile (1,1) = [8,8]-[16,16]
        int tileIndex = grid.TileIndex(1, 1);
        bool hasCoverageInWedge = false;
        bool hasCoverageOutsideWedge = false;

        foreach (var strip in buffer.Strips)
        {
            if (strip.TileIndex != tileIndex)
                continue;

            for (int row = 0; row < 8; row++)
            {
                if ((strip.RowMask & (1 << row)) == 0)
                    continue;

                int worldY = 8 + row;
                for (int col = (int)strip.X0; col <= (int)strip.X1; col++)
                {
                    int worldX = 8 + col;
                    double dx = worldX - 8;
                    double dy = worldY - 8;
                    double angle = Math.Atan2(dy, dx);
                    if (angle < 0) angle += 2 * Math.PI;

                    bool insideSector = angle >= 0 && angle <= Math.PI / 4;
                    if (insideSector && buffer.CoverageForStrip(strip)[col - (int)strip.X0] > 0)
                        hasCoverageInWedge = true;
                    if (!insideSector && buffer.CoverageForStrip(strip)[col - (int)strip.X0] > 0)
                        hasCoverageOutsideWedge = true;
                }
            }
        }

        if (!hasCoverageInWedge)
            throw new InvalidOperationException("Expected coverage inside the sector wedge, but found none.");

        if (hasCoverageOutsideWedge)
            throw new InvalidOperationException(
                "Found coverage OUTSIDE the sector wedge. This suggests the strips form a full circle instead of a sector wedge.");
    }

    [Test]
    public void NonZeroFill_DiagonalLine_TTile8_ProducesFractionalCoverage()
    {
        // Sanity check: a simple diagonal line should produce fractional coverage
        var grid = new TileGrid<TTile8>(32, 32);
        var path = CreateDiagonalPath(8, 8, 24, 24);

        var buffer = CreateStripBuffer(grid, path, FillRule.NonZero);

        int fractionalPixels = 0;
        int totalPixels = 0;
        foreach (var strip in buffer.Strips)
        {
            var coverage = buffer.CoverageForStrip(strip);
            for (int i = 0; i < coverage.Length; i++)
            {
                totalPixels++;
                if (coverage[i] > 0 && coverage[i] < 255)
                    fractionalPixels++;
            }
        }

        if (fractionalPixels == 0)
        {
            throw new InvalidOperationException(
                $"Even a simple diagonal line produced 0 fractional coverage ({totalPixels} total pixels). " +
                "This indicates a fundamental issue with ComputeColumnCoverageNonZero or ClipEdgeToTile.");
        }
    }

    [Test]
    public void NonZeroFill_SyntheticArcSegment_TTile8_ProducesFractionalCoverage()
    {
        // Directly test ComputeColumnCoverageNonZero with a synthetic arc segment
        // that mimics what ClipEdgeToTile would produce for a boundary tile.
        var edges = new (Point, Point)[]
        {
            // Arc segment crossing tile [40,40]-[48,48] at row 40.5
            // From (40.1, 40.8) to (40.8, 40.1)
            (new Point(40.1, 40.8), new Point(40.8, 40.1)),
            // Return segment to close the path within the tile
            (new Point(40.8, 40.1), new Point(45.0, 47.9)),
            (new Point(45.0, 47.9), new Point(40.1, 40.8)),
        };

        var coverage = new byte[8];
        AnalyticCoverage.ComputeColumnCoverageNonZero(edges, coverage, 5, 5, 8, 0);

        bool hasFractional = false;
        for (int col = 0; col < 8; col++)
        {
            if (coverage[col] > 0 && coverage[col] < 255)
            {
                hasFractional = true;
                break;
            }
        }

        if (!hasFractional)
        {
            throw new InvalidOperationException(
                $"Synthetic arc segment produced no fractional coverage: [{string.Join(",", coverage)}]");
        }
    }

    [Test]
    public void NonZeroFill_ClippedArcSegment_TTile8_ProducesFractionalCoverage()
    {
        // Test with edges that mimic ClipEdgeToTile output for a corner-touching segment.
        // ClipEdgeToTile inflates points near boundaries by eps (1e-6).
        var edges = new (Point, Point)[]
        {
            // Segment entering tile (6,4) = [48,32]-[56,40] from the right
            // After clipping, start is at (56-eps, 32+eps) and end is inside
            (new Point(56 - 1e-6, 32 + 1e-6), new Point(55.0, 36.0)),
            // Segment exiting tile to the top
            (new Point(55.0, 36.0), new Point(52.0, 40 - 1e-6)),
            // Close back to start
            (new Point(52.0, 40 - 1e-6), new Point(56 - 1e-6, 32 + 1e-6)),
        };

        var coverage = new byte[8];
        AnalyticCoverage.ComputeColumnCoverageNonZero(edges, coverage, 6, 4, 8, 0);

        bool hasFractional = false;
        for (int col = 0; col < 8; col++)
        {
            if (coverage[col] > 0 && coverage[col] < 255)
            {
                hasFractional = true;
                break;
            }
        }

        if (!hasFractional)
        {
            throw new InvalidOperationException(
                $"Clipped arc segment produced no fractional coverage: [{string.Join(",", coverage)}]");
        }
    }

    private static BezPath CreateDiagonalPath(double x0, double y0, double x1, double y1)
    {
        var builder = BezPathBuilder.Begin(4);
        builder.MoveTo(new Point(x0, y0));
        builder.LineTo(new Point(x1, y1));
        builder.LineTo(new Point(x1, y0 + 8));
        builder.LineTo(new Point(x0, y0 + 8));
        builder.Close();
        return builder.Build();
    }

    private static BezPath CreatePieSectorPath(double cx, double cy, double radius, double startRad, double sweepRad)
    {
        var builder = BezPathBuilder.Begin(16);
        builder.MoveTo(new Point(cx, cy));
        builder.LineTo(new Point(cx + radius * Math.Cos(startRad), cy + radius * Math.Sin(startRad)));

        // Split sweep into segments of at most 90° (π/2), same as NodePainter.BuildSectorPath
        double remaining = sweepRad;
        double current = startRad;

        while (Math.Abs(remaining) > 0.001)
        {
            double maxSegment = Math.PI / 2.0;
            double segment = remaining > 0
                ? Math.Min(remaining, maxSegment)
                : Math.Max(remaining, -maxSegment);

            double halfSegment = segment / 2.0;
            double k = 4.0 / 3.0 * Math.Tan(halfSegment / 2.0);

            double c0 = Math.Cos(current);
            double s0 = Math.Sin(current);
            double c1 = Math.Cos(current + segment);
            double s1 = Math.Sin(current + segment);

            double x0 = cx + radius * c0;
            double y0 = cy + radius * s0;
            double x1 = cx + radius * c1;
            double y1 = cy + radius * s1;

            double cp1x = x0 - k * radius * s0;
            double cp1y = y0 + k * radius * c0;
            double cp2x = x1 + k * radius * s1;
            double cp2y = y1 - k * radius * c1;

            builder.CubicTo(new Point(cp1x, cp1y), new Point(cp2x, cp2y), new Point(x1, y1));

            current += segment;
            remaining -= segment;
        }

        builder.LineTo(new Point(cx, cy));
        builder.Close();
        return builder.Build();
    }

    private static StripBuffer CreateStripBuffer<TTile>(TileGrid<TTile> grid, BezPath path, FillRule rule)
        where TTile : unmanaged, ITileSize
    {
        var (buffer, _) = CreateStripBufferWithDebug(grid, path, rule);
        return buffer;
    }

    private static (StripBuffer, ClassifiedScene) CreateStripBufferWithDebug<TTile>(TileGrid<TTile> grid, BezPath path, FillRule rule)
        where TTile : unmanaged, ITileSize
    {
        var sb = SceneBuilder.Begin(4);
        sb.BeginFrame();
        int pathId = sb.AddPath(path);
        int paintId = sb.AddPaint(Paint.Solid(0xFFFF0000));
        int transformId = sb.AddTransform(Affine.Identity);
        sb.FillPath(pathId, paintId, transformId, rule);
        sb.EndFrame();
        var scene = sb.End();
        sb.Dispose();

        var accum = new ClassificationAccumulator(4096);
        BBoxClassifier.Classify(scene, grid, ref accum);
        var entries = accum.Finish().ToArray();
        var classified = ClassificationMerge.Merge([entries], grid);

        var buffer = StripEmitter.Emit(scene, classified, grid);
        return (buffer, classified);
    }

    private static BezPath CreateFigureEightPath()
    {
        var builder = BezPathBuilder.Begin(16);
        builder.MoveTo(new Point(32, 4));
        builder.CubicTo(new Point(52, 4), new Point(60, 32), new Point(32, 32));
        builder.CubicTo(new Point(4, 32), new Point(12, 4), new Point(32, 4));
        builder.MoveTo(new Point(32, 32));
        builder.CubicTo(new Point(12, 32), new Point(4, 60), new Point(32, 60));
        builder.CubicTo(new Point(60, 60), new Point(52, 32), new Point(32, 32));
        builder.Close();
        return builder.Build();
    }

    [Test]
    public void NonZeroFill_CircleEdge_HasIntermediateCoverageValues()
    {
        var grid = new TileGrid<TTile16>(64, 64);
        double cx = 32, cy = 32, r = 20;
        double k = 0.5522847498;
        var pb = BezPathBuilder.Begin(16);
        pb.MoveTo(new Point(cx + r, cy));
        pb.CubicTo(new Point(cx + r, cy + k * r), new Point(cx + k * r, cy + r), new Point(cx, cy + r));
        pb.CubicTo(new Point(cx - k * r, cy + r), new Point(cx - r, cy + k * r), new Point(cx - r, cy));
        pb.CubicTo(new Point(cx - r, cy - k * r), new Point(cx - k * r, cy - r), new Point(cx, cy - r));
        pb.CubicTo(new Point(cx + k * r, cy - r), new Point(cx + r, cy - k * r), new Point(cx + r, cy));
        pb.Close();
        var path = pb.Build();

        var (buffer, classified) = CreateStripBufferWithDebug(grid, path, FillRule.NonZero);

        int intermediateCount = 0;
        int totalCoverageValues = 0;
        foreach (var strip in buffer.Strips)
        {
            var cov = buffer.CoverageForStrip(strip);
            foreach (byte c in cov)
            {
                totalCoverageValues++;
                if (c > 0 && c < 255)
                {
                    intermediateCount++;
                }
            }
        }

        if (intermediateCount == 0)
        {
            // Debug: check if strips exist and what the path bounding box is
            var aabb = path.Aabb();
            throw new InvalidOperationException(
                "Strips=" + buffer.StripCount + ", TotalCoverage=" + totalCoverageValues +
                ", Intermediate=" + intermediateCount + ", Entries=" + classified.AllEntries.Length +
                ". Aabb=[" + aabb.MinX + "," + aabb.MinY + "]-[" + aabb.MaxX + "," + aabb.MaxY + "]");
        }
    }

    [Test]
    public void NonZeroFill_Circle_CpuRender_BoundaryPixelsAreSmooth()
    {
        // Render a circle via CPU and verify boundary pixels have intermediate values
        const int width = 64;
        const int height = 64;
        var grid = new TileGrid<TTile8>(width, height);

        var builder = SceneBuilder.Begin();
        builder.BeginFrame();
        int identity = builder.AddTransform(Affine.Identity);
        int paintId = builder.AddPaint(Paint.Solid(0xFFFF0000u));

        // Circle centered at (32, 32) with radius 20
        float cx = 32, cy = 32, r = 20;
        double k = 0.5522847498;
        using var pb = BezPathBuilder.Begin();
        pb.MoveTo(new Point(cx + r, cy));
        pb.CubicTo(new Point(cx + r, cy + k * r), new Point(cx + k * r, cy + r), new Point(cx, cy + r));
        pb.CubicTo(new Point(cx - k * r, cy + r), new Point(cx - r, cy + k * r), new Point(cx - r, cy));
        pb.CubicTo(new Point(cx - r, cy - k * r), new Point(cx - k * r, cy - r), new Point(cx, cy - r));
        pb.CubicTo(new Point(cx + k * r, cy - r), new Point(cx + r, cy - k * r), new Point(cx + r, cy));
        pb.Close();
        int pathId = builder.AddPath(pb.Build());
        builder.FillPath(pathId, paintId, identity, FillRule.NonZero);
        builder.EndFrame();
        var scene = builder.End();

        var accum = new ClassificationAccumulator(4096);
        BBoxClassifier.Classify(scene, grid, ref accum);
        var entries = accum.Finish().ToArray();
        var classified = ClassificationMerge.Merge([entries], grid);
        var stripBuffer = StripEmitter.Emit(scene, classified, grid);

        // Count boundary pixels (pixels adjacent to both filled and empty)
        int boundaryPixels = 0;
        int smoothBoundaryPixels = 0;
        var tileCoverage = new byte[grid.TileCountX * grid.TileCountY * TTile8.Width * TTile8.Height];

        foreach (var strip in stripBuffer.Strips)
        {
            var (tx, ty) = grid.TileXY((int)strip.TileIndex);
            var cov = stripBuffer.CoverageForStrip(strip);
            int rowIdx = 0;
            for (int row = 0; row < TTile8.Height; row++)
            {
                if ((strip.RowMask & (1 << row)) == 0)
                    continue;
                for (int col = (int)strip.X0; col <= (int)strip.X1; col++)
                {
                    tileCoverage[(tx + ty * grid.TileCountX) * TTile8.Width * TTile8.Height + row * TTile8.Width + col] = cov[col - (int)strip.X0 + rowIdx * ((int)strip.X1 - (int)strip.X0 + 1)];
                }
                rowIdx++;
            }
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int tx = x / TTile8.Width;
                int ty = y / TTile8.Height;
                int col = x % TTile8.Width;
                int row = y % TTile8.Height;
                byte c = tileCoverage[(tx + ty * grid.TileCountX) * TTile8.Width * TTile8.Height + row * TTile8.Width + col];

                // Check if this is a boundary pixel (has coverage, but neighbor has none)
                bool isBoundary = false;
                if (c > 0)
                {
                    for (int dy = -1; dy <= 1 && !isBoundary; dy++)
                    {
                        for (int dx = -1; dx <= 1 && !isBoundary; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                            int ntx = nx / TTile8.Width;
                            int nty = ny / TTile8.Height;
                            int ncol = nx % TTile8.Width;
                            int nrow = ny % TTile8.Height;
                            if (tileCoverage[(ntx + nty * grid.TileCountX) * TTile8.Width * TTile8.Height + nrow * TTile8.Width + ncol] == 0)
                                isBoundary = true;
                        }
                    }
                }

                if (isBoundary)
                {
                    boundaryPixels++;
                    if (c > 0 && c < 255)
                        smoothBoundaryPixels++;
                }
            }
        }

        if (boundaryPixels == 0)
            throw new InvalidOperationException("No boundary pixels found");

        double smoothRatio = (double)smoothBoundaryPixels / boundaryPixels;

        if (smoothRatio < 0.3)
        {
            var debugOutput = new System.Text.StringBuilder();
            debugOutput.AppendLine("Coverage values for circle (32,32) r=20:");
            for (int y = 10; y < 35; y++)
            {
                for (int x = 30; x < 60; x++)
                {
                    int tx = x / TTile8.Width;
                    int ty = y / TTile8.Height;
                    int col = x % TTile8.Width;
                    int row = y % TTile8.Height;
                    byte c = tileCoverage[(tx + ty * grid.TileCountX) * TTile8.Width * TTile8.Height + row * TTile8.Width + col];
                    if (c == 0) debugOutput.Append("  .");
                    else if (c == 255) debugOutput.Append(" ##");
                    else debugOutput.Append(" " + c.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(2));
                }
                debugOutput.AppendLine();
            }
            debugOutput.AppendLine("Boundary pixels: " + smoothBoundaryPixels + "/" + boundaryPixels + " fractional (" + smoothRatio.ToString("P0", System.Globalization.CultureInfo.InvariantCulture) + ")");

            throw new InvalidOperationException(
                "Boundary is too blocky: only " + smoothBoundaryPixels + "/" + boundaryPixels + " (" + smoothRatio.ToString("P0", System.Globalization.CultureInfo.InvariantCulture) + ") boundary pixels have fractional coverage. " +
                "Strips=" + stripBuffer.StripCount + ". Most boundary pixels are 0 or 255.\n\n" + debugOutput);
        }
    }

    private static BezPath CreateRectanglePath(double minX, double minY, double maxX, double maxY)
    {
        var builder = BezPathBuilder.Begin(4);
        builder.MoveTo(new Point(minX, minY));
        builder.LineTo(new Point(maxX, minY));
        builder.LineTo(new Point(maxX, maxY));
        builder.LineTo(new Point(minX, maxY));
        builder.Close();
        return builder.Build();
    }
}
