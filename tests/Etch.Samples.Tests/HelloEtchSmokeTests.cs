using System;
using System.IO;
using Etch.Gpu;
using Etch.Gpu.Descriptors;
using Etch.Gpu.Native;

namespace Etch.Samples.Tests;

/// <summary>
/// Headless smoke test for the HelloEtch red-triangle sample (SMP-001).
/// Renders one frame off-screen and asserts the center pixel is red.
/// </summary>
public class HelloEtchSmokeTests
{
    private const int Width = 640;
    private const int Height = 480;

    [Test]
    public async Task Headless_CenterPixel_IsRed()
    {
        var (instance, adapter, device) = CreateHeadlessDevice();
        if (device.IsInvalid)
        {
            // No GPU available — skip without failing.
            await Task.CompletedTask;
            return;
        }

        try
        {
            byte[] pixels = RenderOneFrame(device);

            int centerX = Width / 2;
            int centerY = Height / 2;
            uint bytesPerRow = (uint)(Width * 4);
            uint alignedBytesPerRow = (bytesPerRow + 255u) & ~255u;
            int offset = (int)(centerY * alignedBytesPerRow + centerX * 4);

            byte b = pixels[offset];
            byte g = pixels[offset + 1];
            byte r = pixels[offset + 2];
            byte a = pixels[offset + 3];

            await Assert.That(r).IsGreaterThan((byte)200);
            await Assert.That(g).IsLessThan((byte)50);
            await Assert.That(b).IsLessThan((byte)50);
            await Assert.That(a).IsEqualTo((byte)255);
        }
        finally
        {
            device.Dispose();
            adapter.Dispose();
            instance.Dispose();
        }
    }

    private static unsafe (Instance Instance, Adapter Adapter, Device Device) CreateHeadlessDevice()
    {
        Instance instance = Instance.Create();
        if (instance.IsInvalid)
            return (instance, default, default);

        var result = AsyncRequest.RequestAdapterSync(instance, backendType: BackendType.Undefined);
        if (result.Status != RequestAdapterStatus.Success || result.Adapter.IsInvalid)
        {
            instance.Dispose();
            return (instance, default, default);
        }

        var deviceResult = AsyncRequest.RequestDeviceSync(instance, result.Adapter);
        if (deviceResult.Status != RequestDeviceStatus.Success || deviceResult.Device.IsInvalid)
        {
            result.Adapter.Dispose();
            instance.Dispose();
            return (instance, result.Adapter, default);
        }

        return (instance, result.Adapter, deviceResult.Device);
    }

    private static unsafe byte[] RenderOneFrame(Device device)
    {
        // Off-screen texture
        var texture = device.CreateTexture(new TextureDescriptor
        {
            NextInChain = IntPtr.Zero,
            Size = new Extent3D { Width = (uint)Width, Height = (uint)Height, DepthOrArrayLayers = 1 },
            Format = TextureFormat.Bgra8Unorm,
            Usage = (ulong)(TextureUsage.RenderAttachment | TextureUsage.CopySrc),
            MipLevelCount = 1,
            SampleCount = 1,
        });

        var textureView = texture.CreateView();

        // Render
        using var renderer = new HelloEtch.HelloEtchRenderer(device, TextureFormat.Bgra8Unorm);
        renderer.Render(textureView);

        // Readback buffer
        uint bytesPerRow = (uint)(Width * 4);
        uint alignedBytesPerRow = (bytesPerRow + 255u) & ~255u;
        ulong bufferSize = alignedBytesPerRow * (uint)Height;

        var readbackBuffer = device.CreateBuffer(new BufferDescriptor
        {
            Usage = (ulong)(BufferUsage.CopyDst | BufferUsage.MapRead),
            Size = bufferSize,
        });

        // Copy texture → buffer
        using var encoder = device.CreateCommandEncoder();
        encoder.CopyTextureToBuffer(
            texture, 0, new WGPUOrigin3D(),
            readbackBuffer,
            new WGPUTexelCopyBufferLayout
            {
                Offset = 0,
                BytesPerRow = alignedBytesPerRow,
                RowsPerImage = (uint)Height,
            },
            new Extent3D { Width = (uint)Width, Height = (uint)Height, DepthOrArrayLayers = 1 });

        using var commandBuffer = encoder.Finish();
        Span<CommandBuffer> submitList = stackalloc CommandBuffer[1];
        submitList[0] = commandBuffer;
        device.Queue.Submit(submitList);

        // Map and read
        if (!readbackBuffer.MapSync(device, MapMode.Read, 0, bufferSize))
        {
            Etch.Panic.Invariant(Etch.PanicCodes.GpuBufferMapTimeout,
                "Headless smoke test failed: buffer map timeout.");
        }

        var mapped = readbackBuffer.GetConstMappedRange(0, bufferSize);
        var output = new byte[Width * Height * 4];

        for (int y = 0; y < Height; y++)
        {
            var srcRow = mapped.Slice((int)(y * alignedBytesPerRow), Width * 4);
            var dstRow = output.AsSpan(y * Width * 4, Width * 4);
            srcRow.Slice(0, Width * 4).CopyTo(dstRow);
        }

        readbackBuffer.Unmap();

        // Cleanup
        readbackBuffer.Dispose();
        textureView.Dispose();
        texture.Dispose();

        return output;
    }
}
