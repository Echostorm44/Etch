using System;
using System.Runtime.InteropServices;
using Etch.Tiling.Strips;
using TUnit;

namespace Etch.Tiling.Tests;

internal sealed class StripFormatTests
{
    private static readonly Strip[] s_singleStripArray = new[] { new Strip(tileIndex: 0, rowMask: 1, x0: 0, x1: 15, coverageOffset: 0, paintId: 0) };
    private static readonly int[] s_singleOffset = new[] { 0, 1 };

    [Test]
    public void Strip_SizeIs24()
    {
        if (Marshal.SizeOf<Strip>() != 24)
            throw new InvalidOperationException($"sizeof(Strip) = {Marshal.SizeOf<Strip>()}, expected 24");
    }

    [Test]
    public void Strip_Construction()
    {
        var strip = new Strip(tileIndex: 5, rowMask: 0x00FF, x0: 2, x1: 5, coverageOffset: 100, paintId: 42);

        if (strip.TileIndex != 5)
            throw new InvalidOperationException($"TileIndex = {strip.TileIndex}");
        if (strip.RowMask != 0x00FF)
            throw new InvalidOperationException($"RowMask = {strip.RowMask}");
        if (strip.X0 != 2)
            throw new InvalidOperationException($"X0 = {strip.X0}");
        if (strip.X1 != 5)
            throw new InvalidOperationException($"X1 = {strip.X1}");
        if (strip.CoverageOffset != 100)
            throw new InvalidOperationException($"CoverageOffset = {strip.CoverageOffset}");
        if (strip.PaintId != 42)
            throw new InvalidOperationException($"PaintId = {strip.PaintId}");
    }

    [Test]
    public void StripBuffer_EmptyScene()
    {
        var buffer = new StripBuffer([], [], [], 0);

        if (buffer.TileCount != 0)
            throw new InvalidOperationException($"TileCount = {buffer.TileCount}");
        if (buffer.StripCount != 0)
            throw new InvalidOperationException($"StripCount = {buffer.StripCount}");
        if (buffer.StripsForTile(0).Length != 0)
            throw new InvalidOperationException("Empty buffer should have no strips for tile 0");
    }

    [Test]
    public void StripBuffer_StripsForTile_ReturnsCorrectRange()
    {
        var strips = new Strip[]
        {
            new Strip(tileIndex: 0, rowMask: 1, x0: 0, x1: 15, coverageOffset: 0, paintId: 0),
            new Strip(tileIndex: 0, rowMask: 2, x0: 0, x1: 15, coverageOffset: 16, paintId: 0),
            new Strip(tileIndex: 1, rowMask: 1, x0: 0, x1: 15, coverageOffset: 32, paintId: 0),
        };
        var offsets = new[] { 0, 2, 3, 3 };
        var coverage = new byte[64];
        var buffer = new StripBuffer(strips, offsets, coverage, tileCount: 3);

        var tile0Strips = buffer.StripsForTile(0);
        if (tile0Strips.Length != 2)
            throw new InvalidOperationException($"Tile 0 should have 2 strips, got {tile0Strips.Length}");

        var tile1Strips = buffer.StripsForTile(1);
        if (tile1Strips.Length != 1)
            throw new InvalidOperationException($"Tile 1 should have 1 strip, got {tile1Strips.Length}");

        var tile2Strips = buffer.StripsForTile(2);
        if (tile2Strips.Length != 0)
            throw new InvalidOperationException($"Tile 2 should have 0 strips, got {tile2Strips.Length}");
    }

    [Test]
    public void StripBuffer_StripsForTile_OutOfBounds_ReturnsEmpty()
    {
        var buffer = new StripBuffer([], [], [], 3);

        if (buffer.StripsForTile(-1).Length != 0)
            throw new InvalidOperationException("Negative index should return empty");
        if (buffer.StripsForTile(3).Length != 0)
            throw new InvalidOperationException("Index >= tileCount should return empty");
    }

    [Test]
    public void StripBuffer_CoverageForStrip_ReturnsCorrectSlice()
    {
        var strip = new Strip(tileIndex: 0, rowMask: 0x0003, x0: 0, x1: 3, coverageOffset: 10, paintId: 0);
        var coverage = new byte[100];
        for (int i = 0; i < coverage.Length; i++)
            coverage[i] = (byte)(i + 1);
        var buffer = new StripBuffer(s_singleStripArray, s_singleOffset, coverage, tileCount: 1);

        var slice = buffer.CoverageForStrip(in strip);

        int expectedLength = (3 - 0 + 1) * 2;
        if (slice.Length != expectedLength)
            throw new InvalidOperationException($"Expected {expectedLength} bytes, got {slice.Length}");
    }

    [Test]
    public void StripBuffer_CoverageForStrip_OutOfBounds_ReturnsEmpty()
    {
        var strip = new Strip(tileIndex: 0, rowMask: 1, x0: 0, x1: 15, coverageOffset: 1000, paintId: 0);
        var coverage = new byte[100];
        var buffer = new StripBuffer(s_singleStripArray, s_singleOffset, coverage, tileCount: 1);

        var slice = buffer.CoverageForStrip(in strip);
        if (slice.Length != 0)
            throw new InvalidOperationException("Out-of-bounds coverage should return empty");
    }

    [Test]
    public void StripsBuilder_BasicRoundTrip()
    {
        var builder = new StripsBuilder();
        builder.Reset(tileCount: 2);

        var coverage1 = new byte[] { 1, 2, 3, 4 };
        builder.AddStrip(new Strip(tileIndex: 0, rowMask: 1, x0: 0, x1: 3, coverageOffset: 0, paintId: 0), coverage1);

        var coverage2 = new byte[] { 5, 6 };
        builder.AddStrip(new Strip(tileIndex: 1, rowMask: 1, x0: 0, x1: 1, coverageOffset: 4, paintId: 0), coverage2);

        var buffer = builder.Finish();

        if (buffer.TileCount != 2)
            throw new InvalidOperationException($"TileCount = {buffer.TileCount}");
        if (buffer.StripCount != 2)
            throw new InvalidOperationException($"StripCount = {buffer.StripCount}");
        if (buffer.StripsForTile(0).Length != 1)
            throw new InvalidOperationException($"Tile 0 should have 1 strip");
        if (buffer.StripsForTile(1).Length != 1)
            throw new InvalidOperationException($"Tile 1 should have 1 strip");

        builder.Dispose();
    }

    [Test]
    public void StripsBuilder_EmptyBuilder()
    {
        var builder = new StripsBuilder();
        builder.Reset(tileCount: 0);
        var buffer = builder.Finish();

        if (buffer.TileCount != 0)
            throw new InvalidOperationException($"TileCount = {buffer.TileCount}");
        if (buffer.StripCount != 0)
            throw new InvalidOperationException($"StripCount = {buffer.StripCount}");

        builder.Dispose();
    }

    [Test]
    public void StripBuffer_Popcount()
    {
        if (StripBuffer.popcount(0x0001) != 1)
            throw new InvalidOperationException("popcount(0x0001) = 1");
        if (StripBuffer.popcount(0x0003) != 2)
            throw new InvalidOperationException("popcount(0x0003) = 2");
        if (StripBuffer.popcount(0x00FF) != 8)
            throw new InvalidOperationException("popcount(0x00FF) = 8");
        if (StripBuffer.popcount(0xFFFF) != 16)
            throw new InvalidOperationException("popcount(0xFFFF) = 16");
        if (StripBuffer.popcount(0) != 0)
            throw new InvalidOperationException("popcount(0) = 0");
    }
}
