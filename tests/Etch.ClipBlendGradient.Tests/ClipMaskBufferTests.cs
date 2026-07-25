using System;
using Etch.Primitives;
using TUnit;

namespace Etch.ClipBlendGradient.Tests;

public sealed class ClipMaskBufferTests
{
    [Test]
    public void ClipStripSizeIsEightBytes()
    {
        if (System.Runtime.InteropServices.Marshal.SizeOf<ClipStrip>() != 8)
            throw new InvalidOperationException($"Expected sizeof(ClipStrip)=8, got {System.Runtime.InteropServices.Marshal.SizeOf<ClipStrip>()}");
    }

    [Test]
    public void RoundTripSerializeDeserializeByteIdentical()
    {
        var strips = new ClipStrip[]
        {
            new ClipStrip(0x00FF, 2, 5, 0),
            new ClipStrip(0x0003, 0, 3, 32),
        };

        var tileOffsets = new int[] { 0, 1, 2 };
        var coverageBytes = new byte[64];
        for (int i = 0; i < coverageBytes.Length; i++)
            coverageBytes[i] = (byte)i;

        var buffer = new ClipMaskBuffer(strips, tileOffsets, coverageBytes, 2);

        if (buffer.StripCount != 2)
            throw new InvalidOperationException($"Expected 2 strips, got {buffer.StripCount}");
        if (buffer.TileCount != 2)
            throw new InvalidOperationException($"Expected 2 tiles, got {buffer.TileCount}");
        if (buffer.CoverageBytes.Length != 64)
            throw new InvalidOperationException($"Expected 64 coverage bytes, got {buffer.CoverageBytes.Length}");
    }

    [Test]
    public void StripsForTileReturnsCorrectRange()
    {
        var strips = new ClipStrip[]
        {
            new ClipStrip(0x0001, 0, 10, 0),
            new ClipStrip(0x0002, 2, 8, 11),
            new ClipStrip(0x0004, 5, 15, 20),
        };

        var tileOffsets = new int[] { 0, 2, 3 };
        var coverageBytes = new byte[100];

        var buffer = new ClipMaskBuffer(strips, tileOffsets, coverageBytes, 2);

        var tile0Strips = buffer.StripsForTile(0);
        if (tile0Strips.Length != 2)
            throw new InvalidOperationException($"Expected 2 strips for tile 0, got {tile0Strips.Length}");

        var tile1Strips = buffer.StripsForTile(1);
        if (tile1Strips.Length != 1)
            throw new InvalidOperationException($"Expected 1 strip for tile 1, got {tile1Strips.Length}");
    }

    [Test]
    public void StripsForOutOfBoundsTileReturnsEmpty()
    {
        var strips = Array.Empty<ClipStrip>();
        var tileOffsets = new int[] { 0, 0 };
        var coverageBytes = Array.Empty<byte>();

        var buffer = new ClipMaskBuffer(strips, tileOffsets, coverageBytes, 1);

        var outOfBounds = buffer.StripsForTile(99);
        if (outOfBounds.Length != 0)
            throw new InvalidOperationException($"Expected empty span for out-of-bounds tile, got {outOfBounds.Length}");
    }

    [Test]
    public void CoverageForStripReturnsCorrectBytes()
    {
        var strips = new ClipStrip[]
        {
            new ClipStrip(0x0003, 2, 4, 0),
        };

        var tileOffsets = new int[] { 0, 1 };
        var coverageBytes = new byte[15];
        for (int i = 0; i < coverageBytes.Length; i++)
            coverageBytes[i] = (byte)(i * 17);

        var buffer = new ClipMaskBuffer(strips, tileOffsets, coverageBytes, 1);

        var coverage = buffer.CoverageForStrip(in strips[0]);
        int expectedLength = (4 - 2 + 1) * 2;
        if (coverage.Length != expectedLength)
            throw new InvalidOperationException($"Expected {expectedLength} coverage bytes, got {coverage.Length}");
    }

    [Test]
    public void ClipMaskBuilderAppendAndFinish()
    {
        var builder = new ClipMaskBuilder(4);

        builder.BeginTile(0);
        builder.Append(new ClipStrip(0x0001, 0, 10, 0));
        builder.Append(new ClipStrip(0x0002, 2, 8, 11));

        builder.BeginTile(1);
        builder.Append(new ClipStrip(0x0004, 5, 15, 22));

        var buffer = builder.Finish();

        if (buffer.TileCount != 2)
            throw new InvalidOperationException($"Expected 2 tiles, got {buffer.TileCount}");
        if (buffer.StripCount != 3)
            throw new InvalidOperationException($"Expected 3 strips, got {buffer.StripCount}");
    }

    [Test]
    public void ClipMaskBuilderReserveAndWriteCoverage()
    {
        var builder = new ClipMaskBuilder(2);

        builder.BeginTile(0);

        var coverage = new byte[] { 255, 128, 64, 32 };
        uint offset = builder.ReserveCoverage(coverage.Length);
        if (offset != 0)
            throw new InvalidOperationException($"Expected offset 0, got {offset}");

        builder.WriteCoverage(coverage);

        uint offset2 = builder.ReserveCoverage(coverage.Length);
        if (offset2 != (uint)(coverage.Length * 2))
            throw new InvalidOperationException($"Expected offset {coverage.Length * 2}, got {offset2}");

        var buffer = builder.Finish();

        if (buffer.CoverageBytes.Length != coverage.Length * 3)
            throw new InvalidOperationException($"Expected {coverage.Length * 3} coverage bytes, got {buffer.CoverageBytes.Length}");
    }

    [Test]
    public void RangeForTileReturnsCorrectRange()
    {
        var strips = new ClipStrip[]
        {
            new ClipStrip(0x0001, 0, 10, 0),
            new ClipStrip(0x0002, 2, 8, 11),
            new ClipStrip(0x0004, 5, 15, 20),
        };

        var tileOffsets = new int[] { 0, 2, 3 };
        var coverageBytes = new byte[100];

        var buffer = new ClipMaskBuffer(strips, tileOffsets, coverageBytes, 2);

        var range0 = buffer.RangeForTile(0);
        if (range0.StartIndex != 0 || range0.Length != 2)
            throw new InvalidOperationException($"Expected tile 0 range (0, 2), got ({range0.StartIndex}, {range0.Length})");

        var range1 = buffer.RangeForTile(1);
        if (range1.StartIndex != 2 || range1.Length != 1)
            throw new InvalidOperationException($"Expected tile 1 range (2, 1), got ({range1.StartIndex}, {range1.Length})");
    }

    [Test]
    public void PopcountReturnsCorrectValues()
    {
        if (ClipMaskBuffer.Popcount(0x0001) != 1)
            throw new InvalidOperationException($"Popcount(0x0001) expected 1");
        if (ClipMaskBuffer.Popcount(0x0003) != 2)
            throw new InvalidOperationException($"Popcount(0x0003) expected 2");
        if (ClipMaskBuffer.Popcount(0x00FF) != 8)
            throw new InvalidOperationException($"Popcount(0x00FF) expected 8");
        if (ClipMaskBuffer.Popcount(0xFFFF) != 16)
            throw new InvalidOperationException($"Popcount(0xFFFF) expected 16");
        if (ClipMaskBuffer.Popcount(0x0000) != 0)
            throw new InvalidOperationException($"Popcount(0x0000) expected 0");
    }
}
