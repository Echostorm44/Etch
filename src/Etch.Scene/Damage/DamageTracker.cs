using System;
using System.Buffers;
using Etch.Geometry;

namespace Etch.Scene.Damage;

public sealed class DamageTracker
{
    private const int MaxHashesPerTile = 32;

    private readonly int _tileCountX;
    private readonly int _tileCountY;
    private readonly int _totalTiles;
    private ulong[] _prevHashes;
    private ulong[] _currHashes;
    private readonly bool[] _dirtyTiles;
    private bool _allDirty;

    public static DamageTracker Create(int tileCountX, int tileCountY)
    {
        return new DamageTracker(tileCountX, tileCountY);
    }

    private DamageTracker(int tileCountX, int tileCountY)
    {
        _tileCountX = tileCountX;
        _tileCountY = tileCountY;
        _totalTiles = tileCountX * tileCountY;
        _prevHashes = ArrayPool<ulong>.Shared.Rent(_totalTiles * MaxHashesPerTile);
        _currHashes = ArrayPool<ulong>.Shared.Rent(_totalTiles * MaxHashesPerTile);
        _dirtyTiles = new bool[_totalTiles];
        _allDirty = false;
    }

    public void Reset()
    {
        _allDirty = false;
        Array.Clear(_prevHashes, 0, _prevHashes.Length);
        Array.Clear(_currHashes, 0, _currHashes.Length);
        Array.Clear(_dirtyTiles, 0, _dirtyTiles.Length);
    }

    public void MarkAllDirty()
    {
        _allDirty = true;
    }

    public DamageResult Diff(SceneBuffer prev, SceneBuffer curr)
    {
#pragma warning disable CA1062
        bool allDirtyThisCall = _allDirty;
        _allDirty = false;

        if (allDirtyThisCall)
        {
            for (int i = 0; i < _totalTiles; i++)
                _dirtyTiles[i] = true;

            CommandHasher.HashCommandsToTiles(prev.Commands, prev, _tileCountX, _tileCountY, _prevHashes);

            return new DamageResult(_dirtyTiles, _totalTiles);
        }

        Array.Clear(_currHashes, 0, _currHashes.Length);

        CommandHasher.HashCommandsToTiles(curr.Commands, curr, _tileCountX, _tileCountY, _currHashes);

        int dirtyCount = 0;
        for (int tileIdx = 0; tileIdx < _totalTiles; tileIdx++)
        {
            int baseIdx = tileIdx * MaxHashesPerTile;

            int prevCount = 0;
            for (int h = 0; h < MaxHashesPerTile; h++)
            {
                if (_prevHashes[baseIdx + h] != 0)
                    prevCount++;
                else
                    break;
            }

            int currCount = 0;
            for (int h = 0; h < MaxHashesPerTile; h++)
            {
                if (_currHashes[baseIdx + h] != 0)
                    currCount++;
                else
                    break;
            }

            if (prevCount != currCount)
            {
                _dirtyTiles[tileIdx] = true;
                dirtyCount++;
                continue;
            }

            bool isDirty = false;
            for (int i = 0; i < prevCount; i++)
            {
                ulong hash = _prevHashes[baseIdx + i];
                bool found = false;
                for (int j = 0; j < currCount; j++)
                {
                    if (_currHashes[baseIdx + j] == hash)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    isDirty = true;
                    break;
                }
            }

            _dirtyTiles[tileIdx] = isDirty;
            if (isDirty)
                dirtyCount++;
        }

        var temp = _prevHashes;
        _prevHashes = _currHashes;
        _currHashes = temp;

        Array.Clear(_currHashes, 0, _currHashes.Length);

        return new DamageResult(_dirtyTiles, dirtyCount);
#pragma warning restore CA1062
    }

    public void Dispose()
    {
        ArrayPool<ulong>.Shared.Return(_prevHashes);
        ArrayPool<ulong>.Shared.Return(_currHashes);
    }
}