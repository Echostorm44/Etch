using System;
using System.Diagnostics;
using Etch.Geometry;
using Etch.Gpu.Native;
using Etch.Scene;
using TUnit;

namespace Etch.Gpu.Compositor.Tests;

internal sealed class GpuCompositorPerformanceTests : IDisposable
{
    private readonly Instance _instance;
    private readonly Adapter _adapter;
    private readonly Device _device;
    private readonly GpuCompositor _compositor;

    public GpuCompositorPerformanceTests()
    {
        _instance = Instance.Create();
        var (adapterStatus, adapter) = AsyncRequest.RequestAdapterSync(_instance, backendType: BackendType.Undefined);
        if (adapterStatus != RequestAdapterStatus.Success || adapter.IsInvalid)
        {
            throw new InvalidOperationException("No GPU adapter available for performance tests");
        }
        _adapter = adapter;

        var (deviceStatus, device) = AsyncRequest.RequestDeviceSync(_instance, _adapter);
        if (deviceStatus != RequestDeviceStatus.Success || device.IsInvalid)
        {
            throw new InvalidOperationException("Could not create GPU device for performance tests");
        }
        _device = device;
        _compositor = new GpuCompositor(_device);
    }

    public void Dispose()
    {
        _compositor.Dispose();
        _device.Dispose();
        _adapter.Dispose();
        _instance.Dispose();
    }

    [Test]
    public async Task SinglePath_1080p_UnderBudget()
    {
        const int width = 1920;
        const int height = 1080;
        var scene = BuildRandomPathsScene(width, height, 1);

        double elapsedMs = MeasureGpuRender(scene, width, height);
        Console.WriteLine($"Single path 1080p: {elapsedMs:F2}ms");
        // Budget is 8ms because RenderToRgba8 includes texture creation + readback,
        // which is slower than direct-to-swapchain. Real app should be ~2-3x faster.
        await Assert.That(elapsedMs).IsLessThanOrEqualTo(8.0);
    }

    [Test]
    public async Task HundredPaths_1080p_UnderBudget()
    {
        const int width = 1920;
        const int height = 1080;
        var scene = BuildRandomPathsScene(width, height, 100);

        double elapsedMs = MeasureGpuRender(scene, width, height);
        Console.WriteLine($"100 paths 1080p: {elapsedMs:F2}ms");
        await Assert.That(elapsedMs).IsLessThanOrEqualTo(32.0);
    }

    [Test]
    public async Task FiveHundredPaths_1080p_UnderBudget()
    {
        const int width = 1920;
        const int height = 1080;
        var scene = BuildRandomPathsScene(width, height, 500);

        double elapsedMs = MeasureGpuRender(scene, width, height);
        Console.WriteLine($"500 paths 1080p: {elapsedMs:F2}ms");
        await Assert.That(elapsedMs).IsLessThanOrEqualTo(100.0);
    }

    private double MeasureGpuRender(SceneBuffer scene, int width, int height)
    {
        // Warm-up
        _ = _compositor.RenderToRgba8(scene, width, height);

        var sw = Stopwatch.StartNew();
        _ = _compositor.RenderToRgba8(scene, width, height);
        sw.Stop();
        return sw.Elapsed.TotalMilliseconds;
    }

    private static SceneBuffer BuildRandomPathsScene(int width, int height, int count)
    {
        var builder = SceneBuilder.Begin(Math.Max(4096, count * 8));
        builder.BeginFrame();
        int identity = builder.AddTransform(Affine.Identity);

        for (int i = 0; i < count; i++)
        {
            uint color = (uint)(0xFF000000 | ((i * 1234567) & 0xFFFFFF));
            int paintId = builder.AddPaint(Paint.Solid(color));

            double x0 = ((i * 137.0) % (width - 120));
            double y0 = ((i * 269.0) % (height - 120));
            double x1 = x0 + 20 + ((i * 53) % 80);
            double y1 = y0 + 20 + ((i * 97) % 80);

            using var pb = BezPathBuilder.Begin();
            pb.MoveTo(new Point(x0, y0));
            pb.LineTo(new Point(x1, y0));
            pb.LineTo(new Point(x1, y1));
            pb.LineTo(new Point(x0, y1));
            pb.Close();
            int pathId = builder.AddPath(pb.Build());
            builder.FillPath(pathId, paintId, identity, FillRule.NonZero);
        }

        builder.EndFrame();
        return builder.End();
    }
}
