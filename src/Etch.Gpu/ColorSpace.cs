using Etch.Gpu.Descriptors;

namespace Etch.Gpu;

public enum ColorSpace : byte
{
    Srgb = 0,
    DisplayP3 = 1,
    ScRgb = 2,
}

public static class ColorSpaceFormat
{
    public static TextureFormat GetFormat(ColorSpace space)
    {
        return space switch
        {
            ColorSpace.Srgb => TextureFormat.Bgra8UnormSrgb,
            ColorSpace.DisplayP3 => TextureFormat.Bgra8UnormSrgb,
            ColorSpace.ScRgb => TextureFormat.Rgba16Float,
            _ => TextureFormat.Bgra8UnormSrgb,
        };
    }

    public static int BytesPerPixel(ColorSpace space)
    {
        return space switch
        {
            ColorSpace.ScRgb => 8,
            _ => 4,
        };
    }
}
