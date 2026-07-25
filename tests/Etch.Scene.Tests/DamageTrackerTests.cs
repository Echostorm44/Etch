using System;
using Etch.Geometry;
using Etch.Primitives;
using Etch.Scene;
using Etch.Scene.Damage;
using TUnit;

namespace Etch.Scene.Tests;

internal sealed class DamageTrackerTests
{
    [Test]
    public void IdenticalScenes_DirtyCount_IsZero()
    {
        var tracker = DamageTracker.Create(10, 10);
        tracker.MarkAllDirty();

        var scene1 = CreateSceneWithRect(0, 0, 32, 32);
        var scene2 = CreateSceneWithRect(0, 0, 32, 32);

        tracker.Diff(scene1, scene2);
        var result = tracker.Diff(scene1, scene2);

        if (result.DirtyCount != 0)
            throw new InvalidOperationException($"Expected DirtyCount=0 for identical scenes, got {result.DirtyCount}");

        tracker.Dispose();
    }

    [Test]
    public void TranslatedPath_YieldsUnionOfBeforeAndAfter()
    {
        var tracker = DamageTracker.Create(10, 10);
        tracker.MarkAllDirty();

        var scene1 = CreateSceneWithRect(0, 0, 32, 32);
        var scene2 = CreateSceneWithRect(32, 32, 64, 64);

        tracker.Diff(scene1, scene1);
        var result = tracker.Diff(scene2, scene2);

        if (result.DirtyCount < 2)
            throw new InvalidOperationException($"Expected at least 2 dirty tiles for translated path, got {result.DirtyCount}");

        tracker.Dispose();
    }

    [Test]
    public void ResetThenMarkAllDirty_AllTilesDirty()
    {
        var tracker = DamageTracker.Create(10, 10);
        tracker.MarkAllDirty();

        var scene1 = CreateSceneWithRect(0, 0, 100, 100);
        var scene2 = CreateSceneWithRect(0, 0, 100, 100);

        tracker.Diff(scene1, scene2);

        tracker.Reset();
        tracker.MarkAllDirty();

        var scene3 = CreateSceneWithRect(0, 0, 100, 100);
        var result = tracker.Diff(scene2, scene3);

        if (result.DirtyCount != 100)
            throw new InvalidOperationException($"Expected all 100 tiles dirty after Reset+MarkAllDirty, got {result.DirtyCount}");

        tracker.Dispose();
    }

    [Test]
    public void Diff_ZeroAlloc()
    {
        var tracker = DamageTracker.Create(10, 10);
        tracker.MarkAllDirty();

        var scene1 = CreateSceneWithRect(0, 0, 32, 32);
        var scene2 = CreateSceneWithRect(0, 0, 32, 32);

        tracker.Diff(scene1, scene2);

        using (AllocAssert.NoneExpected())
        {
            tracker.Diff(scene1, scene2);
        }

        tracker.Dispose();
    }

    [Test]
    public void PaintChange_YieldsExactTilesForPath()
    {
        var tracker = DamageTracker.Create(10, 10);
        tracker.MarkAllDirty();

        var scene1 = CreateSceneWithRect(0, 0, 32, 32);
        var scene2 = CreateSceneWithDifferentPaintRect(0, 0, 32, 32);

        tracker.Diff(scene1, scene2);
        var result = tracker.Diff(scene1, scene2);

        if (result.DirtyCount != 1)
            throw new InvalidOperationException($"Expected 1 dirty tile for paint change, got {result.DirtyCount}");

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

    private static SceneBuffer CreateSceneWithDifferentPaintRect(double minX, double minY, double maxX, double maxY)
    {
        var sb = SceneBuilder.Begin(256);
        sb.BeginFrame();

        int paintId = sb.AddPaint(Paint.Solid(0xFF804081));
        int xformId = sb.AddTransform(Affine.Identity);
        var rect = Rect.FromLTRB(minX, minY, maxX, maxY);

        sb.FillRect(rect, paintId, xformId);

        sb.EndFrame();
        return sb.End();
    }
}