using System;
using System.Collections.Generic;

namespace Etch.Tiling.Classify;

public static class ClassificationMerge
{
    public static ClassifiedScene Merge<TTile>(ClassificationEntry[][] perThreadEntries, TileGrid<TTile> grid)
        where TTile : struct, ITileSize
    {
#pragma warning disable CA1062
        if (perThreadEntries == null)
            Etch.Panic.Invariant(Etch.PanicCodes.ArgumentNull, "perThreadEntries must not be null");

        int totalEntries = 0;
        foreach (var entries in perThreadEntries)
#pragma warning restore CA1062
        {
            totalEntries += entries.Length;
        }

        if (totalEntries == 0)
        {
            return new ClassifiedScene([], new int[grid.TotalTiles + 1], grid.TotalTiles);
        }

        var allEntries = new ClassificationEntry[totalEntries];
        int offset = 0;
        foreach (var entries in perThreadEntries)
        {
            entries.AsSpan().CopyTo(allEntries.AsSpan(offset));
            offset += entries.Length;
        }

        Array.Sort(allEntries, new TileOrderComparer());

        var offsets = new int[grid.TotalTiles + 1];
        int cursor = 0;
        for (int t = 0; t < grid.TotalTiles; t++)
        {
            offsets[t] = cursor;
            while (cursor < allEntries.Length && allEntries[cursor].TileIndex == t)
            {
                cursor++;
            }
        }
        offsets[grid.TotalTiles] = allEntries.Length;

        return new ClassifiedScene(allEntries, offsets, grid.TotalTiles);
    }

    private sealed class TileOrderComparer : IComparer<ClassificationEntry>
    {
        public int Compare(ClassificationEntry x, ClassificationEntry y)
        {
            int tileCompare = x.TileIndex - y.TileIndex;
            if (tileCompare != 0)
                return tileCompare;
            return x.CommandOrder - y.CommandOrder;
        }
    }
}