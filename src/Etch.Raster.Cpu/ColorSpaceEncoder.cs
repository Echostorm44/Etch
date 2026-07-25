using System;
using System.Runtime.CompilerServices;
using Etch.Gpu;

namespace Etch.Raster.Cpu;

public static class ColorSpaceEncoder
{
    public static void Encode(
        ReadOnlySpan<Rgba16f> linearF16,
        Span<byte> output,
        ColorSpace space,
        int width,
        int height)
    {
        int pixelCount = width * height;
        int outputBytes = pixelCount * ColorSpaceFormat.BytesPerPixel(space);

        if (output.Length < outputBytes)
            Etch.Panic.Invariant(Etch.PanicCodes.BufferOverflow, "Output buffer too small for color space encoding");

        if (space == ColorSpace.ScRgb)
        {
            EncodeScRgb(linearF16, output, pixelCount);
        }
        else
        {
            EncodeSrgb(linearF16, output, pixelCount);
        }
    }

    private static void EncodeSrgb(ReadOnlySpan<Rgba16f> linearF16, Span<byte> output, int pixelCount)
    {
        for (int i = 0; i < pixelCount; i++)
        {
            var color = linearF16[i];
            int outIdx = i * 4;
            output[outIdx] = Srgb.EncodeChannelScalar((float)color.R);
            output[outIdx + 1] = Srgb.EncodeChannelScalar((float)color.G);
            output[outIdx + 2] = Srgb.EncodeChannelScalar((float)color.B);
            output[outIdx + 3] = (byte)(Math.Clamp((float)color.A, 0f, 1f) * 255f + 0.5f);
        }
    }

    private static void EncodeScRgb(ReadOnlySpan<Rgba16f> linearF16, Span<byte> output, int pixelCount)
    {
        for (int i = 0; i < pixelCount; i++)
        {
            var color = linearF16[i];
            int outIdx = i * 8;
            WriteHalf(output.Slice(outIdx, 2), color.R);
            WriteHalf(output.Slice(outIdx + 2, 2), color.G);
            WriteHalf(output.Slice(outIdx + 4, 2), color.B);
            WriteHalf(output.Slice(outIdx + 6, 2), color.A);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteHalf(Span<byte> dst, Half value)
    {
        BitConverter.TryWriteBytes(dst, value);
    }
}
