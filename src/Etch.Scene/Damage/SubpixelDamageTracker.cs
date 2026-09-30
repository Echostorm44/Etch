using System;
using System.Buffers;
using Etch.Geometry;

namespace Etch.Scene.Damage;

/// <summary>
/// Pixel-precise damage tracking (SCN-008): reports the device-space rectangles covered by commands
/// that changed between two frames, merged until disjoint. When more than <see cref="MaxRects"/>
/// disjoint rectangles would be needed it falls back to a tile bitmap.
/// </summary>
/// <remarks>
/// Commands are paired by index. A pair is unchanged when both describe the same device bounds and
/// content hash; otherwise both the previous and the current bounds are dirty. Arrays referenced by a
/// returned <see cref="DamageResult"/> are owned by this tracker and are only valid until the next
/// <see cref="DiffSubpixel"/> call. After warm-up, <see cref="DiffSubpixel"/> allocates nothing.
/// </remarks>
public sealed class SubpixelDamageTracker
{
    private const int MaxRects = 32;
    private const double TileSize = 32.0;

    private readonly int _tileCountX;
    private readonly int _tileCountY;
    private readonly Rect _deviceBounds;
    private readonly bool[] _dirtyTiles;
    private readonly Rect[]?[] _dirtyRectArraysByCount;
    private Rect[] _rectBuffer;
    private int _rectCount;
    private bool _rectCapExceeded;

    public static SubpixelDamageTracker Create(int deviceWidth, int deviceHeight)
    {
        int tileCountX = (int)Math.Ceiling((double)deviceWidth / TileSize);
        int tileCountY = (int)Math.Ceiling((double)deviceHeight / TileSize);
        return new SubpixelDamageTracker(deviceWidth, deviceHeight, tileCountX, tileCountY);
    }

    private SubpixelDamageTracker(int deviceWidth, int deviceHeight, int tileCountX, int tileCountY)
    {
        _tileCountX = tileCountX;
        _tileCountY = tileCountY;
        _deviceBounds = Rect.FromLTRB(0, 0, deviceWidth, deviceHeight);
        _dirtyTiles = new bool[tileCountX * tileCountY];
        _dirtyRectArraysByCount = new Rect[MaxRects + 1][];
        _rectBuffer = ArrayPool<Rect>.Shared.Rent(MaxRects);
        _rectCount = 0;
    }

    public DamageResult DiffSubpixel(SceneBuffer prev, SceneBuffer curr)
    {
        ArgumentNullException.ThrowIfNull(prev);
        ArgumentNullException.ThrowIfNull(curr);

        _rectCount = 0;
        _rectCapExceeded = false;
        CollectChangedBounds(prev, curr, markTiles: false);

        if (_rectCapExceeded)
        {
            Array.Clear(_dirtyTiles);
            CollectChangedBounds(prev, curr, markTiles: true);
            int dirtyCount = 0;
            for (int i = 0; i < _dirtyTiles.Length; i++)
            {
                if (_dirtyTiles[i])
                    dirtyCount++;
            }
            return new DamageResult(_dirtyTiles, dirtyCount);
        }

        Rect[] dirtyRects = GetDirtyRectArray(_rectCount);
        Array.Copy(_rectBuffer, dirtyRects, _rectCount);
        return new DamageResult(dirtyRects);
    }

    private void CollectChangedBounds(SceneBuffer prev, SceneBuffer curr, bool markTiles)
    {
        var prevCommands = prev.Commands;
        var currCommands = curr.Commands;
        var prevXform = Affine.Identity;
        var currXform = Affine.Identity;
        int commandCount = Math.Max(prevCommands.Length, currCommands.Length);

        for (int i = 0; i < commandCount; i++)
        {
            bool prevDraws = false;
            bool currDraws = false;
            Rect prevBounds = Rect.Empty;
            Rect currBounds = Rect.Empty;
            ulong prevHash = 0;
            ulong currHash = 0;

            if (i < prevCommands.Length)
            {
                ref readonly var prevCmd = ref prevCommands[i];
                if (prevCmd.Op == SceneOpcode.SetTransform)
                    prevXform = prev.GetTransform(prevCmd.SetTransform.TransformId);
                else
                    prevDraws = CommandHasher.TryDescribeDrawCommand(prevCmd, prev, prevXform, out prevBounds, out prevHash);
            }

            if (i < currCommands.Length)
            {
                ref readonly var currCmd = ref currCommands[i];
                if (currCmd.Op == SceneOpcode.SetTransform)
                    currXform = curr.GetTransform(currCmd.SetTransform.TransformId);
                else
                    currDraws = CommandHasher.TryDescribeDrawCommand(currCmd, curr, currXform, out currBounds, out currHash);
            }

            if (prevDraws && currDraws && prevHash == currHash && prevBounds == currBounds)
                continue;

            if (prevDraws)
                AddDirtyBounds(prevBounds, markTiles);
            if (currDraws)
                AddDirtyBounds(currBounds, markTiles);

            if (_rectCapExceeded && !markTiles)
                return;
        }
    }

    private void AddDirtyBounds(Rect deviceBounds, bool markTiles)
    {
        Rect clipped = deviceBounds.Intersect(_deviceBounds);
        if (clipped.IsEmpty)
            return;

        if (markTiles)
            MarkTiles(clipped);
        else
            MergeRect(clipped);
    }

    // Merging can make the grown rectangle overlap ones it previously missed, so the scan restarts
    // after every merge; the buffer therefore always holds pairwise-disjoint rectangles.
    private void MergeRect(Rect rect)
    {
        Rect merged = rect;
        int index = 0;
        while (index < _rectCount)
        {
            if (_rectBuffer[index].Intersects(merged))
            {
                merged = merged.Union(_rectBuffer[index]);
                _rectBuffer[index] = _rectBuffer[--_rectCount];
                index = 0;
                continue;
            }
            index++;
        }

        if (_rectCount == MaxRects)
        {
            _rectCapExceeded = true;
            return;
        }

        _rectBuffer[_rectCount++] = merged;
    }

    private void MarkTiles(Rect rect)
    {
        int minTileX = Math.Max(0, (int)Math.Floor(rect.MinX / TileSize));
        int minTileY = Math.Max(0, (int)Math.Floor(rect.MinY / TileSize));
        int maxTileX = Math.Min(_tileCountX - 1, (int)Math.Floor((rect.MaxX - 1e-10) / TileSize));
        int maxTileY = Math.Min(_tileCountY - 1, (int)Math.Floor((rect.MaxY - 1e-10) / TileSize));

        for (int tileY = minTileY; tileY <= maxTileY; tileY++)
        {
            int rowStart = tileY * _tileCountX;
            for (int tileX = minTileX; tileX <= maxTileX; tileX++)
                _dirtyTiles[rowStart + tileX] = true;
        }
    }

    // DamageResult exposes an exact-length array, so one array per possible count is created on
    // first use and reused afterwards.
    private Rect[] GetDirtyRectArray(int count)
    {
        if (count == 0)
            return Array.Empty<Rect>();

        return _dirtyRectArraysByCount[count] ??= new Rect[count];
    }

    public void Dispose()
    {
        ArrayPool<Rect>.Shared.Return(_rectBuffer);
        _rectBuffer = Array.Empty<Rect>();
    }
}
