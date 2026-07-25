namespace Etch.Tiling;

[Etch.Abstractions.EtchExtensionPoint]
public interface ITileSize
{
    static abstract int Width { get; }
    static abstract int Height { get; }
    static abstract int PixelCount { get; }
    static abstract int Log2Width { get; }
    static abstract int Log2Height { get; }
}