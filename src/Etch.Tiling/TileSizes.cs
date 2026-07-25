namespace Etch.Tiling;

public readonly struct TTile8 : ITileSize
{
    public static int Width => 8;
    public static int Height => 8;
    public static int PixelCount => 64;
    public static int Log2Width => 3;
    public static int Log2Height => 3;
}

public readonly struct TTile16 : ITileSize
{
    public static int Width => 16;
    public static int Height => 16;
    public static int PixelCount => 256;
    public static int Log2Width => 4;
    public static int Log2Height => 4;
}

public readonly struct TTile32 : ITileSize
{
    public static int Width => 32;
    public static int Height => 32;
    public static int PixelCount => 1024;
    public static int Log2Width => 5;
    public static int Log2Height => 5;
}