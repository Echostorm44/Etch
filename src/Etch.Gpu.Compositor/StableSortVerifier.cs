using System;
using System.Diagnostics;
using Etch.Tiling.Classify;

namespace Etch.Gpu.Compositor;

public static class StableSortVerifier
{
    [Conditional("DEBUG")]
    public static void VerifySorted(ReadOnlySpan<ClassificationEntry> entries)
    {
        for (int i = 1; i < entries.Length; i++)
        {
            var prev = entries[i - 1];
            var curr = entries[i];

            if (prev.TileIndex > curr.TileIndex)
            {
                Etch.Panic.Invariant(
                    Etch.PanicCodes.UnsortedDrawOrder,
                    $"Unsorted draw order at index {i}: TileIndex {prev.TileIndex} > {curr.TileIndex}");
            }

            if (prev.TileIndex == curr.TileIndex && prev.CommandOrder > curr.CommandOrder)
            {
                Etch.Panic.Invariant(
                    Etch.PanicCodes.UnsortedDrawOrder,
                    $"Unsorted draw order at index {i}: CommandOrder {prev.CommandOrder} > {curr.CommandOrder} for tile {prev.TileIndex}");
            }
        }
    }

    [Conditional("DEBUG")]
    public static void VerifySorted(ClassifiedScene scene)
    {
        VerifySorted(scene.AllEntries);
    }
}
