using System;
using System.IO;
using System.Security.Cryptography;
using Etch.ClipBlendGradient;
using Etch.Geometry;
using Etch.Scene;
using Etch.SkiaRef;
using TUnit;
using CbBlendMode = Etch.ClipBlendGradient.BlendMode;

namespace Etch.Correctness.Tests.Goldens;

public class GoldenTests
{
    private const int BaselineWidth = 256;
    private const int BaselineHeight = 256;

    [Test]
    public async Task Skia_Determinism_SameSceneTwice_ByteIdentical()
    {
        var scene = CreateSimpleFillRectScene();
        byte[] first = SkiaSceneRenderer.Render(scene, BaselineWidth, BaselineHeight);
        byte[] second = SkiaSceneRenderer.Render(scene, BaselineWidth, BaselineHeight);

        await Assert.That(first.Length).IsEqualTo(second.Length);

        string hash1 = ComputeSha256(first);
        string hash2 = ComputeSha256(second);
        await Assert.That(hash1).IsEqualTo(hash2);
    }

    [Test]
    public async Task Skia_10SceneSmoke_AllRenderWithoutException()
    {
        var scenes = CreateSmokeScenes();
        foreach (var scene in scenes)
        {
            byte[] result = SkiaSceneRenderer.Render(scene, BaselineWidth, BaselineHeight);
            await Assert.That(result.Length).IsGreaterThan(0);
        }
    }

    [Test]
    public async Task Skia_GoldenRegen_MatchesCommitted()
    {
        string goldenDir = Path.Combine(AppContext.BaseDirectory,
            "../../../../../../tests/Etch.Correctness.Tests/Goldens/smoke");

        if (!Directory.Exists(goldenDir))
            Directory.CreateDirectory(goldenDir);

        var scene = CreateSimpleFillRectScene();
        byte[] result = SkiaSceneRenderer.Render(scene, BaselineWidth, BaselineHeight);

        string goldenPath = Path.Combine(goldenDir, "simple-fill-rect.png");
        if (!File.Exists(goldenPath))
        {
            await File.WriteAllBytesAsync(goldenPath, result);
            return;
        }

        byte[] golden = await File.ReadAllBytesAsync(goldenPath);
        await Assert.That(ComputeSha256(result)).IsEqualTo(ComputeSha256(golden));
    }

    [Test]
    public async Task Skia_SceneWithPath_RendersWithoutException()
    {
        var scene = CreateTriangleScene();
        byte[] result = SkiaSceneRenderer.Render(scene, BaselineWidth, BaselineHeight);
        await Assert.That(result.Length).IsGreaterThan(0);
    }

    [Test]
    public async Task Skia_SceneWithStrokePath_RendersWithoutException()
    {
        var scene = CreateStrokeLineScene();
        byte[] result = SkiaSceneRenderer.Render(scene, BaselineWidth, BaselineHeight);
        await Assert.That(result.Length).IsGreaterThan(0);
    }

    [Test]
    public async Task Skia_BlendModes_AllRenderWithoutException()
    {
        var modes = Enum.GetValues<CbBlendMode>();
        foreach (var mode in modes)
        {
            var scene = BuildTwoLayerScene(0xFFFF0000u, 0xFF00FF00u, mode);
            byte[] result = SkiaSceneRenderer.Render(scene, BaselineWidth, BaselineHeight);
            await Assert.That(result.Length).IsGreaterThan(0);
        }
    }

    [Test]
    public async Task Skia_SceneWithTransform_RendersWithoutException()
    {
        var builder = SceneBuilder.Begin();
        builder.BeginFrame();
        var translate = Affine.Translate(new Vec2(50, 50));
        int xform = builder.AddTransform(translate);
        var paint = Paint.Solid(0xFFFF0000u);
        int paintId = builder.AddPaint(paint);
        builder.FillRect(new Rect(0, 0, 100, 100), paintId, xform);
        builder.EndFrame();
        var scene = builder.End();

        byte[] result = SkiaSceneRenderer.Render(scene, BaselineWidth, BaselineHeight);
        await Assert.That(result.Length).IsGreaterThan(0);
    }

    private static SceneBuffer CreateSimpleFillRectScene()
    {
        var builder = SceneBuilder.Begin();
        builder.BeginFrame();
        int xform = builder.AddTransform(Affine.Identity);
        var paint = Paint.Solid(0xFFFF0000u);
        int paintId = builder.AddPaint(paint);
        builder.FillRect(new Rect(50, 50, 200, 200), paintId, xform);
        builder.EndFrame();
        return builder.End();
    }

    private static SceneBuffer CreateTriangleScene()
    {
        var builder = SceneBuilder.Begin();
        builder.BeginFrame();
        int xform = builder.AddTransform(Affine.Identity);
        var paint = Paint.Solid(0xFFFF00FFu);
        int paintId = builder.AddPaint(paint);

        using var pathBuilder = BezPathBuilder.Begin();
        pathBuilder.MoveTo(new Point(50, 20));
        pathBuilder.LineTo(new Point(200, 200));
        pathBuilder.LineTo(new Point(20, 200));
        pathBuilder.Close();
        var path = pathBuilder.Build();
        int pathId = builder.AddPath(path);

        builder.FillPath(pathId, paintId, xform, FillRule.NonZero);
        builder.EndFrame();
        return builder.End();
    }

    private static SceneBuffer CreateStrokeLineScene()
    {
        var builder = SceneBuilder.Begin();
        builder.BeginFrame();
        int xform = builder.AddTransform(Affine.Identity);
        var paint = Paint.Solid(0xFF0000FFu);
        int paintId = builder.AddPaint(paint);

        using var pathBuilder = BezPathBuilder.Begin();
        pathBuilder.MoveTo(new Point(30, 30));
        pathBuilder.LineTo(new Point(200, 200));
        var path = pathBuilder.Build();
        int pathId = builder.AddPath(path);

        builder.StrokePath(pathId, paintId, xform, 4.0f, default);
        builder.EndFrame();
        return builder.End();
    }

    private static SceneBuffer[] CreateSmokeScenes()
    {
        return new[]
        {
            CreateSimpleFillRectScene(),
            BuildTwoLayerScene(0xFFFF0000u, 0xFF00FF00u, CbBlendMode.Normal),
            BuildTwoLayerScene(0xFFFF0000u, 0xFF00FF00u, CbBlendMode.Multiply),
            BuildTwoLayerScene(0xFFFF0000u, 0xFF00FF00u, CbBlendMode.Screen),
            BuildTwoLayerScene(0xFF808080u, 0xFF404040u, CbBlendMode.Overlay),
            BuildTwoLayerScene(0xFF808080u, 0xFF404040u, CbBlendMode.Darken),
            BuildTwoLayerScene(0xFF808080u, 0xFF404040u, CbBlendMode.Lighten),
            BuildTwoLayerScene(0xFF808080u, 0xFF404040u, CbBlendMode.ColorDodge),
            BuildTwoLayerScene(0xFF808080u, 0xFF404040u, CbBlendMode.ColorBurn),
            BuildTwoLayerScene(0xFFFF0000u, 0xFF00FF00u, CbBlendMode.Difference),
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

        builder.FillRect(new Rect(0, 0, BaselineWidth, BaselineHeight), backdropPaintId, identity);
        builder.FillRect(new Rect(64, 64, 192, 192), sourcePaintId, identity);

        builder.EndFrame();
        return builder.End();
    }

    private static string ComputeSha256(byte[] data)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(data, hash);
        return Convert.ToHexString(hash);
    }
}
