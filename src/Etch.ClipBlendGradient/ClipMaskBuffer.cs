using System;

namespace Etch.ClipBlendGradient;

public sealed class ClipMaskBuffer
{
    private readonly ClipStrip[] _strips;
    private readonly int[] _tileOffsets;
    private readonly byte[] _coverageBytes;
    private readonly int _tileCount;

    public ClipMaskBuffer(ClipStrip[] strips, int[] tileOffsets, byte[] coverageBytes, int tileCount)
    {
        _strips = strips;
        _tileOffsets = tileOffsets;
        _coverageBytes = coverageBytes;
        _tileCount = tileCount;
    }

    public int TileCount => _tileCount;
    public int StripCount => _strips.Length;
    public ReadOnlySpan<ClipStrip> Strips => _strips;
    public ReadOnlySpan<byte> CoverageBytes => _coverageBytes;

    public ReadOnlySpan<ClipStrip> StripsForTile(int tileIndex)
    {
        if ((uint)tileIndex >= (uint)_tileCount)
            return [];

        int start = _tileOffsets[tileIndex];
        int end = _tileOffsets[tileIndex + 1];
        return _strips.AsSpan(start, end - start);
    }

    public ClipStripRange RangeForTile(int tileIndex)
    {
        if ((uint)tileIndex >= (uint)_tileCount)
            return new ClipStripRange(0, 0);
        int start = _tileOffsets[tileIndex];
        int end = _tileOffsets[tileIndex + 1];
        return new ClipStripRange(start, end - start);
    }

    public ReadOnlySpan<byte> CoverageForStrip(in ClipStrip strip)
    {
        int length = (strip.X1 - strip.X0 + 1) * Popcount(strip.RowMask);
        if ((uint)(strip.CoverageOffset + length) > (uint)_coverageBytes.Length)
            return [];
        return _coverageBytes.AsSpan((int)strip.CoverageOffset, length);
    }

    public static int Popcount(ushort value)
    {
        int count = 0;
        ushort v = value;
        while (v != 0)
        {
            count++;
            v &= (ushort)(v - 1);
        }
        return count;
    }
}

public readonly struct ClipStripRange
{
    public readonly int StartIndex;
    public readonly int Length;

    public ClipStripRange(int startIndex, int length)
    {
        StartIndex = startIndex;
        Length = length;
    }
}
