namespace Etch.Tiling.Classify;

public enum ClassificationKind : byte
{
    FillPath = 0,
    StrokePath = 1,
    FillRect = 2,
    DrawImage = 3,
    DrawGlyphRun = 4,
}