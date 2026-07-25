using System;
using Etch.Geometry;
using Etch.Tiling;
using TUnit;

namespace Etch.Tiling.Tests;

public sealed class TileGridTests
{
    [Test]
    public void TileGrid_1920x1080_TTile16_TileCountX_Is120()
    {
        var grid = new TileGrid<TTile16>(1920, 1080);
        if (grid.TileCountX != 120)
            throw new InvalidOperationException($"Expected 120, got {grid.TileCountX}");
    }

    [Test]
    public void TileGrid_1920x1080_TTile16_TileCountY_Is68()
    {
        var grid = new TileGrid<TTile16>(1920, 1080);
        if (grid.TileCountY != 68)
            throw new InvalidOperationException($"Expected 68, got {grid.TileCountY}");
    }

    [Test]
    public void TileGrid_1920x1080_TTile16_TotalTiles_Is8160()
    {
        var grid = new TileGrid<TTile16>(1920, 1080);
        if (grid.TotalTiles != 8160)
            throw new InvalidOperationException($"Expected 8160, got {grid.TotalTiles}");
    }

    [Test]
    public void TileGrid_1x1_TTile8_Produces1Tile()
    {
        var grid = new TileGrid<TTile8>(1, 1);
        if (grid.TotalTiles != 1)
            throw new InvalidOperationException($"Expected 1, got {grid.TotalTiles}");
    }

    [Test]
    public void TileGrid_1x1_TTile32_Produces1Tile()
    {
        var grid = new TileGrid<TTile32>(1, 1);
        if (grid.TotalTiles != 1)
            throw new InvalidOperationException($"Expected 1, got {grid.TotalTiles}");
    }

    [Test]
    public void TileIndex_TileXY_RoundTrip()
    {
        var grid = new TileGrid<TTile16>(1920, 1080);
        for (int y = 0; y < grid.TileCountY; y++)
        {
            for (int x = 0; x < grid.TileCountX; x++)
            {
                int index = grid.TileIndex(x, y);
                var (rx, ry) = grid.TileXY(index);
                if (rx != x || ry != y)
                    throw new InvalidOperationException($"Round-trip failed for ({x}, {y}): got ({rx}, {ry})");
            }
        }
    }

    [Test]
    public void TileIndex_TileXY_RoundTrip_TTile8()
    {
        var grid = new TileGrid<TTile8>(1920, 1080);
        for (int y = 0; y < grid.TileCountY; y++)
        {
            for (int x = 0; x < grid.TileCountX; x++)
            {
                int index = grid.TileIndex(x, y);
                var (rx, ry) = grid.TileXY(index);
                if (rx != x || ry != y)
                    throw new InvalidOperationException($"Round-trip failed for ({x}, {y}): got ({rx}, {ry})");
            }
        }
    }

    [Test]
    public void TileIndex_TileXY_RoundTrip_TTile32()
    {
        var grid = new TileGrid<TTile32>(1920, 1080);
        for (int y = 0; y < grid.TileCountY; y++)
        {
            for (int x = 0; x < grid.TileCountX; x++)
            {
                int index = grid.TileIndex(x, y);
                var (rx, ry) = grid.TileXY(index);
                if (rx != x || ry != y)
                    throw new InvalidOperationException($"Round-trip failed for ({x}, {y}): got ({rx}, {ry})");
            }
        }
    }

    [Test]
    public void TileBounds_ReturnsCorrectRect()
    {
        var grid = new TileGrid<TTile16>(1920, 1080);
        var bounds = grid.TileBounds(0, 0);
        if (bounds.MinX != 0 || bounds.MinY != 0 || bounds.MaxX != 16 || bounds.MaxY != 16)
            throw new InvalidOperationException($"Expected Rect(0, 0, 16, 16), got {bounds}");
    }

    [Test]
    public void TileBounds_TrailingTile_IsClamped()
    {
        var grid = new TileGrid<TTile16>(100, 100);
        var bounds = grid.TileBounds(grid.TileCountX - 1, grid.TileCountY - 1);
        if (bounds.MaxX != 100 || bounds.MaxY != 100)
            throw new InvalidOperationException($"Expected Rect(96, 96, 100, 100), got {bounds}");
    }

    [Test]
    public void TilesOverlappingPixelRect_StraddlingBoundary()
    {
        var grid = new TileGrid<TTile16>(1920, 1080);
        grid.TilesOverlappingPixelRect(Rect.FromLTRB(14, 14, 18, 18), out int minX, out int minY, out int maxX, out int maxY);
        if (minX != 0 || minY != 0 || maxX != 1 || maxY != 1)
            throw new InvalidOperationException($"Expected (0, 0, 1, 1), got ({minX}, {minY}, {maxX}, {maxY})");
    }

    [Test]
    public void TilesOverlappingPixelRect_OutsideSurface_ReturnsEmpty()
    {
        var grid = new TileGrid<TTile16>(1920, 1080);
        grid.TilesOverlappingPixelRect(Rect.FromLTRB(2000, 2000, 3000, 3000), out int minX, out int minY, out int maxX, out int maxY);
        if (minX != 0 || minY != 0 || maxX != -1 || maxY != -1)
            throw new InvalidOperationException($"Expected (0, 0, -1, -1), got ({minX}, {minY}, {maxX}, {maxY})");
    }

    [Test]
    public void TilesOverlappingPixelRect_EntirelyInsideTile()
    {
        var grid = new TileGrid<TTile16>(1920, 1080);
        grid.TilesOverlappingPixelRect(Rect.FromLTRB(32, 32, 48, 48), out int minX, out int minY, out int maxX, out int maxY);
        if (minX != 2 || minY != 2 || maxX != 2 || maxY != 2)
            throw new InvalidOperationException($"Expected (2, 2, 2, 2), got ({minX}, {minY}, {maxX}, {maxY})");
    }

    [Test]
    public void IsValidTile_Valid_ReturnsTrue()
    {
        var grid = new TileGrid<TTile16>(1920, 1080);
        if (!grid.IsValidTile(0, 0))
            throw new InvalidOperationException("Expected true for (0, 0)");
        if (!grid.IsValidTile(119, 67))
            throw new InvalidOperationException("Expected true for (119, 67)");
    }

    [Test]
    public void IsValidTile_Invalid_ReturnsFalse()
    {
        var grid = new TileGrid<TTile16>(1920, 1080);
        if (grid.IsValidTile(120, 0))
            throw new InvalidOperationException("Expected false for (120, 0)");
        if (grid.IsValidTile(0, 68))
            throw new InvalidOperationException("Expected false for (0, 68)");
        if (grid.IsValidTile(-1, 0))
            throw new InvalidOperationException("Expected false for (-1, 0)");
    }

    [Test]
    public void TotalTiles_EqualsTileCountX_Times_TileCountY()
    {
        var grid = new TileGrid<TTile16>(1920, 1080);
        if (grid.TotalTiles != grid.TileCountX * grid.TileCountY)
            throw new InvalidOperationException($"TotalTiles {grid.TotalTiles} != TileCountX * TileCountY = {grid.TileCountX * grid.TileCountY}");
    }
}