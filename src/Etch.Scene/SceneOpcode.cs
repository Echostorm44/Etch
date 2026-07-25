namespace Etch.Scene;

public enum SceneOpcode : byte
{
    Noop = 0,
    PushLayer = 1,
    PopLayer = 2,
    PushClip = 3,
    PopClip = 4,
    SetTransform = 5,
    FillPath = 6,
    StrokePath = 7,
    FillRect = 8,
    DrawImage = 9,
    DrawGlyphRun = 10,
    SetBlendMode = 11,
    BeginFrame = 12,
    EndFrame = 13,
    DrawShadow = 14,
    DrawMaterialRegion = 15,
    PushColorFilter = 16,
    PopColorFilter = 17,
    FillSector = 18,
}
