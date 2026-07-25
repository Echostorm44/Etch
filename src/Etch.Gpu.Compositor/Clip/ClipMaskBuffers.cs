using Etch.Gpu;

namespace Etch.Gpu.Compositor.Clip;

public readonly struct ClipMaskBuffers : IDisposable
{
    public const int MaxClipLevels = 16;
    public const int AtlasSize = 2048;
    public const int SlotSize = 512;
    public const int SlotsPerRow = AtlasSize / SlotSize;

    public Texture R8AlphaAtlas { get; }
    public Buffer TileMaskOffsets { get; }
    public int Width { get; }
    public int Height { get; }

    public ClipMaskBuffers(Texture r8AlphaAtlas, Buffer tileMaskOffsets, int width, int height)
    {
        R8AlphaAtlas = r8AlphaAtlas;
        TileMaskOffsets = tileMaskOffsets;
        Width = width;
        Height = height;
    }

    public static bool CanFitSlot(int slotIndex)
    {
        int slotX = slotIndex % SlotsPerRow;
        int slotY = slotIndex / SlotsPerRow;
        return slotX < SlotsPerRow && slotY < SlotsPerRow;
    }

    public void Dispose()
    {
        R8AlphaAtlas.Dispose();
        TileMaskOffsets.Dispose();
    }
}
