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

        using var scene1 = CreateSceneWithRect(500, 500, 516, 516);
        using var scene2 = CreateSceneWithRect(501, 500, 517, 516);

        var result = tracker.DiffSubpixel(scene1, scene2);

        await Assert.That(result.Mode).IsEqualTo(DamageMode.RectGranular);
        await Assert.That(result.DirtyRects.Length).IsLessThanOrEqualTo(2);

        tracker.Dispose();
    }

    [Test]
    public async Task Moving16x16CursorBy100Pixels_ProducesExactly2DisjointRects()
    {
        var tracker = SubpixelDamageTracker.Create(1920, 1080);

        using var scene1 = CreateSceneWithRect(100, 100, 116, 116);
        using var scene2 = CreateSceneWithRect(200, 100, 216, 116);

        var result = tracker.DiffSubpixel(scene1, scene2);

        await Assert.That(result.Mode).IsEqualTo(DamageMode.RectGranular);
        await Assert.That(result.DirtyRects.Length).IsEqualTo(2);

        tracker.Dispose();
    }

    [Test]
    public async Task MoreThanMaxRectsChanges_FallsBackToTileBitmapMode()
    {
        var tracker = SubpixelDamageTracker.Create(1920, 1080);

        using var scene1 = BuildCursorGridScene(50, cursorOffsetX: 0);
        using var scene2 = BuildCursorGridScene(50, cursorOffsetX: 20);

        var result = tracker.DiffSubpixel(scene1, scene2);

        await Assert.That(result.Mode).IsEqualTo(DamageMode.TileBitmap);

        tracker.Dispose();
    }

    [Test]
    public async Task SubpixelRectsCoverUnionOfTileGranularOutput_Invariant()
    {
        var tracker = SubpixelDamageTracker.Create(1920, 1080);

        using var scene1 = CreateSceneWithRect(100, 100, 200, 200);
        using var scene2 = CreateSceneWithRect(150, 150, 250, 250);

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

        using var scene1 = CreateSceneWithRect(100, 100, 200, 200);
        using var scene2 = CreateSceneWithRect(150, 150, 250, 250);

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

        using var scene1 = CreateSceneWithRect(500, 500, 516, 516);
        using var scene2 = CreateSceneWithRect(501, 500, 517, 516);

        var result = tracker.DiffSubpixel(scene1, scene2);

        await Assert.That(result.Mode).IsEqualTo(DamageMode.RectGranular);
        // SCN-008: a 1-pixel move of a 16x16 cursor overlaps its previous position, so the two
        // dirty rectangles merge into a single 17x16 rectangle.
        await Assert.That(result.DirtyRects.Length).IsEqualTo(1);
        await Assert.That(result.DirtyRects[0].Width).IsEqualTo(17);
        await Assert.That(result.DirtyRects[0].Height).IsEqualTo(16);

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

    // Cursors sit on a 40px grid and move 20px, so every previous and current 16x16 cursor
    // rectangle is disjoint from all others: 2 * cursorCount rectangles that cannot merge.
    private static SceneBuffer BuildCursorGridScene(int cursorCount, double cursorOffsetX)
    {
        const int CursorsPerRow = 10;
        const double GridSpacing = 40;
        const double CursorSize = 16;

        var sb = SceneBuilder.Begin(4096);
        sb.BeginFrame();

        int paintId = sb.AddPaint(Paint.Solid(0xFF804080));
        int xformId = sb.AddTransform(Affine.Identity);
        for (int i = 0; i < cursorCount; i++)
        {
            double x = (i % CursorsPerRow) * GridSpacing + cursorOffsetX;
            double y = (i / CursorsPerRow) * GridSpacing;
            sb.FillRect(Rect.FromLTRB(x, y, x + CursorSize, y + CursorSize), paintId, xformId);
        }

        sb.EndFrame();
        return sb.End();
    }
}