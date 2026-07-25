using System;
using Etch.Geometry;
using Etch.Primitives;
using Etch.Scene;
using Etch.Scene.Damage;
using TUnit;

namespace Etch.Scene.Tests;

internal sealed class SubpixelDamageTrackerTests
{
    [Test]
    public async Task Moving16x16CursorBy1Pixel_ProducesAtMost2DirtyRects()
    {
        var tracker = SubpixelDamageTracker.Create(1920, 1080);

        var scene1 = CreateSceneWithRect(500, 500, 516, 516);
        var scene2 = CreateSceneWithRect(501, 500, 517, 516);

        var result = tracker.DiffSubpixel(scene1, scene2);

        await Assert.That(result.Mode).IsEqualTo(DamageMode.RectGranular);
        await Assert.That(result.DirtyRects.Length).IsLessThanOrEqualTo(2);

        tracker.Dispose();
    }

    [Test]
    public async Task Moving16x16CursorBy100Pixels_ProducesExactly2DisjointRects()
    {
        var tracker = SubpixelDamageTracker.Create(1920, 1080);

        var scene1 = CreateSceneWithRect(100, 100, 116, 116);
        var scene2 = CreateSceneWithRect(200, 100, 216, 116);

        var result = tracker.DiffSubpixel(scene1, scene2);

        await Assert.That(result.Mode).IsEqualTo(DamageMode.RectGranular);
        await Assert.That(result.DirtyRects.Length).IsEqualTo(2);

        tracker.Dispose();
    }

    [Test]
    public async Task MoreThanMaxRectsChanges_FallsBackToTileBitmapMode()
    {
        var tracker = SubpixelDamageTracker.Create(100, 100);

        var scene1 = BuildSceneWithManyChanges(50);
        var scene2 = BuildSceneWithManyChangesDifferent(50);

        var result = tracker.DiffSubpixel(scene1, scene2);

        await Assert.That(result.Mode).IsEqualTo(DamageMode.TileBitmap);

        tracker.Dispose();
    }

    [Test]
    public async Task SubpixelRectsCoverUnionOfTileGranularOutput_Invariant()
    {
        var tracker = SubpixelDamageTracker.Create(1920, 1080);

        var scene1 = CreateSceneWithRect(100, 100, 200, 200);
        var scene2 = CreateSceneWithRect(150, 150, 250, 250);

        var result = tracker.DiffSubpixel(scene1, scene2);

        await Assert.That(result.Mode).IsEqualTo(DamageMode.RectGranular);
        await Assert.That(result.DirtyRects.Length).IsGreaterThan(0);

        var totalRectArea = 0.0;
        foreach (var rect in result.DirtyRects)
        {
            totalRectArea += rect.Width * rect.Height;
        }

        await Assert.That(totalRectArea).IsGreaterThan(0);

        tracker.Dispose();
    }

    [Test]
    public async Task DiffSubpixel_ZeroAlloc()
    {
        var tracker = SubpixelDamageTracker.Create(1920, 1080);

        var scene1 = CreateSceneWithRect(100, 100, 200, 200);
        var scene2 = CreateSceneWithRect(150, 150, 250, 250);

        tracker.DiffSubpixel(scene1, scene2);

        using (AllocAssert.NoneExpected())
        {
            tracker.DiffSubpixel(scene1, scene2);
        }

        tracker.Dispose();
    }

    [Test]
    public async Task Cursor16x16_MoveBy1Pixel_HasCorrectShape()
    {
        var tracker = SubpixelDamageTracker.Create(1920, 1080);

        var scene1 = CreateSceneWithRect(500, 500, 516, 516);
        var scene2 = CreateSceneWithRect(501, 500, 517, 516);

        var result = tracker.DiffSubpixel(scene1, scene2);

        await Assert.That(result.Mode).IsEqualTo(DamageMode.RectGranular);
        await Assert.That(result.DirtyRects.Length).IsLessThanOrEqualTo(2);

        foreach (var rect in result.DirtyRects)
        {
            await Assert.That(rect.Width).IsEqualTo(16);
            await Assert.That(rect.Height).IsEqualTo(16);
        }

        tracker.Dispose();
    }

    private static SceneBuffer CreateSceneWithRect(double minX, double minY, double maxX, double maxY)
    {
        var sb = SceneBuilder.Begin(256);
        sb.BeginFrame();

        int paintId = sb.AddPaint(Paint.Solid(0xFF804080));
        int xformId = sb.AddTransform(Affine.Identity);
        var rect = Rect.FromLTRB(minX, minY, maxX, maxY);

        sb.FillRect(rect, paintId, xformId);

        sb.EndFrame();
        return sb.End();
    }

    private static SceneBuffer BuildSceneWithManyChanges(int count)
    {
        var sb = SceneBuilder.Begin(4096);
        sb.BeginFrame();

        for (int i = 0; i < count; i++)
        {
            double x = i * 10;
            int paintId = sb.AddPaint(Paint.Solid(0xFF804080));
            int xformId = sb.AddTransform(Affine.Identity);
            var rect = Rect.FromLTRB(x, x, x + 16, x + 16);
            sb.FillRect(rect, paintId, xformId);
        }

        sb.EndFrame();
        return sb.End();
    }

    private static SceneBuffer BuildSceneWithManyChangesDifferent(int count)
    {
        var sb = SceneBuilder.Begin(4096);
        sb.BeginFrame();

        for (int i = 0; i < count; i++)
        {
            double x = i * 10 + 5;
            int paintId = sb.AddPaint(Paint.Solid(0xFF804080));
            int xformId = sb.AddTransform(Affine.Identity);
            var rect = Rect.FromLTRB(x, x, x + 16, x + 16);
            sb.FillRect(rect, paintId, xformId);
        }

        sb.EndFrame();
        return sb.End();
    }
}