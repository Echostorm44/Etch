using System;
using System.Buffers;

namespace Etch.ClipBlendGradient;

public ref struct ClipMaskBuilder
{
    private const int InitialStripsCapacity = 256;
    private const int InitialTileOffsetsCapacity = 64;
    private const int InitialCoverageCapacity = 4096;

    private ClipStrip[] _strips;
    private int _stripCount;
    private int _stripCapacity;

    private int[] _tileOffsets;
    private int _tileCount;
    private int _tileOffsetsCapacity;

    private byte[] _coverageBytes;
    private int _coverageUsed;
    private int _coverageCapacity;

    public ClipMaskBuilder(int estimatedTiles = 64)
    {
        _strips = ArrayPool<ClipStrip>.Shared.Rent(InitialStripsCapacity);
        _stripCount = 0;
        _stripCapacity = InitialStripsCapacity;

        _tileOffsets = ArrayPool<int>.Shared.Rent(InitialTileOffsetsCapacity + 1);
        _tileCount = 0;
        _tileOffsetsCapacity = InitialTileOffsetsCapacity;

        _coverageBytes = ArrayPool<byte>.Shared.Rent(InitialCoverageCapacity);
        _coverageUsed = 0;
        _coverageCapacity = InitialCoverageCapacity;

        _tileOffsets[_tileCount++] = 0;
    }

    public void BeginTile(int tileIndex)
    {
        while (_tileCount <= tileIndex)
        {
            EnsureTileOffsetsCapacity(_tileCount + 1);
            _tileOffsets[_tileCount] = _stripCount;
            _tileCount++;
        }
    }

    public void Append(in ClipStrip strip)
    {
        EnsureStripsCapacity(_stripCount + 1);
        _strips[_stripCount++] = strip;
    }

    public uint ReserveCoverage(int byteCount)
    {
        EnsureCoverageCapacity(_coverageUsed + byteCount);
        uint offset = (uint)_coverageUsed;
        _coverageUsed += byteCount;
        return offset;
    }

    public void WriteCoverage(ReadOnlySpan<byte> data)
    {
        EnsureCoverageCapacity(_coverageUsed + data.Length);
        data.CopyTo(_coverageBytes.AsSpan(_coverageUsed));
        _coverageUsed += data.Length;
    }

    public ClipMaskBuffer Finish()
    {
        if (_tileCount > 0)
        {
            _tileOffsets[_tileCount] = _stripCount;
        }

        int actualTileCount = _tileCount > 0 ? _tileCount : 0;

        var strips = _strips.AsSpan(0, _stripCount).ToArray();
        var tileOffsets = _tileOffsets.AsSpan(0, actualTileCount + 1).ToArray();
        var coverageBytes = _coverageBytes.AsSpan(0, _coverageUsed).ToArray();

        ReturnBuffers();

        return new ClipMaskBuffer(strips, tileOffsets, coverageBytes, actualTileCount);
    }

    private void ReturnBuffers()
    {
        if (_strips != null)
        {
            ArrayPool<ClipStrip>.Shared.Return(_strips, clearArray: true);
            _strips = null!;
        }
        if (_tileOffsets != null)
        {
            ArrayPool<int>.Shared.Return(_tileOffsets, clearArray: true);
            _tileOffsets = null!;
        }
        if (_coverageBytes != null)
        {
            ArrayPool<byte>.Shared.Return(_coverageBytes, clearArray: true);
            _coverageBytes = null!;
        }
    }

    private void EnsureStripsCapacity(int required)
    {
        if (required <= _stripCapacity)
            return;

        int newCapacity = Math.Max(required, _stripCapacity * 2);
        var newStrips = ArrayPool<ClipStrip>.Shared.Rent(newCapacity);
        _strips.AsSpan(0, _stripCount).CopyTo(newStrips);
        ArrayPool<ClipStrip>.Shared.Return(_strips, clearArray: true);
        _strips = newStrips;
        _stripCapacity = newCapacity;
    }

    private void EnsureTileOffsetsCapacity(int required)
    {
        if (required <= _tileOffsetsCapacity)
            return;

        int newCapacity = Math.Max(required, _tileOffsetsCapacity * 2);
        var newOffsets = ArrayPool<int>.Shared.Rent(newCapacity);
        _tileOffsets.AsSpan(0, _tileCount).CopyTo(newOffsets);
        ArrayPool<int>.Shared.Return(_tileOffsets, clearArray: true);
        _tileOffsets = newOffsets;
        _tileOffsetsCapacity = newCapacity;
    }

    private void EnsureCoverageCapacity(int required)
    {
        if (required <= _coverageCapacity)
            return;

        int newCapacity = Math.Max(required, _coverageCapacity * 2);
        var newCoverage = ArrayPool<byte>.Shared.Rent(newCapacity);
        _coverageBytes.AsSpan(0, _coverageUsed).CopyTo(newCoverage);
        ArrayPool<byte>.Shared.Return(_coverageBytes, clearArray: true);
        _coverageBytes = newCoverage;
        _coverageCapacity = newCapacity;
    }
}
