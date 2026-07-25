using System;

namespace Etch.Tiling;

public readonly struct TileQuadList
{
    private readonly TileQuad[] _quads;
    private readonly int _count;

    public TileQuadList(TileQuad[] quads, int count)
    {
        _quads = quads;
        _count = count;
    }

    public int Count => _count;

    public ReadOnlySpan<TileQuad> Quads => _quads.AsSpan(0, _count);
}
