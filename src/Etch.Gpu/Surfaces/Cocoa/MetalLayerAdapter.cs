#if NET || NETSTANDARD
namespace Etch.Gpu.Surfaces.Cocoa;

public enum MetalLayerType
{
    CaMetalLayer,
    NSView
}

public static class MetalLayerAdapter
{
    public static void ValidateOrPanic(nint pointer, MetalLayerType suggestedType)
    {
        if (pointer == 0)
        {
            Panic.ArgumentOutOfRange(nameof(pointer), "Pointer cannot be null.");
        }

        if (suggestedType == MetalLayerType.NSView)
        {
            Panic.ArgumentOutOfRange(nameof(suggestedType), "NSView pointer provided but a CAMetalLayer is required. Caller must assign a CAMetalLayer to the view before calling this method.");
        }
    }
}
#endif