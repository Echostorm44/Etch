namespace Etch.Raster.Cpu.Blur;

public static class BlurTaps
{
    public const float DownCenterWeight = 4f / 17f;
    public const float DownCornerWeight = 1f / 17f;

    public const float UpCenterWeight = 4f / 17f;
    public const float UpEdgeWeight = 2f / 17f;
    public const float UpCornerWeight = 1f / 17f;
}
