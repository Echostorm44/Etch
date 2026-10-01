using System;
using System.Threading;
using Etch.Geometry;
using Etch.Gpu.Compositor.Pipelines;
using Etch.Gpu.Descriptors;
using Etch.Scene;
using TUnit;

namespace Etch.Gpu.Compositor.Tests;

// WARP shares one D3D12 device between every wgpu device in the process, and its shader JIT is not
// safe across their queues: two queues executing at once (or one destroying a pipeline meanwhile)
// crash with an access violation inside d3d10warp. That killed this suite on the windows CI runner
// (exit -1073741819, ~3 s in). Etch.Gpu serializes those calls once a WARP device exists
// (WarpSerialization); without it this test crashes the process instead of failing. The race is
// timing-dependent, so a single run does not always catch a regression; it guards across runs.
internal sealed class WarpParallelDevicesTests
{
    private const int Threads = 16;
    private const int RendersPerThread = 8;
    private const int Size = 128;

    [Test]
    public async Task ParallelWarpDevices_RenderWithoutCrashing()
    {
        if (!OperatingSystem.IsWindows() || !WarpIsAvailable())
        {
            Skip.Test("No WARP (software D3D12) adapter on this machine.");
            return;
        }

        using SceneBuffer scene = BuildScene();
        int failures = 0;
        var workers = new Thread[Threads];
        for (int i = 0; i < Threads; i++)
        {
            workers[i] = new Thread(() =>
            {
                for (int k = 0; k < RendersPerThread; k++)
                {
                    if (!RenderOnOwnWarpDevice(scene))
                    {
                        Interlocked.Increment(ref failures);
                    }
                }
            });
            workers[i].Start();
        }
        foreach (Thread worker in workers)
        {
            worker.Join();
        }

        await Assert.That(failures).IsEqualTo(0);
    }

    private static bool WarpIsAvailable()
    {
        using var instance = Instance.Create();
        var (status, adapter) = AsyncRequest.RequestAdapterSync(instance, forceFallbackAdapter: true);
        if (status != RequestAdapterStatus.Success || adapter.IsInvalid)
        {
            return false;
        }
        AdapterDescription description = adapter.GetDescription();
        adapter.Dispose();
        return description.IsSoftware && description.Backend == BackendType.D3D12;
    }

    // A fresh instance and device per render, like independent tests or windows each owning one. Two
    // different pipelines (strip coverage, then blur) so the queues JIT different shaders.
    private static bool RenderOnOwnWarpDevice(SceneBuffer scene)
    {
        using var instance = Instance.Create();
        var (adapterStatus, adapter) = AsyncRequest.RequestAdapterSync(instance, forceFallbackAdapter: true);
        if (adapterStatus != RequestAdapterStatus.Success || adapter.IsInvalid)
        {
            return false;
        }
        var (deviceStatus, device) = AsyncRequest.RequestDeviceSync(instance, adapter);
        adapter.Dispose();
        if (deviceStatus != RequestDeviceStatus.Success || device.IsInvalid)
        {
            return false;
        }

        using (device)
        {
            byte[] pixels;
            using (var compositor = new GpuCompositor(device))
            {
                pixels = compositor.RenderToRgba8(scene, Size, Size);
            }
            Blur(device);
            int center = (Size / 2 * Size + Size / 2) * 4;
            return pixels[center] == 0xFF && pixels[center + 3] == 0xFF;
        }
    }

    private static void Blur(Device device)
    {
        var size = new Extent3D { Width = Size, Height = Size, DepthOrArrayLayers = 1 };
        using var source = device.CreateTexture(new TextureDescriptor
        {
            Usage = (ulong)(TextureUsage.TextureBinding | TextureUsage.CopyDst),
            Size = size,
            Format = TextureFormat.Rgba16Float,
            SampleCount = 1,
            MipLevelCount = 1,
            Dimension = TextureDimension.D2
        });
        using var destination = device.CreateTexture(new TextureDescriptor
        {
            Usage = (ulong)TextureUsage.RenderAttachment,
            Size = size,
            Format = TextureFormat.Rgba16Float,
            SampleCount = 1,
            MipLevelCount = 1,
            Dimension = TextureDimension.D2
        });
        using var blur = new BlurGpuPipeline(device, TextureFormat.Rgba16Float);
        using var encoder = device.CreateCommandEncoder();
        blur.Record(encoder, source, Size, Size, 7f, destination);
        using var commands = encoder.Finish();
        device.Queue.Submit(new ReadOnlySpan<CommandBuffer>(in commands));
        device.Poll(wait: true);
    }

    private static SceneBuffer BuildScene()
    {
        var builder = SceneBuilder.Begin();
        builder.BeginFrame();
        int identity = builder.AddTransform(Affine.Identity);
        int paintId = builder.AddPaint(Paint.Solid(0xFFFF0000u));
        builder.FillRect(new Rect(8, 8, Size - 8, Size - 8), paintId, identity);
        builder.EndFrame();
        return builder.End();
    }
}
