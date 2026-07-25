using System.Runtime.InteropServices;

namespace Etch.Tiling.Classify;

[StructLayout(LayoutKind.Sequential, Size = 16)]
public readonly struct ClassifiedScene
{
    private readonly ClassificationEntry[] _entries;
    private readonly int[] _tileOffsets;
    private readonly int _totalTiles;

    public ClassifiedScene(ClassificationEntry[] entries, int[] tileOffsets, int totalTiles)
    {
        _entries = entries;
        _tileOffsets = tileOffsets;
        _totalTiles = totalTiles;
    }

    public int TileCount => _totalTiles;

    public ReadOnlySpan<ClassificationEntry> Entries(int tileIndex)
    {
        if ((uint)tileIndex >= (uint)_totalTiles)
            return [];

        int start = _tileOffsets[tileIndex];
        int end = _tileOffsets[tileIndex + 1];
        return _entries.AsSpan(start, end - start);
    }

    public ReadOnlySpan<ClassificationEntry> AllEntries => _entries;
}