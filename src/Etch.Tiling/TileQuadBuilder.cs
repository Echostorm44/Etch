using System;
using Etch.Tiling.Strips;

namespace Etch.Tiling;

public static class TileQuadBuilder
{
    public const ushort FlagSolidColor = 1;
    public const ushort FlagTileInsideFrame = 2;

    public static TileQuadList Build<TTile>(StripBuffer strips, TileGrid<TTile> grid, Func<int, uint> resolvePaint)
        where TTile : struct, ITileSize
    {
        var quads = new TileQuad[grid.TotalTiles];
        int count = 0;
        uint stripCursor = 0;

#pragma warning disable CA1062
        if (strips == null)
            Etch.Panic.Invariant(Etch.PanicCodes.ArgumentNull, "strips must not be null");
        if (resolvePaint == null)
            Etch.Panic.Invariant(Etch.PanicCodes.ArgumentNull, "resolvePaint must not be null");

        for (int t = 0; t < grid.TotalTiles; t++)
        {
            var range = strips.RangeForTile(t);
            if (range.Length == 0)
                continue;

            var (tx, ty) = grid.TileXY(t);
            var stripsSpan = strips.Strips.Slice(range.StartIndex, range.Length);

            bool isSolid = IsSolidColor(strips, stripsSpan);

            ushort flags = 0;
            if (isSolid)
                flags |= FlagSolidColor;
            if (IsTileInsideFrame(tx, ty, grid))
                flags |= FlagTileInsideFrame;

            quads[count++] = new TileQuad(
                (ushort)(tx * TTile.Width),
                (ushort)(ty * TTile.Height),
                (ushort)TTile.Width,
                (ushort)TTile.Height,
                (uint)range.StartIndex,
                (ushort)range.Length,
                flags,
                resolvePaint(t));

            stripCursor += (uint)range.Length;
        }
#pragma warning restore CA1062

        return new TileQuadList(quads, count);
    }

    private static bool IsSolidColor(StripBuffer strips, ReadOnlySpan<Strip> stripsForTile)
    {
        foreach (var strip in stripsForTile)
        {
            var coverage = strips.CoverageForStrip(in strip);
            for (int i = 0; i < coverage.Length; i++)
            {
                if (coverage[i] != 0xFF)
                    return false;
            }
        }
        return true;
    }

    private static bool IsTileInsideFrame<TTile>(int tileX, int tileY, TileGrid<TTile> grid)
        where TTile : struct, ITileSize
    {
        int tileMaxX = (tileX + 1) * TTile.Width;
        int tileMaxY = (tileY + 1) * TTile.Height;
        return tileMaxX <= grid.SurfaceWidth && tileMaxY <= grid.SurfaceHeight;
    }
}
