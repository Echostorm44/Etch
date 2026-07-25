namespace Etch.Gpu.SwapChains;

// Strongly-typed swap-chain configuration. Maps 1:1 into WGPUSurfaceConfiguration
// when applied. All enum fields are the v29 canonical ones from Etch.Gpu.

public struct SwapChainConfig
{
    public TextureFormat Format { get; init; }
    public uint Width { get; init; }
    public uint Height { get; init; }
    public PresentMode PresentMode { get; init; }
    public CompositeAlphaMode AlphaMode { get; init; }
    public TextureUsage Usage { get; init; }
    public ColorSpace ColorSpace { get; init; }

    public static SwapChainConfig Default => new()
    {
        Format = TextureFormat.Bgra8Unorm,
        Width = 1,
        Height = 1,
        PresentMode = PresentMode.Fifo,
        AlphaMode = CompositeAlphaMode.Auto,
        Usage = TextureUsage.RenderAttachment,
        ColorSpace = ColorSpace.Srgb,
    };
}
