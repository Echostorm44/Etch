using System.Runtime.InteropServices;

namespace Etch.Tiling.Classify;

[StructLayout(LayoutKind.Sequential, Size = 24)]
public readonly struct ClassificationEntry
{
    public readonly int TileIndex;
    public readonly int CommandOrder;
    public readonly ClassificationKind Kind;
    public readonly byte Padding0;
    public readonly byte Padding1;
    public readonly byte Padding2;
    public readonly CoveragePayload Payload;

    public ClassificationEntry(int tileIndex, int commandOrder, ClassificationKind kind, in CoveragePayload payload)
    {
        TileIndex = tileIndex;
        CommandOrder = commandOrder;
        Kind = kind;
        Padding0 = 0;
        Padding1 = 0;
        Padding2 = 0;
        Payload = payload;
    }
}