using System;
using System.Security.Cryptography;
using Etch.ClipBlendGradient;
using Etch.Geometry;
using Etch.Scene;
using Etch.Testing;
using Etch.Tiling.Scheduler;
using TUnit;
using CbBlendMode = Etch.ClipBlendGradient.BlendMode;

namespace Etch.Correctness.Tests.Determinism;

public class DeterminismRegressionTests
{
    private const int RenderSize = 64;
    private const int IterationCount = 16;

    [Test]
    public async Task Cpu_SingleThreaded_Scene01_SimpleFillRect_ByteIdentical()
        => await RunCpuSingleThreadedTest(CreateSimpleFillRect(), "01-SimpleFillRect");

    [Test]
    public async Task Cpu_SingleThreaded_Scene02_TranslatedRect_ByteIdentical()
        => await RunCpuSingleThreadedTest(CreateTranslatedRect(), "02-TranslatedRect");

    [Test]
    public async Task Cpu_SingleThreaded_Scene03_ScaledRect_ByteIdentical()
        => await RunCpuSingleThreadedTest(CreateScaledRect(), "03-ScaledRect");

    [Test]
    public async Task Cpu_SingleThreaded_Scene04_OverlappingRectsNormal_ByteIdentical()
        => await RunCpuSingleThreadedTest(CreateOverlappingRectsNormal(), "04-OverlappingRectsNormal");

    [Test]
    public async Task Cpu_SingleThreaded_Scene05_OverlappingRectsMultiply_ByteIdentical()
        => await RunCpuSingleThreadedTest(CreateOverlappingRectsMultiply(), "05-OverlappingRectsMultiply");

    [Test]
    public async Task Cpu_SingleThreaded_Scene06_FillTriangle_ByteIdentical()
        => await RunCpuSingleThreadedTest(CreateFillTriangle(), "06-FillTriangle");

    [Test]
    public async Task Cpu_SingleThreaded_Scene07_TransparentRect_ByteIdentical()
        => await RunCpuSingleThreadedTest(CreateTransparentRect(), "07-TransparentRect");

    [Test]
    public async Task Cpu_SingleThreaded_Scene08_ScreenBlend_ByteIdentical()
        => await RunCpuSingleThreadedTest(CreateScreenBlend(), "08-ScreenBlend");

    [Test]
    public async Task Cpu_SingleThreaded_Scene09_DarkenBlend_ByteIdentical()
        => await RunCpuSingleThreadedTest(CreateDarkenBlend(), "09-DarkenBlend");

    [Test]
    public async Task Cpu_SingleThreaded_Scene10_LightenBlend_ByteIdentical()
        => await RunCpuSingleThreadedTest(CreateLightenBlend(), "10-LightenBlend");

    [Test]
    public async Task Cpu_SingleThreaded_Scene11_ColorDodgeBlend_ByteIdentical()
        => await RunCpuSingleThreadedTest(CreateColorDodgeBlend(), "11-ColorDodgeBlend");

    [Test]
    public async Task Cpu_SingleThreaded_Scene12_ColorBurnBlend_ByteIdentical()
        => await RunCpuSingleThreadedTest(CreateColorBurnBlend(), "12-ColorBurnBlend");

    [Test]
    public async Task Cpu_SingleThreaded_Scene13_HardLightBlend_ByteIdentical()
        => await RunCpuSingleThreadedTest(CreateHardLightBlend(), "13-HardLightBlend");

    [Test]
    public async Task Cpu_SingleThreaded_Scene14_SoftLightBlend_ByteIdentical()
        => await RunCpuSingleThreadedTest(CreateSoftLightBlend(), "14-SoftLightBlend");

    [Test]
    public async Task Cpu_SingleThreaded_Scene15_DifferenceBlend_ByteIdentical()
        => await RunCpuSingleThreadedTest(CreateDifferenceBlend(), "15-DifferenceBlend");

    [Test]
    public async Task Cpu_SingleThreaded_Scene16_ExclusionBlend_ByteIdentical()
        => await RunCpuSingleThreadedTest(CreateExclusionBlend(), "16-ExclusionBlend");

    [Test]
    public async Task Cpu_SingleThreaded_Scene17_HueBlend_ByteIdentical()
        => await RunCpuSingleThreadedTest(CreateHueBlend(), "17-HueBlend");

    [Test]
    public async Task Cpu_SingleThreaded_Scene18_SaturationBlend_ByteIdentical()
        => await RunCpuSingleThreadedTest(CreateSaturationBlend(), "18-SaturationBlend");

    [Test]
    public async Task Cpu_SingleThreaded_Scene19_ColorBlend_ByteIdentical()
        => await RunCpuSingleThreadedTest(CreateColorBlend(), "19-ColorBlend");

    [Test]
    public async Task Cpu_SingleThreaded_Scene20_LuminosityBlend_ByteIdentical()
        => await RunCpuSingleThreadedTest(CreateLuminosityBlend(), "20-LuminosityBlend");

    [Test]
#pragma warning disable CA2000 // Dispose ownership transferred to try-finally
    public void Gpu_AllScenes_ByteIdenticalAcrossRuns()
    {
        SceneGpuRenderer.GpuRenderCache cache;
        try
        {
            cache = new SceneGpuRenderer.GpuRenderCache(RenderSize, RenderSize);
        }
        catch (EtchException ex) when (ex.Code == Etch.PanicCodes.GpuAdapterUnavailable ||
                                         ex.Code == Etch.PanicCodes.GpuDeviceCreationFailed)
        {
            return;
        }

        try
        {
            var scenes = new (SceneBuffer scene, string name)[]
            {
                (CreateSimpleFillRect(), "01-SimpleFillRect"),
                (CreateTranslatedRect(), "02-TranslatedRect"),
                (CreateScaledRect(), "03-ScaledRect"),
                (CreateOverlappingRectsNormal(), "04-OverlappingRectsNormal"),
                (CreateOverlappingRectsMultiply(), "05-OverlappingRectsMultiply"),
                (CreateFillTriangle(), "06-FillTriangle"),
                (CreateTransparentRect(), "07-TransparentRect"),
                (CreateScreenBlend(), "08-ScreenBlend"),
                (CreateDarkenBlend(), "09-DarkenBlend"),
                (CreateLightenBlend(), "10-LightenBlend"),
                (CreateColorDodgeBlend(), "11-ColorDodgeBlend"),
                (CreateColorBurnBlend(), "12-ColorBurnBlend"),
                (CreateHardLightBlend(), "13-HardLightBlend"),
                (CreateSoftLightBlend(), "14-SoftLightBlend"),
                (CreateDifferenceBlend(), "15-DifferenceBlend"),
                (CreateExclusionBlend(), "16-ExclusionBlend"),
                (CreateHueBlend(), "17-HueBlend"),
                (CreateSaturationBlend(), "18-SaturationBlend"),
                (CreateColorBlend(), "19-ColorBlend"),
                (CreateLuminosityBlend(), "20-LuminosityBlend"),
            };

            foreach (var (scene, name) in scenes)
            {
                string firstHash = null!;
                for (int i = 0; i < IterationCount; i++)
                {
                    byte[] result = cache.Render(scene);
                    string hash = ComputeSha256(result);

                    if (firstHash == null)
                    {
                        firstHash = hash;
                        continue;
                    }

                    if (hash != firstHash)
                        throw new InvalidOperationException(
                            $"GPU determinism failed for scene {name}: " +
                            $"run 0 hash {firstHash} != run {i} hash {hash}");
                }
            }
        }
        finally
        {
            cache.Dispose();
        }
    }
#pragma warning restore CA2000

    [Test]
    public async Task MtCpu_SameSceneByteIdenticalUnderDeterministicMerge()
    {
        var scene = CreateOverlappingRectsNormal();
        using var scheduler = new SingleThreadedTileScheduler();

        string first = null!;
        for (int i = 0; i < IterationCount; i++)
        {
            byte[] result = SceneCpuRenderer.RenderToRgba8Parallel(scene, RenderSize, RenderSize, scheduler);
            string hash = ComputeSha256(result);

            if (first == null)
            {
                first = hash;
                continue;
            }

            await Assert.That(hash).IsEqualTo(first);
        }
    }

    private static async Task RunCpuSingleThreadedTest(SceneBuffer scene, string name)
    {
        string firstHash = null!;
        for (int i = 0; i < IterationCount; i++)
        {
            byte[] result = SceneRunner.RunCpu(scene, RenderSize, RenderSize);
            string hash = ComputeSha256(result);

            if (firstHash == null)
            {
                firstHash = hash;
                continue;
            }

            await Assert.That(hash).IsEqualTo(firstHash);
        }
    }

    private static string ComputeSha256(byte[] data)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(data, hash);
        return Convert.ToHexString(hash);
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
