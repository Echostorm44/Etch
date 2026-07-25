using System;
using Etch.Geometry;

namespace Etch.Tiling.Strips;

internal sealed class EvenOddFillStrategy : IFillStrategy
{
    public static readonly EvenOddFillStrategy Instance = new();

    private EvenOddFillStrategy() { }

    public void ComputeRowCoverage(
        ReadOnlySpan<(Point Start, Point End)> edges,
        Span<byte> coveragePerColumn,
        int tileX,
        int tileY,
        int tileWidth,
        int rowIndex)
    {
        AnalyticCoverage.ComputeColumnCoverage(edges, coveragePerColumn, tileX, tileY, tileWidth, rowIndex);
    }
}
