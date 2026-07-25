using System.Runtime.InteropServices;

namespace Etch.Tiling.Classify;

[StructLayout(LayoutKind.Sequential, Size = 12)]
public readonly struct CoveragePayload
{
    public readonly int StripRowMask;
    public readonly uint Packed0;
    public readonly uint Packed1;

    public CoveragePayload(int stripRowMask, uint packed0, uint packed1)
    {
        StripRowMask = stripRowMask;
        Packed0 = packed0;
        Packed1 = packed1;
    }
}