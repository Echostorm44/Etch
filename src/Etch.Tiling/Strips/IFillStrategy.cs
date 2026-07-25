using System;
using Etch.Geometry;

namespace Etch.Tiling.Strips;

internal interface IFillStrategy
{
    void ComputeRowCoverage(
        ReadOnlySpan<(Point Start, Point End)> edges,
        Span<byte> coveragePerColumn,
        int tileX,
        int tileY,
        int tileWidth,
        int rowIndex);
}
