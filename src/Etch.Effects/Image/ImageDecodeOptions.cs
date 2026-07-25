namespace Etch.Effects.Image;

public sealed class ImageDecodeOptions
{
    public bool PremultiplyAlpha { get; init; } = true;
    public bool SrgbToLinear { get; init; } = true;
    public ImageFormat? ForceFormat { get; init; }
    public bool GenerateMipmaps { get; init; }
    public int MipmapLevels { get; init; }
}
