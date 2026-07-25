using System.Runtime.InteropServices;

namespace Etch.Tiling;

[StructLayout(LayoutKind.Sequential, Size = 32)]
public readonly struct TileQuad
{
    public readonly ushort TileX;
    public readonly ushort TileY;
    public readonly ushort TileW;
    public readonly ushort TileH;
    public readonly uint StripStart;
    public readonly ushort StripCount;
    public readonly ushort Flags;
    public readonly uint PaintId;
    public readonly uint Reserved;

    public TileQuad(ushort tileX, ushort tileY, ushort tileW, ushort tileH, uint stripStart, ushort stripCount, ushort flags, uint paintId)
    {
        TileX = tileX;
        TileY = tileY;
        TileW = tileW;
        TileH = tileH;
        StripStart = stripStart;
        StripCount = stripCount;
        Flags = flags;
        PaintId = paintId;
        Reserved = 0;
    }
}
