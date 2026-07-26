using System;
using System.Collections.Generic;
using Etch.ClipBlendGradient;
using Etch.Geometry;
using Etch.Gpu;
using Etch.Gpu.Native;
using Etch.Scene;
using Etch.Testing;
using TUnit;
using CbBlendMode = Etch.ClipBlendGradient.BlendMode;

namespace Etch.Correctness.Tests.Determinism;

// wgpu-native is not thread-safe for concurrent device create/release. TUnit runs tests in
// parallel, so all GPU-device tests share this constraint key to serialize against each other
// (an intermittent access violation in DeviceRelease otherwise). Non-GPU tests still run parallel.
[NotInParallel("EtchGpuDevice")]
public class CrossBackendTests
{
    private const int RenderSize = 64;
    private const float MeanTolerance = 2f / 255f;
    private const float MaxTolerance = 8f / 255f;

    [Test]
#pragma warning disable CA1031 // Catch general exceptions for backend enumeration
    public async Task CrossBackend_PairwiseDiffs_WithinTolerance()
    {
        var availableBackends = DiscoverAvailableBackends();
        if (availableBackends.Count < 2)
        {
            return;
        }

        var scenes = CreateReferenceScenes();

        var resultsByBackend = new Dictionary<BackendType, List<byte[]>>();
        foreach (var backend in availableBackends)
        {
            var backendResults = new List<byte[]>(scenes.Length);
            foreach (var scene in scenes)
            {
                try
                {
                    byte[] result = SceneGpuRenderer.RenderToRgba8(scene, RenderSize, RenderSize, backend);
                    backendResults.Add(result);
                }
                catch (EtchException ex) when (ex.Code == Etch.PanicCodes.GpuAdapterUnavailable ||
                                                  ex.Code == Etch.PanicCodes.GpuDeviceCreationFailed)
                {
                    backendResults.Clear();
                    break;
                }
            }

            if (backendResults.Count == scenes.Length)
                resultsByBackend[backend] = backendResults;
        }

        if (resultsByBackend.Count < 2)
            return;

        var backendList = new List<BackendType>(resultsByBackend.Keys);
        for (int i = 0; i < backendList.Count; i++)
        {
            for (int j = i + 1; j < backendList.Count; j++)
            {
                var backA = backendList[i];
                var backB = backendList[j];
                var outputsA = resultsByBackend[backA];
                var outputsB = resultsByBackend[backB];

                for (int s = 0; s < outputsA.Count; s++)
                {
                    var result = PixelDiff.Compare(
                        outputsA[s], outputsB[s], RenderSize, RenderSize,
                        new DiffTolerance(MeanTolerance, MaxTolerance, MaxTolerance));

                    await Assert.That(result.Pass).IsTrue();
                    await Assert.That(result.MeanError).IsLessThanOrEqualTo(MeanTolerance);
                    await Assert.That(result.MaxError).IsLessThanOrEqualTo(MaxTolerance);
                }
            }
        }
    }
#pragma warning restore CA1031

    private static List<BackendType> DiscoverAvailableBackends()
    {
        var available = new List<BackendType>();
        var candidates = new[] { BackendType.Vulkan, BackendType.D3D12, BackendType.Metal, BackendType.OpenGL };

        using var instance = Instance.Create();
        foreach (var backend in candidates)
        {
            try
            {
                var (status, adapter) = AsyncRequest.RequestAdapterSync(
                    instance,
                    compatibleSurface: null,
                    preference: PowerPreference.HighPerformance,
                    backendType: backend);

                if (status == RequestAdapterStatus.Success && !adapter.IsInvalid)
                {
                    adapter.Dispose();
                    available.Add(backend);
                }
            }
            catch
            {
                // Backend not available — skip
            }
        }

        return available;
    }

    private static SceneBuffer[] CreateReferenceScenes()
    {
        return new[]
        {
            CreateSimpleFillRect(),
            CreateTranslatedRect(),
            CreateScaledRect(),
            CreateOverlappingRectsNormal(),
            CreateOverlappingRectsMultiply(),
            CreateFillTriangle(),
            CreateTransparentRect(),
            CreateScreenBlend(),
            CreateDarkenBlend(),
            CreateLightenBlend(),
            CreateColorDodgeBlend(),
            CreateColorBurnBlend(),
            CreateHardLightBlend(),
            CreateSoftLightBlend(),
            CreateDifferenceBlend(),
            CreateExclusionBlend(),
            CreateHueBlend(),
            CreateSaturationBlend(),
            CreateColorBlend(),
            CreateLuminosityBlend(),
        };
    }

    private static SceneBuffer BuildTwoLayerScene(
        uint backdropArgb, uint sourceArgb, CbBlendMode blendMode)
    {
        var builder = SceneBuilder.Begin();
        builder.BeginFrame();

        int identity = builder.AddTransform(Affine.Identity);

        var backdropPaint = Paint.Solid(backdropArgb, blendModeId: 0);
        int backdropPaintId = builder.AddPaint(backdropPaint);

        var sourcePaint = Paint.Solid(sourceArgb, blendModeId: (byte)blendMode);
        int sourcePaintId = builder.AddPaint(sourcePaint);

        builder.FillRect(new Rect(0, 0, RenderSize, RenderSize), backdropPaintId, identity);

        int srcW = RenderSize / 2;
        int srcH = RenderSize / 2;
        int srcX = (RenderSize - srcW) / 2;
        int srcY = (RenderSize - srcH) / 2;
        builder.FillRect(new Rect(srcX, srcY, srcX + srcW, srcY + srcH), sourcePaintId, identity);

        builder.EndFrame();
        return builder.End();
    }

    private static SceneBuffer CreateSimpleFillRect()
    {
        var builder = SceneBuilder.Begin();
        builder.BeginFrame();
        int xform = builder.AddTransform(Affine.Identity);
        var paint = Paint.Solid(0xFFFF0000u);
        int paintId = builder.AddPaint(paint);
        builder.FillRect(new Rect(8, 8, 56, 56), paintId, xform);
        builder.EndFrame();
        return builder.End();
    }

    private static SceneBuffer CreateTranslatedRect()
    {
        var builder = SceneBuilder.Begin();
        builder.BeginFrame();
        var translate = Affine.Translate(new Vec2(10, 10));
        int xform = builder.AddTransform(translate);
        var paint = Paint.Solid(0xFF00FF00u);
        int paintId = builder.AddPaint(paint);
        builder.FillRect(new Rect(0, 0, 44, 44), paintId, xform);
        builder.EndFrame();
        return builder.End();
    }

    private static SceneBuffer CreateScaledRect()
    {
        var builder = SceneBuilder.Begin();
        builder.BeginFrame();
        var scale = Affine.Identity.PreScale(2, 2);
        int xform = builder.AddTransform(scale);
        var paint = Paint.Solid(0xFF0000FFu);
        int paintId = builder.AddPaint(paint);
        builder.FillRect(new Rect(0, 0, 16, 16), paintId, xform);
        builder.EndFrame();
        return builder.End();
    }

    private static SceneBuffer CreateOverlappingRectsNormal()
        => BuildTwoLayerScene(0xFFFF0000u, 0xFF00FF00u, CbBlendMode.Normal);

    private static SceneBuffer CreateOverlappingRectsMultiply()
        => BuildTwoLayerScene(0xFFFF0000u, 0xFF00FF00u, CbBlendMode.Multiply);

    private static SceneBuffer CreateFillTriangle()
    {
        var builder = SceneBuilder.Begin();
        builder.BeginFrame();
        int xform = builder.AddTransform(Affine.Identity);
        var paint = Paint.Solid(0xFFFF00FFu);
        int paintId = builder.AddPaint(paint);

        using var pathBuilder = BezPathBuilder.Begin();
        pathBuilder.MoveTo(new Point(32, 8));
        pathBuilder.LineTo(new Point(56, 56));
        pathBuilder.LineTo(new Point(8, 56));
        pathBuilder.Close();
        var path = pathBuilder.Build();
        int pathId = builder.AddPath(path);

        builder.FillPath(pathId, paintId, xform, FillRule.NonZero);
        builder.EndFrame();
        return builder.End();
    }

    private static SceneBuffer CreateTransparentRect()
        => BuildTwoLayerScene(0xFFFF0000u, 0x80FFFF00u, CbBlendMode.Normal);

    private static SceneBuffer CreateScreenBlend()
        => BuildTwoLayerScene(0xFFFF0000u, 0xFF00FF00u, CbBlendMode.Screen);

    private static SceneBuffer CreateDarkenBlend()
        => BuildTwoLayerScene(0xFF808080u, 0xFF404040u, CbBlendMode.Darken);

    private static SceneBuffer CreateLightenBlend()
        => BuildTwoLayerScene(0xFF808080u, 0xFF404040u, CbBlendMode.Lighten);

    private static SceneBuffer CreateColorDodgeBlend()
        => BuildTwoLayerScene(0xFF808080u, 0xFF404040u, CbBlendMode.ColorDodge);

    private static SceneBuffer CreateColorBurnBlend()
        => BuildTwoLayerScene(0xFF808080u, 0xFF404040u, CbBlendMode.ColorBurn);

    private static SceneBuffer CreateHardLightBlend()
        => BuildTwoLayerScene(0xFF808080u, 0xFF404040u, CbBlendMode.HardLight);

    private static SceneBuffer CreateSoftLightBlend()
        => BuildTwoLayerScene(0xFF808080u, 0xFF404040u, CbBlendMode.SoftLight);

    private static SceneBuffer CreateDifferenceBlend()
        => BuildTwoLayerScene(0xFFFF0000u, 0xFF00FF00u, CbBlendMode.Difference);

    private static SceneBuffer CreateExclusionBlend()
        => BuildTwoLayerScene(0xFF808080u, 0xFF404040u, CbBlendMode.Exclusion);

    private static SceneBuffer CreateHueBlend()
        => BuildTwoLayerScene(0xFF8000FFu, 0xFFFF8000u, CbBlendMode.Hue);

    private static SceneBuffer CreateSaturationBlend()
        => BuildTwoLayerScene(0xFF8000FFu, 0xFFFF8000u, CbBlendMode.Saturation);

    private static SceneBuffer CreateColorBlend()
        => BuildTwoLayerScene(0xFF8000FFu, 0xFFFF8000u, CbBlendMode.Color);

    private static SceneBuffer CreateLuminosityBlend()
        => BuildTwoLayerScene(0xFF8000FFu, 0xFFFF8000u, CbBlendMode.Luminosity);
}
