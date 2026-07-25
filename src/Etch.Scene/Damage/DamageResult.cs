using System;
using Etch.Geometry;

namespace Etch.Scene.Damage;

public enum DamageMode
{
    TileBitmap,
    RectGranular,
    Scroll
}

public readonly struct DamageResult
{
    public readonly bool[] DirtyTiles;
    public readonly int DirtyCount;
    public readonly int TotalTiles;
    public readonly double DirtyPercent;
    public readonly DamageMode Mode;
    public readonly Rect[] DirtyRects;

    public DamageResult(bool[] dirtyTiles, int dirtyCount)
    {
#pragma warning disable CA1062
        DirtyTiles = dirtyTiles;
        DirtyCount = dirtyCount;
        TotalTiles = dirtyTiles.Length;
        DirtyPercent = TotalTiles > 0 ? (double)dirtyCount / TotalTiles : 0.0;
        Mode = DamageMode.TileBitmap;
        DirtyRects = null!;
#pragma warning restore CA1062
    }

    public DamageResult(Rect[] dirtyRects)
    {
        DirtyTiles = null!;
        DirtyCount = 0;
        TotalTiles = 0;
        DirtyPercent = 0.0;
        Mode = DamageMode.RectGranular;
        DirtyRects = dirtyRects;
    }

    public DamageResult(ScrollHint hint, Rect revealedStrip)
    {
        DirtyTiles = null!;
        DirtyCount = 0;
        TotalTiles = 0;
        DirtyPercent = 0.0;
        Mode = DamageMode.Scroll;
        DirtyRects = new Rect[] { revealedStrip };
    }
}