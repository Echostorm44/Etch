using Etch.Text.Atlas;
using TUnit;

namespace Etch.Text.Tests;

public sealed class ShelfPackerTests
{
    [Test]
    public async Task Uniform32x32PacksToHighOccupancy()
    {
        // With 1px padding, each 32x32 glyph becomes 33x33.
        // 1024/33 = 31 glyphs per row, 31 rows = 961 glyphs.
        // Occupancy = 961 * 32 * 32 / (1024 * 1024) ≈ 93.8%.
        var packer = new ShelfPacker(1024, 1024, 33);
        int count = 0;
        int usedArea = 0;
        while (packer.TryInsert(32, 32, out _, out _))
        {
            count++;
            usedArea += 32 * 32;
        }

        double occupancy = (double)usedArea / (1024 * 1024);
        if (occupancy < 0.93)
            throw new InvalidOperationException($"Occupancy {occupancy:P1} below 93% ({count} glyphs)");
    }

    [Test]
    public async Task VariableHeightOccupancyViaHorizontalFill()
    {
        // Row height must accommodate tallest glyph + 1px padding.
        var packer = new ShelfPacker(2048, 2048, 65);

        int count = 0;
        int lastY = 0;
        int lastX = 0;

        int[] heights = { 40, 55, 64, 50, 60 };

        while (packer.TryInsert(50, heights[count % heights.Length], out var x, out var y))
        {
            count++;
            lastY = y;
            lastX = x;
        }

        int rowsUsed = (lastY / 65) + 1;
        double horizontalOccupancy = (double)(count * 50) / (rowsUsed * 2048);
        if (horizontalOccupancy < 0.75)
            throw new InvalidOperationException($"Horizontal occupancy {horizontalOccupancy:P1} below 75% ({count} glyphs in {rowsUsed} rows)");
    }

    [Test]
    public async Task OverflowReturnsFalseWithoutException()
    {
        // 128x128 atlas, 32x32 glyphs with 1px padding → 33x33 each.
        // 128/33 = 3 per row, 3 rows = 9 glyphs max.
        var packer = new ShelfPacker(128, 128, 33);

        int successCount = 0;
        while (packer.TryInsert(32, 32, out _, out _))
        {
            successCount++;
            if (successCount > 20)
            {
                throw new InvalidOperationException("Infinite loop detected in shelf packer");
            }
        }

        if (successCount < 4)
            throw new InvalidOperationException($"Expected at least 4 successful inserts, got {successCount}");
    }

    [Test]
    public async Task ResetReturnsToEmpty()
    {
        // Row height must be > glyph height + 1px padding.
        var packer = new ShelfPacker(256, 256, 65);

        packer.TryInsert(64, 64, out _, out _);
        packer.TryInsert(64, 64, out _, out _);

        packer.Reset();

        bool first = packer.TryInsert(64, 64, out int x, out int y);
        if (!first || x != 0 || y != 0)
            throw new InvalidOperationException("After reset, first glyph should be at origin");
    }
}
