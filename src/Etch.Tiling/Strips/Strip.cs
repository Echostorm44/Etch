using System.Runtime.InteropServices;

namespace Etch.Tiling.Strips;

[StructLayout(LayoutKind.Sequential, Size = 24)]
public readonly struct Strip
{
    public readonly uint TileIndex;
    public readonly uint RowMask;
    public readonly uint X0;
    public readonly uint X1;
    public readonly uint CoverageOffset;
    public readonly uint PaintId;

    public Strip(uint tileIndex, uint rowMask, uint x0, uint x1, uint coverageOffset, uint paintId)
    {
        TileIndex = tileIndex;
        RowMask = rowMask;
        X0 = x0;
        X1 = x1;
        CoverageOffset = coverageOffset;
        PaintId = paintId;
    }
}
