using System;
using System.IO;

namespace Etch.Effects.Image;

/// <summary>
/// A decoded image with raw pixel data in a known format. Created via
/// <see cref="Decode(ReadOnlySpan{byte}, ImageDecodeOptions?)"/> or
/// <see cref="DecodeStreaming(Stream, ImageDecodeOptions?)"/>.
/// </summary>
public sealed class ImageSource : IDisposable
{
    private byte[]? _pixelData;
    private readonly int _width;
    private readonly int _height;
    private readonly ImageFormat _format;
    private bool _disposed;

    private ImageSource(int width, int height, ImageFormat format, byte[] pixelData)
    {
        _width = width;
        _height = height;
        _format = format;
        _pixelData = pixelData;
    }

    /// <summary>Width in pixels.</summary>
    public int Width => _width;

    /// <summary>Height in pixels.</summary>
    public int Height => _height;

    /// <summary>Pixel format (e.g. RGBA8, BGRA8).</summary>
    public ImageFormat Format => _format;

    /// <summary>Decodes an image from a byte buffer (PNG, JPEG, etc.).</summary>
    public static ImageSource Decode(ReadOnlySpan<byte> encoded, ImageDecodeOptions? opts = null)
    {
        if (encoded.Length == 0)
        {
            Panic.ArgumentNull(nameof(encoded));
        }

        opts ??= new ImageDecodeOptions();

        var format = SharpImage.Formats.FormatRegistry.DetectFormat(encoded);
        ImageSource result;

        switch (format)
        {
            case SharpImage.Formats.ImageFileFormat.Png:
                result = DecodeWithPng(encoded, opts);
                break;
            case SharpImage.Formats.ImageFileFormat.Jpeg:
                result = DecodeWithJpeg(encoded, opts);
                break;
            case SharpImage.Formats.ImageFileFormat.Hdr:
                result = DecodeWithHdr(encoded, opts);
                break;
            case SharpImage.Formats.ImageFileFormat.Exr:
                result = DecodeWithExr(encoded, opts);
                break;
            case SharpImage.Formats.ImageFileFormat.Tiff:
                result = DecodeWithTiff(encoded, opts);
                break;
            default:
                result = DecodeWithPng(encoded, opts);
                break;
        }

        if (!ValidateDimensions(result._width, result._height))
        {
            Panic.Invariant(PanicCodes.ImageDimensionsExceedLimit, $"Image dimensions {result._width}x{result._height} exceed device limit.");
        }

        return result;
    }

    public static ImageSource DecodeStreaming(Stream stream, ImageDecodeOptions? opts = null)
    {
        if (stream == null)
        {
            Panic.ArgumentNull(nameof(stream));
        }

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return Decode(ms.ToArray(), opts);
    }

    public ReadOnlySpan<byte> GetPixelSpan()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_pixelData == null)
        {
            Panic.Invariant(PanicCodes.InvalidState, "ImageSource pixel data has already been consumed.");
        }
        return _pixelData!;
    }

    public void CopyTo(Span<byte> dest)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ReadOnlySpan<byte> src = GetPixelSpan();
        if (dest.Length < src.Length)
        {
            Panic.Invariant(PanicCodes.BufferOverflow, $"Destination span too small. Need {src.Length} bytes, got {dest.Length}.");
        }
        src.CopyTo(dest);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _pixelData = null;
        }
    }

    private static bool ValidateDimensions(int width, int height)
    {
        const uint MaxTextureDimension2D = 8192u;
        return (uint)width <= MaxTextureDimension2D && (uint)height <= MaxTextureDimension2D;
    }

    private static ImageSource DecodeWithPng(ReadOnlySpan<byte> encoded, ImageDecodeOptions opts)
    {
        try
        {
            using var ms = new MemoryStream(encoded.ToArray());
            var frame = SharpImage.Formats.PngCoder.Read(ms);
            return CreateFromFrame(frame, opts, ImageFormat.Rgba8Unorm);
        }
        catch (Exception ex)
        {
            Panic.Invariant(PanicCodes.ImageDecodeFailed, $"PNG decode failed: {ex.Message}");
            throw;
        }
    }

    private static ImageSource DecodeWithJpeg(ReadOnlySpan<byte> encoded, ImageDecodeOptions opts)
    {
        try
        {
            using var ms = new MemoryStream(encoded.ToArray());
            var frame = SharpImage.Formats.JpegCoder.Read(ms);
            return CreateFromFrame(frame, opts, ImageFormat.Rgba8Unorm);
        }
        catch (Exception ex)
        {
            Panic.Invariant(PanicCodes.ImageDecodeFailed, $"JPEG decode failed: {ex.Message}");
            throw;
        }
    }

    private static ImageSource DecodeWithHdr(ReadOnlySpan<byte> encoded, ImageDecodeOptions opts)
    {
        try
        {
            var frame = SharpImage.Formats.HdrCoder.Decode(encoded);
            return CreateFromFrame(frame, opts, ImageFormat.Rgba16f);
        }
        catch (Exception ex)
        {
            Panic.Invariant(PanicCodes.ImageDecodeFailed, $"HDR decode failed: {ex.Message}");
            throw;
        }
    }

    private static ImageSource DecodeWithExr(ReadOnlySpan<byte> encoded, ImageDecodeOptions opts)
    {
        try
        {
            var frame = SharpImage.Formats.ExrCoder.Decode(encoded.ToArray());
            return CreateFromFrame(frame, opts, ImageFormat.Rgba16f);
        }
        catch (Exception ex)
        {
            Panic.Invariant(PanicCodes.ImageDecodeFailed, $"EXR decode failed: {ex.Message}");
            throw;
        }
    }

    private static ImageSource DecodeWithTiff(ReadOnlySpan<byte> encoded, ImageDecodeOptions opts)
    {
        try
        {
            using var ms = new MemoryStream(encoded.ToArray());
            var frame = SharpImage.Formats.TiffCoder.Read(ms);
            return CreateFromFrame(frame, opts, ImageFormat.Rgba8Unorm);
        }
        catch (Exception ex)
        {
            Panic.Invariant(PanicCodes.ImageDecodeFailed, $"TIFF decode failed: {ex.Message}");
            throw;
        }
    }

    private static ImageSource CreateFromFrame(SharpImage.Image.ImageFrame frame, ImageDecodeOptions opts, ImageFormat targetFormat)
    {
        int width = (int)frame.Columns;
        int height = (int)frame.Rows;
        int channelCount = (int)frame.NumberOfChannels;

        byte[] pixels;
        if (targetFormat == ImageFormat.Rgba16f)
        {
            pixels = ConvertFrameToRgba16f(frame, width, height, channelCount);
        }
        else
        {
            pixels = ConvertFrameToRgba8(frame, width, height, channelCount, opts);
        }

        return new ImageSource(width, height, targetFormat, pixels);
    }

    private static byte[] ConvertFrameToRgba8(SharpImage.Image.ImageFrame frame, int width, int height, int channelCount, ImageDecodeOptions opts)
    {
        byte[] result = new byte[width * height * 4];

        for (int row = 0; row < height; row++)
        {
            ReadOnlySpan<ushort> pixelRow = frame.GetPixelRow(row);
            int dstRowOffset = row * width * 4;

            for (int col = 0; col < width; col++)
            {
                int srcIdx = col * channelCount;
                int dstIdx = dstRowOffset + col * 4;

                float rNorm = pixelRow[srcIdx + 0] / 65535f;
                float gNorm = pixelRow[srcIdx + 1] / 65535f;
                float bNorm = pixelRow[srcIdx + 2] / 65535f;
                float aNorm = channelCount == 4 ? pixelRow[srcIdx + 3] / 65535f : 1.0f;

                if (opts.SrgbToLinear)
                {
                    rNorm = SrgbToLinear(rNorm);
                    gNorm = SrgbToLinear(gNorm);
                    bNorm = SrgbToLinear(bNorm);
                }

                if (opts.PremultiplyAlpha && channelCount == 4)
                {
                    rNorm *= aNorm;
                    gNorm *= aNorm;
                    bNorm *= aNorm;
                }

                result[dstIdx + 0] = (byte)(rNorm * 255f + 0.5f);
                result[dstIdx + 1] = (byte)(gNorm * 255f + 0.5f);
                result[dstIdx + 2] = (byte)(bNorm * 255f + 0.5f);
                result[dstIdx + 3] = (byte)(aNorm * 255f + 0.5f);
            }
        }

        return result;
    }

    private static byte[] ConvertFrameToRgba16f(SharpImage.Image.ImageFrame frame, int width, int height, int channelCount)
    {
        byte[] result = new byte[width * height * 8];

        for (int row = 0; row < height; row++)
        {
            ReadOnlySpan<ushort> pixelRow = frame.GetPixelRow(row);
            int dstRowOffset = row * width * 8;

            for (int col = 0; col < width; col++)
            {
                int srcIdx = col * channelCount;
                int dstIdx = dstRowOffset + col * 8;

                float rNorm = pixelRow[srcIdx + 0] / 65535f;
                float gNorm = pixelRow[srcIdx + 1] / 65535f;
                float bNorm = pixelRow[srcIdx + 2] / 65535f;
                float aNorm = channelCount == 4 ? pixelRow[srcIdx + 3] / 65535f : 1.0f;

                ushort rh = FloatToHalf(rNorm);
                ushort gh = FloatToHalf(gNorm);
                ushort bh = FloatToHalf(bNorm);
                ushort ah = FloatToHalf(aNorm);

                result[dstIdx + 0] = (byte)(rh & 0xFF);
                result[dstIdx + 1] = (byte)((rh >> 8) & 0xFF);
                result[dstIdx + 2] = (byte)(gh & 0xFF);
                result[dstIdx + 3] = (byte)((gh >> 8) & 0xFF);
                result[dstIdx + 4] = (byte)(bh & 0xFF);
                result[dstIdx + 5] = (byte)((bh >> 8) & 0xFF);
                result[dstIdx + 6] = (byte)(ah & 0xFF);
                result[dstIdx + 7] = (byte)((ah >> 8) & 0xFF);
            }
        }

        return result;
    }

    private static float SrgbToLinear(float srgb)
    {
        return srgb <= 0.04045f ? srgb / 12.92f : (float)Math.Pow((srgb + 0.055f) / 1.055f, 2.4f);
    }

    private static ushort FloatToHalf(float value)
    {
        uint bits = BitConverter.SingleToUInt32Bits(value);

        uint sign = (bits >> 16) & 0x8000u;
        int exponent = (int)((bits >> 23) & 0xFFu);
        uint fraction = bits & 0x7FFFFFu;

        if (exponent == 0)
        {
            return 0;
        }
        else if (exponent == 0xFF)
        {
            return (ushort)(sign | 0x7C00u | (fraction != 0 ? 0x200u : 0u));
        }
        else
        {
            int newExponent = exponent - 127 + 15;
            if (newExponent >= 30)
            {
                newExponent = 30;
            }
            else if (newExponent <= 0)
            {
                return (ushort)sign;
            }

            return (ushort)(sign | ((uint)(newExponent) << 10) | (fraction >> 13));
        }
    }
}
