using System;
using System.Buffers;
using System.Collections.Generic;
using Etch.Tiling.Classify;

namespace Etch.Gpu.Compositor;

public static class DrawOrderPolicy
{
    public static void ApplyStableSort(ClassifiedScene scene, out ClassificationEntry[] sortedEntries)
    {
        var source = scene.AllEntries;
        sortedEntries = new ClassificationEntry[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            sortedEntries[i] = source[i];
        }

        Array.Sort(sortedEntries, new DrawOrderComparer());
    }

    private sealed class DrawOrderComparer : IComparer<ClassificationEntry>
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
