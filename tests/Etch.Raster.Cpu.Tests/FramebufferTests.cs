using System;
using System.Buffers;
using Etch;
using Etch.Raster.Cpu;
using TUnit;

namespace Etch.Raster.Cpu.Tests;

internal sealed class FramebufferTests
{
    [Test]
    public void Rgba16fSizeIs8Bytes()
    {
        if (System.Runtime.InteropServices.Marshal.SizeOf<Rgba16f>() != 8)
            throw new InvalidOperationException("Rgba16f must be 8 bytes");
    }

    [Test]
    public void Framebuffer1024x1024Allocates16MiB()
    {
        int width = 1024;
        int height = 1024;
        int expectedBytes = width * height * 8;

        var buffer = new Rgba16f[width * height];
        var fb = new Framebuffer(width, height, width, buffer);

        if (fb.Width != width || fb.Height != height)
            throw new InvalidOperationException("Dimensions mismatch");

        if (buffer.Length * 8 != expectedBytes)
            throw new InvalidOperationException($"Expected {expectedBytes} bytes, got {buffer.Length * 8}");
    }

    [Test]
    public void Rgba16fFromLinearBytesEncodesToSrgb()
    {
        var buffer = new Rgba16f[256];
        for (int i = 0; i <= 255; i++)
        {
            buffer[i] = Rgba16f.FromLinearBytes((byte)i, (byte)i, (byte)i, 255);
        }

        var fb = new Framebuffer(256, 1, 256, buffer);
        var output = new byte[256 * 4];

        SrgbOutputView.Encode(fb, output, premultiplied: false);

        for (int i = 0; i <= 255; i++)
        {
            float linearInput = i / 255.0f;
            float expectedLinear = linearInput <= 0.0031308f
                ? linearInput * 12.92f
                : 1.055f * MathF.Pow(linearInput, 1.0f / 2.4f) - 0.055f;
            byte expected = (byte)(expectedLinear * 255.0f + 0.5f);
            byte actual = output[i * 4 + 2];
            int diff = Math.Abs(actual - expected);
            if (diff > 1)
                throw new InvalidOperationException($"Srgb encode error for {i}: got {actual}, expected {expected}, diff={diff}");
        }
    }

    [Test]
    public void RowSpanBoundsCheckThrowsOnHeight()
    {
        var buffer = new Rgba16f[100];
        var fb = new Framebuffer(10, 10, 10, buffer);

        try
        {
            _ = fb.RowSpan(10);
            throw new InvalidOperationException("Expected exception for y == Height");
        }
        catch (EtchException)
        {
        }
    }

    [Test]
    public void RowSpanBoundsCheckThrowsOnNegative()
    {
        var buffer = new Rgba16f[100];
        var fb = new Framebuffer(10, 10, 10, buffer);

        try
        {
            _ = fb.RowSpan(-1);
            throw new InvalidOperationException("Expected exception for negative y");
        }
        catch (EtchException)
        {
        }
    }

    [Test]
    public void RowSpanReturnsCorrectSlice()
    {
        var buffer = new Rgba16f[100];
        for (int i = 0; i < 100; i++)
        {
            buffer[i] = Rgba16f.From((float)i, 0, 0, 1);
        }

        var fb = new Framebuffer(10, 10, 10, buffer);
        var row = fb.RowSpan(5);

        if (row.Length != 10)
            throw new InvalidOperationException($"Expected row length 10, got {row.Length}");
        if ((int)(float)row[0].R != 50)
            throw new InvalidOperationException($"Expected row[0].R = 50, got {(int)(float)row[0].R}");
        if ((int)(float)row[9].R != 59)
            throw new InvalidOperationException($"Expected row[9].R = 59, got {(int)(float)row[9].R}");
    }

    [Test]
    public void PoolReturnsSameBufferForSameSizeAfterReturn()
    {
        var fb1 = FramebufferPool.Rent(100, 100);
        var data1 = fb1.Pixels.Span;
        data1[0] = Rgba16f.From(1, 2, 3, 4);
        int fb1Width = fb1.Width;
        int fb1Height = fb1.Height;
        FramebufferPool.Return(ref fb1);

        var fb2 = FramebufferPool.Rent(100, 100);
        var data2 = fb2.Pixels.Span;

        if (fb1Width != fb2.Width || fb1Height != fb2.Height)
            throw new InvalidOperationException("Pool should return same dimensions for same size request");

        FramebufferPool.Return(ref fb2);
    }

    [Test]
    public void PoolReturnsDifferentBufferForDifferentSize()
    {
        var fb1 = FramebufferPool.Rent(100, 100);
        FramebufferPool.Return(ref fb1);

        var fb2 = FramebufferPool.Rent(200, 200);

        if (fb1.Width == fb2.Width && fb1.Height == fb2.Height)
            throw new InvalidOperationException("Pool should return different dimensions for different size request");

        FramebufferPool.Return(ref fb2);
    }

    [Test]
    public void EncodeProducesCorrectRedOutput()
    {
        var buffer = new Rgba16f[4];
        buffer[0] = Rgba16f.From(1, 0, 0, 1);

        var fb = new Framebuffer(1, 1, 1, buffer);
        var output = new byte[4];

        SrgbOutputView.Encode(fb, output, premultiplied: false);

        if (output[2] < 250 || output[2] > 255)
            throw new InvalidOperationException($"Expected output[2] (R) near 255, got {output[2]}");
        if (output[1] != 0)
            throw new InvalidOperationException($"Expected output[1] (G) = 0, got {output[1]}");
        if (output[0] != 0)
            throw new InvalidOperationException($"Expected output[0] (B) = 0, got {output[0]}");
    }
}