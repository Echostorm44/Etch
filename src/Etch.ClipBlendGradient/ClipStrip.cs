using System.Runtime.InteropServices;

namespace Etch.ClipBlendGradient;

[StructLayout(LayoutKind.Sequential, Size = 8)]
public readonly struct ClipStrip
{
    public readonly ushort RowMask;
    public readonly byte X0;
    public readonly byte X1;
    public readonly uint CoverageOffset;

    public ClipStrip(ushort rowMask, byte x0, byte x1, uint coverageOffset)
    {
        RowMask = rowMask;
        X0 = x0;
        X1 = x1;
        CoverageOffset = coverageOffset;
    }
}
