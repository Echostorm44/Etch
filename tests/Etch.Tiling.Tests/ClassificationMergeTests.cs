using System;
using Etch.Geometry;
using Etch.Tiling;
using Etch.Tiling.Classify;
using TUnit;

namespace Etch.Tiling.Tests;

public sealed class ClassificationMergeTests
{
    [Test]
    public void Merge_EmptyEntries_ProducesEmptyScene()
    {
        var grid = new TileGrid<TTile16>(1920, 1080);

        var entries1 = Array.Empty<ClassificationEntry>();
        var entries2 = Array.Empty<ClassificationEntry>();

        var scene = ClassificationMerge.Merge(new[] { entries1, entries2 }, grid);

        if (scene.TileCount != grid.TotalTiles)
            throw new InvalidOperationException($"Expected {grid.TotalTiles}, got {scene.TileCount}");

        for (int i = 0; i < grid.TotalTiles; i++)
        {
            if (scene.Entries(i).Length != 0)
                throw new InvalidOperationException($"Expected 0 entries for tile {i}");
        }
    }

    [Test]
    public void Merge_OneEntrySet_ProducesCorrectScene()
    {
        var grid = new TileGrid<TTile16>(1920, 1080);

        var entries = new ClassificationEntry[]
        {
            new ClassificationEntry(0, 0, ClassificationKind.FillPath, default),
            new ClassificationEntry(1, 1, ClassificationKind.FillPath, default),
            new ClassificationEntry(0, 2, ClassificationKind.StrokePath, default),
        };

        var scene = ClassificationMerge.Merge(new[] { entries }, grid);

        if (scene.TileCount != grid.TotalTiles)
            throw new InvalidOperationException($"Expected {grid.TotalTiles}, got {scene.TileCount}");

        var tile0Entries = scene.Entries(0);
        if (tile0Entries.Length != 2)
            throw new InvalidOperationException($"Expected 2 entries for tile 0, got {tile0Entries.Length}");

        var tile1Entries = scene.Entries(1);
        if (tile1Entries.Length != 1)
            throw new InvalidOperationException($"Expected 1 entry for tile 1, got {tile1Entries.Length}");
    }

    [Test]
    public void Merge_MultipleEntrySets_StableSort()
    {
        var grid = new TileGrid<TTile16>(1920, 1080);

        var entries1 = new ClassificationEntry[]
        {
            new ClassificationEntry(0, 0, ClassificationKind.FillPath, default),
            new ClassificationEntry(0, 2, ClassificationKind.FillPath, default),
        };

        var entries2 = new ClassificationEntry[]
        {
            new ClassificationEntry(0, 1, ClassificationKind.FillPath, default),
            new ClassificationEntry(0, 3, ClassificationKind.FillPath, default),
        };

        var scene = ClassificationMerge.Merge(new[] { entries1, entries2 }, grid);

        var tile0Entries = scene.Entries(0);
        if (tile0Entries.Length != 4)
            throw new InvalidOperationException($"Expected 4 entries, got {tile0Entries.Length}");

        for (int i = 0; i < tile0Entries.Length; i++)
        {
            if (tile0Entries[i].CommandOrder != i)
                throw new InvalidOperationException($"Expected CommandOrder {i}, got {tile0Entries[i].CommandOrder}");
        }
    }

    [Test]
    public void Merge_DifferentThreadCounts_ProduceSameResult()
    {
        var grid = new TileGrid<TTile16>(1920, 1080);

        var entries1 = new ClassificationEntry[]
        {
            new ClassificationEntry(5, 0, ClassificationKind.FillPath, default),
            new ClassificationEntry(10, 1, ClassificationKind.StrokePath, default),
            new ClassificationEntry(5, 2, ClassificationKind.FillRect, default),
            new ClassificationEntry(7, 3, ClassificationKind.DrawImage, default),
            new ClassificationEntry(10, 4, ClassificationKind.FillPath, default),
        };

        var entriesA = new ClassificationEntry[]
        {
            new ClassificationEntry(5, 0, ClassificationKind.FillPath, default),
            new ClassificationEntry(10, 1, ClassificationKind.StrokePath, default),
            new ClassificationEntry(5, 2, ClassificationKind.FillRect, default),
        };

        var entriesB = new ClassificationEntry[]
        {
            new ClassificationEntry(7, 3, ClassificationKind.DrawImage, default),
            new ClassificationEntry(10, 4, ClassificationKind.FillPath, default),
        };

        var scene1 = ClassificationMerge.Merge(new[] { entriesA, entriesB }, grid);
        var scene2 = ClassificationMerge.Merge(new[] { entries1 }, grid);

        if (scene1.AllEntries.Length != scene2.AllEntries.Length)
            throw new InvalidOperationException($"Length mismatch: {scene1.AllEntries.Length} vs {scene2.AllEntries.Length}");

        for (int i = 0; i < scene1.AllEntries.Length; i++)
        {
            if (scene1.AllEntries[i].TileIndex != scene2.AllEntries[i].TileIndex)
                throw new InvalidOperationException($"TileIndex mismatch at {i}");
            if (scene1.AllEntries[i].CommandOrder != scene2.AllEntries[i].CommandOrder)
                throw new InvalidOperationException($"CommandOrder mismatch at {i}");
        }
    }
}