using System;
using Etch.Geometry;

namespace Etch.Tiling.Strips;

internal sealed class NonZeroFillStrategy : IFillStrategy
{
    public static readonly NonZeroFillStrategy Instance = new();

    private NonZeroFillStrategy() { }

    public void ComputeRowCoverage(
        ReadOnlySpan<(Point Start, Point End)> edges,
        Span<byte> coveragePerColumn,
        int tileX,
        int tileY,
        int tileWidth,
        int rowIndex)
    {
        AnalyticCoverage.ComputeColumnCoverageNonZero(edges, coveragePerColumn, tileX, tileY, tileWidth, rowIndex);
    }
}
