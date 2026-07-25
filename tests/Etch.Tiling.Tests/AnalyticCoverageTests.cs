using System;
using Etch.Geometry;
using Etch.Tiling.Strips;
using TUnit;

namespace Etch.Tiling.Tests;

internal sealed class AnalyticCoverageTests
{
    [Test]
    public void HorizontalLine_CoverageAtY0()
    {
        var edges = new (Point, Point)[]
        {
            (new Point(0, 0), new Point(16, 0))
        };

        var coverage = new byte[16];
        AnalyticCoverage.ComputeColumnCoverage(edges, coverage, 0, 0, 16, 0);

        for (int i = 0; i < 16; i++)
        {
            if (coverage[i] != 0)
                throw new InvalidOperationException($"Expected 0 coverage at column {i} for horizontal line at y=0, got {coverage[i]}");
        }
    }

    [Test]
    public void HorizontalLine_CoverageAtY1()
    {
        var edges = new (Point, Point)[]
        {
            (new Point(0, 1), new Point(16, 1))
        };

        var coverage = new byte[16];
        AnalyticCoverage.ComputeColumnCoverage(edges, coverage, 0, 0, 16, 1);

        for (int i = 0; i < 16; i++)
        {
            if (coverage[i] != 0)
                throw new InvalidOperationException($"Expected 0 coverage at column {i} for horizontal line at y=1, got {coverage[i]}");
        }
    }

    [Test]
    public void DiagonalBand_HasCoverage()
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
            AnalyticCoverage.ComputeColumnCoverage(edges, coverage, 0, 0, 16, row);
            for (int col = 0; col < 16; col++)
            {
                totalCoverage += coverage[col];
            }
        }

        if (totalCoverage == 0)
            throw new InvalidOperationException("Total coverage should not be 0 for diagonal band");
    }

    [Test]
    public void VerticalBand_HasCoverage()
    {
        var edges = new (Point, Point)[]
        {
            (new Point(4, 0), new Point(8, 0)),
            (new Point(8, 0), new Point(8, 16)),
            (new Point(8, 16), new Point(4, 16)),
            (new Point(4, 16), new Point(4, 0))
        };

        int totalCoverage = 0;
        for (int row = 0; row < 16; row++)
        {
            var coverage = new byte[16];
            AnalyticCoverage.ComputeColumnCoverage(edges, coverage, 0, 0, 16, row);
            for (int col = 0; col < 16; col++)
            {
                totalCoverage += coverage[col];
            }
        }

        if (totalCoverage == 0)
            throw new InvalidOperationException("Vertical band should have non-zero coverage");
    }

    [Test]
    public void Square_AllPixelsCovered()
    {
        var edges = new (Point, Point)[]
        {
            (new Point(2, 2), new Point(14, 2)),
            (new Point(14, 2), new Point(14, 14)),
            (new Point(14, 14), new Point(2, 14)),
            (new Point(2, 14), new Point(2, 2))
        };

        for (int row = 2; row < 14; row++)
        {
            var coverage = new byte[16];
            AnalyticCoverage.ComputeColumnCoverage(edges, coverage, 0, 0, 16, row);

            for (int col = 2; col < 14; col++)
            {
                if (coverage[col] == 0)
                    throw new InvalidOperationException($"Expected non-zero coverage at ({col}, {row}), got 0");
            }
        }
    }

    [Test]
    public void EmptyEdges_ZeroCoverage()
    {
        var edges = Array.Empty<(Point, Point)>();

        var coverage = new byte[16];
        AnalyticCoverage.ComputeColumnCoverage(edges, coverage, 0, 0, 16, 0);

        for (int i = 0; i < 16; i++)
        {
            if (coverage[i] != 0)
                throw new InvalidOperationException($"Expected 0 coverage for empty edges at column {i}, got {coverage[i]}");
        }
    }

    [Test]
    public void EdgeOutsideTile_NoCoverage()
    {
        var edges = new (Point, Point)[]
        {
            (new Point(100, 100), new Point(200, 200))
        };

        var coverage = new byte[16];
        AnalyticCoverage.ComputeColumnCoverage(edges, coverage, 0, 0, 16, 0);

        for (int i = 0; i < 16; i++)
        {
            if (coverage[i] != 0)
                throw new InvalidOperationException($"Expected 0 coverage for edge outside tile, got {coverage[i]}");
        }
    }

    [Test]
    public void DeterministicEdges_ConsistentResults()
    {
        var edges = new (Point, Point)[]
        {
            (new Point(1.5, 3.0), new Point(7.5, 9.0)),
            (new Point(-1.0, 5.0), new Point(12.0, 5.0)),
            (new Point(3.0, 1.0), new Point(8.0, 15.0)),
        };

        var coverage1 = new byte[16];
        var coverage2 = new byte[16];

        AnalyticCoverage.ComputeColumnCoverage(edges, coverage1, 0, 0, 16, 5);
        AnalyticCoverage.ComputeColumnCoverage(edges, coverage2, 0, 0, 16, 5);

        for (int i = 0; i < 16; i++)
        {
            if (coverage1[i] != coverage2[i])
                throw new InvalidOperationException($"Coverage mismatch at column {i}: {coverage1[i]} vs {coverage2[i]}");
        }
    }
}
