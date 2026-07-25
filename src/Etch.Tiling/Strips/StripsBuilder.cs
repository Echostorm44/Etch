using System;
using System.Buffers;

namespace Etch.Tiling.Strips;

public ref struct StripsBuilder
{
    private Strip[]? _stripsArray;
    private byte[]? _coverageArray;
    private int _stripCount;
    private int _stripCapacity;
    private int _coverageCount;
    private int _coverageCapacity;
    private int _tileCount;

    public StripsBuilder(int initialCapacity = 256)
    {
        _stripsArray = null;
        _coverageArray = null;
        _stripCount = 0;
        _stripCapacity = 0;
        _coverageCount = 0;
        _coverageCapacity = 0;
        _tileCount = 0;
    }

    public void Reset(int tileCount)
    {
        if (_stripsArray != null)
            ArrayPool<Strip>.Shared.Return(_stripsArray);
        if (_coverageArray != null)
            ArrayPool<byte>.Shared.Return(_coverageArray);

        _stripsArray = ArrayPool<Strip>.Shared.Rent(Math.Max(256, tileCount * 4));
        _coverageArray = ArrayPool<byte>.Shared.Rent(8192);
        _stripCount = 0;
        _stripCapacity = _stripsArray.Length;
        _coverageCount = 0;
        _coverageCapacity = _coverageArray.Length;
        _tileCount = tileCount;
    }

    public int CoverageCount => _coverageCount;

    public void AddStrip(Strip strip, ReadOnlySpan<byte> coverage)
    {
        if (_stripsArray == null || _coverageArray == null)
            return;

        EnsureStripCapacity();
        _stripsArray[_stripCount++] = strip;

        if (coverage.Length > 0)
        {
            EnsureCoverageCapacity(coverage.Length);
            coverage.CopyTo(_coverageArray.AsSpan(_coverageCount));
            _coverageCount += coverage.Length;
        }
    }

    private void EnsureStripCapacity()
    {
        if (_stripCount >= _stripCapacity)
        {
            var newArray = ArrayPool<Strip>.Shared.Rent(_stripCapacity * 2);
            _stripsArray.AsSpan(0, _stripCount).CopyTo(newArray);
            ArrayPool<Strip>.Shared.Return(_stripsArray!);
            _stripsArray = newArray;
            _stripCapacity = newArray.Length;
        }
    }

    private void EnsureCoverageCapacity(int needed)
    {
        if (_coverageCount + needed > _coverageCapacity)
        {
            var newArray = ArrayPool<byte>.Shared.Rent(_coverageCapacity * 2);
            _coverageArray.AsSpan(0, _coverageCount).CopyTo(newArray);
            ArrayPool<byte>.Shared.Return(_coverageArray!);
            _coverageArray = newArray;
            _coverageCapacity = newArray.Length;
        }
    }

    public StripBuffer Finish()
    {
        if (_stripsArray == null || _coverageArray == null)
            return new StripBuffer([], [], [], 0);

        var strips = new Strip[_stripCount];
        _stripsArray.AsSpan(0, _stripCount).CopyTo(strips);

        var coverage = new byte[_coverageCount];
        _coverageArray.AsSpan(0, _coverageCount).CopyTo(coverage);

        var offsets = ComputeTileOffsets(strips, _tileCount);

        return new StripBuffer(strips, offsets, coverage, _tileCount);
    }

    private static int[] ComputeTileOffsets(Strip[] strips, int tileCount)
    {
        var counts = new int[tileCount];
        for (int i = 0; i < strips.Length; i++)
        {
            int tileIdx = (int)strips[i].TileIndex;
            if ((uint)tileIdx < (uint)tileCount)
                counts[tileIdx]++;
        }

        var offsets = new int[tileCount + 1];
        int running = 0;
        for (int i = 0; i < tileCount; i++)
        {
            offsets[i] = running;
            running += counts[i];
        }
        offsets[tileCount] = running;

        return offsets;
    }

    public void Dispose()
    {
        if (_stripsArray != null)
        {
            ArrayPool<Strip>.Shared.Return(_stripsArray);
            _stripsArray = null;
        }
        if (_coverageArray != null)
        {
            ArrayPool<byte>.Shared.Return(_coverageArray);
            _coverageArray = null;
        }
    }
}
