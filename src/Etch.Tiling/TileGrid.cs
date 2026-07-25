using System;
using Etch.Geometry;

namespace Etch.Tiling;

public readonly struct TileGrid<TTile>
    where TTile : struct, ITileSize
{
    public readonly int SurfaceWidth;
    public readonly int SurfaceHeight;
    public readonly int TileCountX;
    public readonly int TileCountY;

    public int TotalTiles => TileCountX * TileCountY;

    public TileGrid(int surfaceWidth, int surfaceHeight)
    {
        if (surfaceWidth <= 0 || surfaceHeight <= 0)
            Etch.Panic.Invariant(Etch.PanicCodes.InvalidSurfaceSize, "Surface dimensions must be positive");

        SurfaceWidth = surfaceWidth;
        SurfaceHeight = surfaceHeight;
        TileCountX = (surfaceWidth + TTile.Width - 1) >> TTile.Log2Width;
        TileCountY = (surfaceHeight + TTile.Height - 1) >> TTile.Log2Height;
    }

    public int TileIndex(int tileX, int tileY) => tileY * TileCountX + tileX;

    public (int x, int y) TileXY(int tileIndex)
    {
        int y = tileIndex / TileCountX;
        int x = tileIndex - y * TileCountX;
        return (x, y);
    }

    public Rect TileBounds(int tileX, int tileY)
    {
        int minX = tileX << TTile.Log2Width;
        int minY = tileY << TTile.Log2Height;
        int maxX = Math.Min(minX + TTile.Width, SurfaceWidth);
        int maxY = Math.Min(minY + TTile.Height, SurfaceHeight);
        return Rect.FromLTRB(minX, minY, maxX, maxY);
    }

    public void TilesOverlappingPixelRect(Rect pixelRect, out int minTileX, out int minTileY, out int maxTileX, out int maxTileY)
    {
        if (pixelRect.IsEmpty)
        {
            minTileX = 0;
            minTileY = 0;
            maxTileX = -1;
            maxTileY = -1;
            return;
        }

        minTileX = (int)Math.Floor(pixelRect.MinX) >> TTile.Log2Width;
        minTileY = (int)Math.Floor(pixelRect.MinY) >> TTile.Log2Height;
        maxTileX = (int)Math.Ceiling(pixelRect.MaxX - 1) >> TTile.Log2Width;
        maxTileY = (int)Math.Ceiling(pixelRect.MaxY - 1) >> TTile.Log2Height;

        if (minTileX > maxTileX || minTileY > maxTileY ||
            minTileX >= TileCountX || minTileY >= TileCountY ||
            maxTileX < 0 || maxTileY < 0)
        {
            minTileX = 0;
            minTileY = 0;
            maxTileX = -1;
            maxTileY = -1;
            return;
        }

        minTileX = Math.Max(0, minTileX);
        minTileY = Math.Max(0, minTileY);
        maxTileX = Math.Min(TileCountX - 1, maxTileX);
        maxTileY = Math.Min(TileCountY - 1, maxTileY);
    }

    public bool IsValidTile(int tileX, int tileY)
    {
        return (uint)tileX < (uint)TileCountX && (uint)tileY < (uint)TileCountY;
    }
}