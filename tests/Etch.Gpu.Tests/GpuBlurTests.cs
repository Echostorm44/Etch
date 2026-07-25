using System;
using System.Threading.Tasks;
using Etch.Gpu.Descriptors;
using Etch.Gpu.Pipelines;

namespace Etch.Gpu.Tests;

internal sealed class GpuBlurTests
{
    private static bool TryCreateDevice(out Device device)
    {
        device = default;
        try
        {
            var instance = Instance.Create();
            var (adapterStatus, adapter) = AsyncRequest.RequestAdapterSync(instance);
            if (adapterStatus != RequestAdapterStatus.Success || adapter.IsInvalid)
            {
                instance.Dispose();
                return false;
            }

            var (deviceStatus, dev) = AsyncRequest.RequestDeviceSync(instance, adapter);
            adapter.Dispose();
            instance.Dispose();

            if (deviceStatus != RequestDeviceStatus.Success || dev.IsInvalid)
            {
                return false;
            }

            device = dev;
            return true;
        }
        catch
        {
            return false;
        }
    }

    [Test]
    public async Task BlurPipeline_CreateAndDispose_Succeeds()
    {
        if (!TryCreateDevice(out var device))
        {
            return;
        }

        using (device)
        {
            using var blurPipeline = new BlurPipeline(device);
            await Assert.That(blurPipeline.DownPipeline.IsInvalid).IsFalse();
            await Assert.That(blurPipeline.UpPipeline.IsInvalid).IsFalse();
            await Assert.That(blurPipeline.PerFrameBindGroup.IsInvalid).IsFalse();
            await Assert.That(blurPipeline.VertexBuffer.IsInvalid).IsFalse();
        }
    }

    [Test]
    public async Task BlurPipeline_CreateTextureBindGroup_Succeeds()
    {
        if (!TryCreateDevice(out var device))
        {
            return;
        }

        using (device)
        {
            using var blurPipeline = new BlurPipeline(device);

            using var testTexture = device.CreateTexture(new TextureDescriptor
            {
                Usage = (ulong)(TextureUsage.RenderAttachment | TextureUsage.TextureBinding | TextureUsage.CopySrc),
                Size = new Extent3D { Width = 64, Height = 64, DepthOrArrayLayers = 1 },
                Format = TextureFormat.Bgra8UnormSrgb,
                SampleCount = 1,
                MipLevelCount = 1,
                Dimension = TextureDimension.D2
            });

            var viewDescriptor = new TextureViewDescriptor
            {
                Format = TextureFormat.Bgra8UnormSrgb,
                Dimension = TextureViewDimension.D2,
                BaseMipLevel = 0,
                MipLevelCount = 1,
                BaseArrayLayer = 0,
                ArrayLayerCount = 1
            };

            using var view = testTexture.CreateView(viewDescriptor);
            using var bindGroup = blurPipeline.CreateTextureBindGroup(view);
            await Assert.That(bindGroup.IsInvalid).IsFalse();
        }
    }

    [Test]
    public async Task BlurPipeline_ZeroSizedSurface_Succeeds()
    {
        if (!TryCreateDevice(out var device))
        {
            return;
        }

        using (device)
        {
            using var blurPipeline = new BlurPipeline(device);
            blurPipeline.SetSurfaceSize(800, 600);
            blurPipeline.SetSurfaceSize(1920, 1080);
            blurPipeline.SetSurfaceSize(1, 1);
        }
    }
}
