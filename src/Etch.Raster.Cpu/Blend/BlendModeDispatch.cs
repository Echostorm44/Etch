using System;
using System.Runtime.CompilerServices;
using Etch.ClipBlendGradient;

namespace Etch.Raster.Cpu;

public static class BlendModeDispatch
{
    private delegate void BlendFunc(ReadOnlySpan<byte> coverage, Rgba16f paint, Span<Rgba16f> row);

    private static readonly BlendFunc[] DispatchTable = new BlendFunc[16]
    {
        NormalBlender.Blend,
        MultiplyBlender.Blend,
        ScreenBlender.Blend,
        OverlayBlender.Blend,
        DarkenBlender.Blend,
        LightenBlender.Blend,
        ColorDodgeBlender.Blend,
        ColorBurnBlender.Blend,
        HardLightBlender.Blend,
        SoftLightBlender.Blend,
        DifferenceBlender.Blend,
        ExclusionBlender.Blend,
        HueBlender.Blend,
        SaturationBlender.Blend,
        ColorBlender.Blend,
        LuminosityBlender.Blend,
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Blend(BlendMode mode, ReadOnlySpan<byte> coverage, Rgba16f paint, Span<Rgba16f> row)
    {
        DispatchTable[(int)mode](coverage, paint, row);
    }
}
