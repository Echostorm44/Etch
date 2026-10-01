using System.Threading.Tasks;
using Etch;
using Etch.Geometry;
using Etch.Scene;
using Etch.Testing;
using TUnit.Core;

namespace Etch.Testing.Tests;

internal sealed class SceneGpuRendererTests
{
    [Test]
    public async Task FillRectSolidRedCenterPixelIsRed()
    {
        // wgpu-native offscreen rendering segfaults (uncatchably) against software Vulkan
        // (Lavapipe/SwiftShader) on headless CI runners — an upstream/driver limitation, not
        // an Etch rendering bug. The GPU render path is exercised on hardware-GPU runners
        // (Windows/macOS CI and dev machines), so skip it in software-GPU mode.
        if (SceneRunner.IsSoftwareGpu)
        {
            Skip.Test("Software-GPU CI (ETCH_SOFTWARE_GPU=1): wgpu offscreen render is unstable on " +
                "headless software-Vulkan runners; GPU render path is covered on hardware-GPU runners.");
            return;
        }

        var sb = SceneBuilder.Begin();
        sb.BeginFrame();

        var identity = sb.AddTransform(Affine.Identity);
        var red = sb.AddPaint(Paint.Solid(0xFFFF0000));

        sb.FillRect(new Rect(10, 10, 54, 54), red, identity);
        sb.EndFrame();

        var scene = sb.End();

        byte[] output;
        try
        {
            output = SceneRunner.RunGpu(scene, 64, 64);
        }
        catch (EtchException ex) when (ex.Code == PanicCodes.GpuAdapterUnavailable ||
                                         ex.Code == PanicCodes.GpuDeviceCreationFailed)
        {
            // No GPU adapter on this runner (e.g. headless CI without a software driver) —
            // this test validates the GPU render path where one exists, so skip rather than fail.
            return;
        }

        // Center of the 64×64 image, inside the red rect.
        // RenderToRgba8 returns RGBA byte order (Rgba8Unorm), matching SceneRunner.RunCpu:
        // R at index 0, then G, B, A.
        int cx = 32;
        int cy = 32;
        int idx = (cy * 64 + cx) * 4;

        byte r = output[idx + 0];
        byte g = output[idx + 1];
        byte b = output[idx + 2];
        byte a = output[idx + 3];

        await Assert.That((int)r).IsGreaterThan(200);
        await Assert.That((int)g).IsEqualTo(0);
        await Assert.That((int)b).IsEqualTo(0);
        await Assert.That((int)a).IsGreaterThan(200);
    }
}
