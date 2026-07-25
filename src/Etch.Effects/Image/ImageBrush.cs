namespace Etch.Effects.Image;

public enum ImageFilter : uint
{
    Nearest = 0u,
    Bilinear = 1u,
    Bicubic = 2u,
    Trilinear = 3u,
}

public enum ImageExtend : uint
{
    Clamp = 0u,
    Repeat = 1u,
    Mirror = 2u,
    Pad = 3u,
}

public readonly struct ImageBrush
{
    public readonly ImageSource Source;
    public readonly ImageFilter Filter;
    public readonly ImageExtend ExtendX;
    public readonly ImageExtend ExtendY;

    public ImageBrush(ImageSource source, ImageFilter filter, ImageExtend extendX, ImageExtend extendY)
    {
        Source = source;
        Filter = filter;
        ExtendX = extendX;
        ExtendY = extendY;
    }

    public static ImageBrush CreateBilinear(ImageSource source)
    {
        return new ImageBrush(source, ImageFilter.Bilinear, ImageExtend.Clamp, ImageExtend.Clamp);
    }

    public static ImageBrush CreateNearest(ImageSource source)
    {
        return new ImageBrush(source, ImageFilter.Nearest, ImageExtend.Clamp, ImageExtend.Clamp);
    }

    public static ImageBrush CreateBilinearRepeat(ImageSource source)
    {
        return new ImageBrush(source, ImageFilter.Bilinear, ImageExtend.Repeat, ImageExtend.Repeat);
    }
}
