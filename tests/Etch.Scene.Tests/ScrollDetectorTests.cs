using Etch.Geometry;
using Etch.Primitives;
using Etch.Scene;
using Etch.Scene.Damage;
using TUnit;

namespace Etch.Scene.Tests;

internal sealed class ScrollDetectorTests
{
    [Test]
    public async Task PureVerticalScroll_IsScrollTrueDeltaCorrect()
    {
        var viewport = Rect.FromLTRB(0, 0, 1920, 1080);

        var scene1 = CreateScrollScene(0, 0);
        var scene2 = CreateScrollScene(0, 50);

        var hint = ScrollDetector.Detect(scene1, scene2, viewport);

        await Assert.That(hint.IsScroll).IsTrue();
        await Assert.That(hint.Delta.X).IsEqualTo(0);
        await Assert.That(hint.Delta.Y).IsEqualTo(50);
    }

    [Test]
    public async Task PureHorizontalScroll_IsScrollTrueDeltaCorrect()
    {
        var viewport = Rect.FromLTRB(0, 0, 1920, 1080);

        var scene1 = CreateScrollScene(0, 0);
        var scene2 = CreateScrollScene(30, 0);

        var hint = ScrollDetector.Detect(scene1, scene2, viewport);

        await Assert.That(hint.IsScroll).IsTrue();
        await Assert.That(hint.Delta.X).IsEqualTo(30);
        await Assert.That(hint.Delta.Y).IsEqualTo(0);
    }

    [Test]
    public async Task NonScrollScene_IsScrollFalse()
    {
        var viewport = Rect.FromLTRB(0, 0, 1920, 1080);

        var scene1 = CreateSceneWithRect(100, 100, 200, 200);
        var scene2 = CreateSceneWithRect(300, 400, 400, 500);

        var hint = ScrollDetector.Detect(scene1, scene2, viewport);

        await Assert.That(hint.IsScroll).IsFalse();
    }

    [Test]
    public async Task ScrollWithAddedItem_IsScrollTrue()
    {
        var viewport = Rect.FromLTRB(0, 0, 1920, 1080);

        var scene1 = CreateScrollScene(0, 0);
        var scene2 = CreateScrollSceneWithExtra(0, 50);

        var hint = ScrollDetector.Detect(scene1, scene2, viewport);

        await Assert.That(hint.IsScroll).IsTrue();
    }

    [Test]
    public async Task EmptyScene_ReturnsNone()
    {
        var viewport = Rect.FromLTRB(0, 0, 1920, 1080);

        var sb1 = SceneBuilder.Begin(256);
        sb1.BeginFrame();
        sb1.EndFrame();
        var scene1 = sb1.End();

        var sb2 = SceneBuilder.Begin(256);
        sb2.BeginFrame();
        sb2.EndFrame();
        var scene2 = sb2.End();

        var hint = ScrollDetector.Detect(scene1, scene2, viewport);

        await Assert.That(hint.IsScroll).IsFalse();
    }

    [Test]
    public async Task Detect_ZeroAlloc()
    {
        var viewport = Rect.FromLTRB(0, 0, 1920, 1080);

        var scene1 = CreateScrollScene(0, 0);
        var scene2 = CreateScrollScene(0, 50);

        ScrollDetector.Detect(scene1, scene2, viewport);

        using (AllocAssert.NoneExpected())
        {
            ScrollDetector.Detect(scene1, scene2, viewport);
        }
    }

    [Test]
    public async Task ThresholdAt60Percent_StillDetectsScroll()
    {
        var viewport = Rect.FromLTRB(0, 0, 1920, 1080);

        var scene1 = CreateScrollScene(0, 0);
        var scene2 = CreateScrollScene(0, 50);

        var hint = ScrollDetector.DetectWithThreshold(scene1, scene2, viewport, 0.60);

        await Assert.That(hint.IsScroll).IsTrue();
    }

    [Test]
    public async Task ThresholdAt99Percent_DoesNotDetectScroll()
    {
        var viewport = Rect.FromLTRB(0, 0, 1920, 1080);

        var scene1 = CreateScrollScene(0, 0);
        var scene2 = CreateScrollScene(0, 50);

        var hint = ScrollDetector.DetectWithThreshold(scene1, scene2, viewport, 0.99);

        await Assert.That(hint.IsScroll).IsFalse();
    }

    private static SceneBuffer CreateScrollScene(double translateX, double translateY)
    {
        var sb = SceneBuilder.Begin(4096);
        sb.BeginFrame();

        var transform = Affine.Translate(translateX, translateY);
        int transformId = sb.AddTransform(transform);

        for (int i = 0; i < 100; i++)
        {
            int paintId = sb.AddPaint(Paint.Solid(0xFF804080));
            var rect = Rect.FromLTRB(i * 10, i * 10, i * 10 + 50, i * 10 + 50);
            sb.FillRect(rect, paintId, transformId);
        }

        sb.EndFrame();
        return sb.End();
    }

    private static SceneBuffer CreateScrollSceneWithExtra(double translateX, double translateY)
    {
        var sb = SceneBuilder.Begin(4096);
        sb.BeginFrame();

        var transform = Affine.Translate(translateX, translateY);
        int transformId = sb.AddTransform(transform);

        for (int i = 0; i < 100; i++)
        {
            int paintId = sb.AddPaint(Paint.Solid(0xFF804080));
            var rect = Rect.FromLTRB(i * 10, i * 10, i * 10 + 50, i * 10 + 50);
            sb.FillRect(rect, paintId, transformId);
        }

        int extraPaintId = sb.AddPaint(Paint.Solid(0xFF804080));
        var extraRect = Rect.FromLTRB(500, 500, 600, 600);
        sb.FillRect(extraRect, extraPaintId, transformId);

        sb.EndFrame();
        return sb.End();
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
}