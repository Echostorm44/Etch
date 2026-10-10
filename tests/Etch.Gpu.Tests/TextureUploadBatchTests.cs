using System;
using Etch.Gpu.Descriptors;
using Etch.Gpu.Native;

namespace Etch.Gpu.Tests;

/// <summary>
/// <see cref="TextureUploadBatch"/>: many small writes stage in a few mapped buffers (bounded by
/// the bytes written, not by a placement per write), land at their texels and array layers when
/// flushed, and leave nothing held between flushes.
/// </summary>
[NotInParallel]
internal sealed class TextureUploadBatchTests
{
    private const int Size = 256;

    private static bool TryCreateDevice(out Device device)
    {
        device = default;
        var instance = Instance.Create();
        var (adapterStatus, adapter) = AsyncRequest.RequestAdapterSync(instance);
        if (adapterStatus != RequestAdapterStatus.Success || adapter.IsInvalid)
        {
            instance.Dispose();
            return false;
        }
        var (deviceStatus, created) = AsyncRequest.RequestDeviceSync(instance, adapter);
        adapter.Dispose();
        instance.Dispose();
        if (deviceStatus != RequestDeviceStatus.Success || created.IsInvalid)
        {
            return false;
        }
        device = created;
        return true;
    }

    private static Texture CreateLayers(Device device, TextureFormat format, uint layers) => device.CreateTexture(new TextureDescriptor
    {
        Size = new Extent3D { Width = Size, Height = Size, DepthOrArrayLayers = layers },
        Format = format,
        Usage = (ulong)(TextureUsage.TextureBinding | TextureUsage.CopyDst | TextureUsage.CopySrc),
        Dimension = TextureDimension.D2,
        MipLevelCount = 1,
        SampleCount = 1,
    });

    private static void Submit(Device device, CommandEncoder encoder)
    {
        using var commands = encoder.Finish();
        Span<CommandBuffer> submit = stackalloc CommandBuffer[1];
        submit[0] = commands;
        device.Queue.Submit(submit);
    }

    // Layer `layer` of the texture, tightly packed rows of Size × bytesPerPixel.
    private static byte[] ReadBack(Device device, Texture texture, uint layer, int bytesPerPixel)
    {
        uint stride = (uint)(Size * bytesPerPixel);
        using var buffer = device.CreateBuffer(new BufferDescriptor
        {
            Usage = (ulong)(BufferUsage.MapRead | BufferUsage.CopyDst),
            Size = stride * Size,
        });
        using (var encoder = device.CreateCommandEncoder())
        {
            encoder.CopyTextureToBuffer(texture, 0, new WGPUOrigin3D { X = 0, Y = 0, Z = layer }, buffer,
                new WGPUTexelCopyBufferLayout { Offset = 0, BytesPerRow = stride, RowsPerImage = Size },
                new Extent3D { Width = Size, Height = Size, DepthOrArrayLayers = 1 });
            Submit(device, encoder);
        }
        if (!buffer.MapSync(device, MapMode.Read))
        {
            throw new InvalidOperationException("Readback map failed");
        }
        var pixels = buffer.GetConstMappedRange(0, stride * Size).ToArray();
        buffer.Unmap();
        return pixels;
    }

    [Test]
    public async Task ManySmallWrites_StageInAFewBuffers_AndLandAtTheirTexels()
    {
        if (!TryCreateDevice(out var device))
        {
            return;
        }
        using (device)
        {
            using var texture = CreateLayers(device, TextureFormat.R8Unorm, 1);
            using var batch = new TextureUploadBatch(device);
            var glyph = new byte[12 * 16];
            for (int i = 0; i < 300; i++)
            {
                Array.Fill(glyph, (byte)(1 + i % 250));
                batch.Add((uint)(i % 20 * 12), (uint)(i / 20 * 16), 0, 12, 16, 1, glyph);
            }

            // 300 writes of 16 rows at a 256-byte stride: ~1.2 MB in buffers that double from 64 KB,
            // where one staging buffer per write took at least a 64 KB placement each (~19 MB).
            await Assert.That(batch.PendingCount).IsEqualTo(300);
            await Assert.That(batch.PendingBufferBytes).IsLessThanOrEqualTo(4UL * 1024 * 1024);

            using (var encoder = device.CreateCommandEncoder())
            {
                batch.Flush(encoder, texture);
                Submit(device, encoder);
            }
            await Assert.That(batch.PendingCount).IsEqualTo(0);
            await Assert.That(batch.PendingBufferBytes).IsEqualTo(0UL);

            var pixels = ReadBack(device, texture, 0, 1);
            for (int i = 0; i < 300; i++)
            {
                int x = i % 20 * 12;
                int y = i / 20 * 16;
                byte expected = (byte)(1 + i % 250);
                await Assert.That(pixels[y * Size + x]).IsEqualTo(expected);
                await Assert.That(pixels[(y + 15) * Size + x + 11]).IsEqualTo(expected);
            }
        }
    }

    [Test]
    public async Task RgbaWrites_LandInTheirArrayLayer()
    {
        if (!TryCreateDevice(out var device))
        {
            return;
        }
        using (device)
        {
            using var texture = CreateLayers(device, TextureFormat.Rgba8Unorm, 2);
            using var batch = new TextureUploadBatch(device);
            var block = new byte[5 * 3 * 4];
            for (int i = 0; i < block.Length; i += 4)
            {
                (block[i], block[i + 1], block[i + 2], block[i + 3]) = ((byte)10, (byte)20, (byte)30, (byte)40);
            }
            batch.Add(7, 9, 1, 5, 3, 4, block);
            batch.Flush(texture);

            var layer1 = ReadBack(device, texture, 1, 4);
            int corner = ((9 + 2) * Size + 7 + 4) * 4;
            await Assert.That((layer1[corner], layer1[corner + 1], layer1[corner + 2], layer1[corner + 3])).IsEqualTo(((byte)10, (byte)20, (byte)30, (byte)40));
            var layer0 = ReadBack(device, texture, 0, 4);
            await Assert.That(layer0[corner + 3]).IsEqualTo((byte)0);
        }
    }

    [Test]
    public async Task Clear_DropsQueuedWrites()
    {
        if (!TryCreateDevice(out var device))
        {
            return;
        }
        using (device)
        {
            using var texture = CreateLayers(device, TextureFormat.R8Unorm, 1);
            using var batch = new TextureUploadBatch(device);
            batch.Add(0, 0, 0, 4, 4, 1, new byte[] { 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9 });

            batch.Clear();
            batch.Flush(texture);

            await Assert.That(batch.PendingBufferBytes).IsEqualTo(0UL);
            await Assert.That(ReadBack(device, texture, 0, 1)[0]).IsEqualTo((byte)0);
        }
    }
}
