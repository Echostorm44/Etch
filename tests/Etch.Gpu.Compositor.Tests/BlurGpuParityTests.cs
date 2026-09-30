using System;
using System.Runtime.InteropServices;
using Etch.Gpu.Compositor.Pipelines;
using Etch.Gpu.Descriptors;
using Etch.Gpu.Native;
using Etch.Gpu.Pipelines;
using Etch.Raster.Cpu;
using Etch.Raster.Cpu.Blur;
using TUnit;

namespace Etch.Gpu.Compositor.Tests;

// The GPU blur and the CPU reference implement the same kernels with texel-exact addressing, so
// they must agree up to half-float rounding. Before this pairing existed the GPU passes bound no
// inputs, the shaders' weights did not sum to one, and nothing compared the two.
internal sealed class BlurGpuParityTests
{
    private const int Width = 64;
    private const int Height = 48;

    [Test]
    [Arguments(1f)]
    [Arguments(7f)]
    [Arguments(63f)]
    public async Task GpuBlur_MatchesCpuReference(float radius)
    {
        using var instance = Instance.Create();
        var (adapterStatus, adapter) = AsyncRequest.RequestAdapterSync(instance);
        if (adapterStatus != RequestAdapterStatus.Success || adapter.IsInvalid)
        {
            return; // no GPU on this machine
        }
        using var device = AsyncRequest.RequestDeviceSync(instance, adapter).Device;
        adapter.Dispose();

        Rgba16f[] input = Pattern();
        Rgba16f[] gpu = BlurOnGpu(device, input, radius);

        var cpu = new Rgba16f[Width * Height];
        BjorgeBlur.Blur(
            new Framebuffer(Width, Height, Width, input),
            new Framebuffer(Width, Height, Width, cpu),
            radius);

        float maxDiff = 0f;
        for (int i = 0; i < cpu.Length; i++)
        {
            maxDiff = MathF.Max(maxDiff, MathF.Abs((float)cpu[i].R - (float)gpu[i].R));
            maxDiff = MathF.Max(maxDiff, MathF.Abs((float)cpu[i].G - (float)gpu[i].G));
            maxDiff = MathF.Max(maxDiff, MathF.Abs((float)cpu[i].B - (float)gpu[i].B));
            maxDiff = MathF.Max(maxDiff, MathF.Abs((float)cpu[i].A - (float)gpu[i].A));
        }

        // Half floats carry ~3 decimal digits; each pass rounds once on both sides.
        await Assert.That(maxDiff).IsLessThan(0.004f);
    }

    // Hard edges and a gradient, so a wrong weight, offset or flip shows up.
    private static Rgba16f[] Pattern()
    {
        var pixels = new Rgba16f[Width * Height];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                bool check = ((x / 8) + (y / 8)) % 2 == 0;
                pixels[y * Width + x] = Rgba16f.From(check ? 1f : 0f, x / (float)(Width - 1), y / (float)(Height - 1), 1f);
            }
        }
        return pixels;
    }

    private static Rgba16f[] BlurOnGpu(Device device, Rgba16f[] input, float radius)
    {
        const uint bytesPerRow = Width * 8; // Rgba16Float; 512 is already a multiple of 256
        var size = new Extent3D { Width = Width, Height = Height, DepthOrArrayLayers = 1 };

        using var source = device.CreateTexture(new TextureDescriptor
        {
            Usage = (ulong)(TextureUsage.TextureBinding | TextureUsage.CopyDst),
            Size = size,
            Format = TextureFormat.Rgba16Float,
            SampleCount = 1,
            MipLevelCount = 1,
            Dimension = TextureDimension.D2
        });
        device.Queue.WriteTexture(source, 0, default, MemoryMarshal.AsBytes(input.AsSpan()), bytesPerRow, Height, size);

        using var destination = device.CreateTexture(new TextureDescriptor
        {
            Usage = (ulong)(TextureUsage.RenderAttachment | TextureUsage.CopySrc),
            Size = size,
            Format = TextureFormat.Rgba16Float,
            SampleCount = 1,
            MipLevelCount = 1,
            Dimension = TextureDimension.D2
        });

        using var readback = device.CreateBuffer(new BufferDescriptor
        {
            Usage = (ulong)(BufferUsage.MapRead | BufferUsage.CopyDst),
            Size = bytesPerRow * Height
        });

        using var blur = new BlurGpuPipeline(device, TextureFormat.Rgba16Float);
        using var encoder = device.CreateCommandEncoder();
        blur.Record(encoder, source, Width, Height, radius, destination);
        encoder.CopyTextureToBuffer(destination, 0, default, readback,
            new WGPUTexelCopyBufferLayout { Offset = 0, BytesPerRow = bytesPerRow, RowsPerImage = Height }, size);
        using var commands = encoder.Finish();
        device.Queue.Submit(new ReadOnlySpan<CommandBuffer>(in commands));

        if (!readback.MapSync(device, MapMode.Read, 0, bytesPerRow * Height))
        {
            throw new InvalidOperationException("GPU readback failed");
        }
        var result = MemoryMarshal.Cast<byte, Rgba16f>(readback.GetConstMappedRange(0, bytesPerRow * Height)).ToArray();
        readback.Unmap();
        return result;
    }
}
