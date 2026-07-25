using System;

namespace Etch.Tiling.Strips;

public sealed class StripBuffer
{
    private readonly Strip[] _strips;
    private readonly int[] _tileOffsets;
    private readonly byte[] _coverageBytes;
    private readonly int _tileCount;

    public StripBuffer(Strip[] strips, int[] tileOffsets, byte[] coverageBytes, int tileCount)
    {
        _strips = strips;
        _tileOffsets = tileOffsets;
        _coverageBytes = coverageBytes;
        _tileCount = tileCount;
    }

    public int TileCount => _tileCount;
    public int StripCount => _strips.Length;
    public ReadOnlySpan<Strip> Strips => _strips;
    public ReadOnlySpan<byte> CoverageBytes => _coverageBytes;

    public ReadOnlySpan<Strip> StripsForTile(int tileIndex)
    {
        if ((uint)tileIndex >= (uint)_tileCount)
            return [];

        int start = _tileOffsets[tileIndex];
        int end = _tileOffsets[tileIndex + 1];
        return _strips.AsSpan(start, end - start);
    }

    public StripRange RangeForTile(int tileIndex)
    {
        if ((uint)tileIndex >= (uint)_tileCount)
            return new StripRange(0, 0);
        int start = _tileOffsets[tileIndex];
        int end = _tileOffsets[tileIndex + 1];
        return new StripRange(start, end - start);
    }

    public ReadOnlySpan<byte> CoverageForStrip(in Strip strip)
    {
        int length = (int)((strip.X1 - strip.X0 + 1) * (uint)popcount(strip.RowMask));
        if ((uint)(strip.CoverageOffset + length) > (uint)_coverageBytes.Length)
            return [];
        return _coverageBytes.AsSpan((int)strip.CoverageOffset, length);
    }

    public static int popcount(uint value)
    {
        int count = 0;
        uint v = value;
        while (v != 0)
        {
            count++;
            v &= v - 1;
        }
        return count;
    }
}

public readonly struct StripRange
{
    public readonly int StartIndex;
    public readonly int Length;

    public StripRange(int startIndex, int length)
    {
        StartIndex = startIndex;
        Length = length;
    }
}
