using System;
using Etch.Geometry;
using Etch.Scene;
using Etch.Testing;
using TUnit;

namespace Etch.Gpu.Compositor.Tests;

internal sealed class GpuCompositorDifferentialTests
{
    [Test]
    public async Task RedCircle_CpuGpuPixelsMatchWithinTolerance()
    {
        const int width = 640;
        const int height = 480;
        var scene = BuildRedCircleScene(width, height);

        byte[] cpuPixels = SceneRunner.RunCpu(scene, width, height);
        byte[] gpuPixels = SceneRunner.RunGpu(scene, width, height);

        int mismatches = 0;
        int totalPixels = width * height;
        for (int i = 0; i < totalPixels; i++)
        {
            int offset = i * 4;
            int dr = Math.Abs(cpuPixels[offset + 0] - gpuPixels[offset + 0]);
            int dg = Math.Abs(cpuPixels[offset + 1] - gpuPixels[offset + 1]);
            int db = Math.Abs(cpuPixels[offset + 2] - gpuPixels[offset + 2]);
            int da = Math.Abs(cpuPixels[offset + 3] - gpuPixels[offset + 3]);
            if (dr > 2 || dg > 2 || db > 2 || da > 2)
            {
                mismatches++;
            }
        }

        double mismatchRatio = (double)mismatches / totalPixels;
        await Assert.That(mismatchRatio).IsLessThanOrEqualTo(0.02);
    }

    [Test]
    public async Task RedCircle_CenterPixelMatchesExactly()
    {
        const int width = 640;
        const int height = 480;
        var scene = BuildRedCircleScene(width, height);

        byte[] cpuPixels = SceneRunner.RunCpu(scene, width, height);
        byte[] gpuPixels = SceneRunner.RunGpu(scene, width, height);

        int cx = width / 2;
        int cy = height / 2;
        int idx = (cy * width + cx) * 4;

        await Assert.That(gpuPixels[idx + 0]).IsEqualTo(cpuPixels[idx + 0]);
        await Assert.That(gpuPixels[idx + 1]).IsEqualTo(cpuPixels[idx + 1]);
        await Assert.That(gpuPixels[idx + 2]).IsEqualTo(cpuPixels[idx + 2]);
        await Assert.That(gpuPixels[idx + 3]).IsEqualTo(cpuPixels[idx + 3]);
    }

    [Test]
    public async Task LinearGradientRect_GpuProducesGradientColors()
    {
        const int width = 640;
        const int height = 480;
        var scene = BuildGradientRectScene(width, height);

        byte[] gpuPixels = SceneRunner.RunGpu(scene, width, height);

        // Left edge should be red-ish, right edge should be blue-ish
        int leftIdx = (height / 2 * width + 100) * 4;
        int rightIdx = (height / 2 * width + 540) * 4;

        // Red channel should be higher on the left
        await Assert.That(gpuPixels[leftIdx + 0]).IsGreaterThan(gpuPixels[rightIdx + 0]);
        // Blue channel should be higher on the right
        await Assert.That(gpuPixels[rightIdx + 2]).IsGreaterThan(gpuPixels[leftIdx + 2]);
    }

    private static SceneBuffer BuildRedCircleScene(int w, int h)
    {
        var builder = SceneBuilder.Begin();
        builder.BeginFrame();
        int identity = builder.AddTransform(Affine.Identity);
        int paintId = builder.AddPaint(Paint.Solid(0xFFFF0000u));
        int cx = w / 2, cy = h / 2, r = 100;
        double k = 0.5522847498;
        using var pb = BezPathBuilder.Begin();
        pb.MoveTo(new Point(cx + r, cy));
        pb.CubicTo(new Point(cx + r, cy + k * r), new Point(cx + k * r, cy + r), new Point(cx, cy + r));
        pb.CubicTo(new Point(cx - k * r, cy + r), new Point(cx - r, cy + k * r), new Point(cx - r, cy));
        pb.CubicTo(new Point(cx - r, cy - k * r), new Point(cx - k * r, cy - r), new Point(cx, cy - r));
        pb.CubicTo(new Point(cx + k * r, cy - r), new Point(cx + r, cy - k * r), new Point(cx + r, cy));
        pb.Close();
        int pathId = builder.AddPath(pb.Build());
        builder.FillPath(pathId, paintId, identity, FillRule.NonZero);
        builder.EndFrame();
        return builder.End();
    }

    [Test]
    public async Task ArbitraryPathStroke_CpuGpuPixelsMatchWithinTolerance()
    {
        const int width = 640;
        const int height = 480;
        var scene = BuildStarStrokeScene(width, height);

        byte[] cpuPixels = SceneRunner.RunCpu(scene, width, height);
        byte[] gpuPixels = SceneRunner.RunGpu(scene, width, height);

        int mismatches = 0;
        int totalPixels = width * height;
        for (int i = 0; i < totalPixels; i++)
        {
            int offset = i * 4;
            int dr = Math.Abs(cpuPixels[offset + 0] - gpuPixels[offset + 0]);
            int dg = Math.Abs(cpuPixels[offset + 1] - gpuPixels[offset + 1]);
            int db = Math.Abs(cpuPixels[offset + 2] - gpuPixels[offset + 2]);
            int da = Math.Abs(cpuPixels[offset + 3] - gpuPixels[offset + 3]);
            if (dr > 2 || dg > 2 || db > 2 || da > 2)
            {
                mismatches++;
            }
        }

        double mismatchRatio = (double)mismatches / totalPixels;
        await Assert.That(mismatchRatio).IsLessThanOrEqualTo(0.02);
    }

    private static SceneBuffer BuildGradientRectScene(int w, int h)
    {
        var builder = SceneBuilder.Begin();
        builder.BeginFrame();
        int identity = builder.AddTransform(Affine.Identity);
        int stopsId = builder.AddGradientStops(GradientStops.Create(
            (0.0f, 0xFFFF0000u),
            (1.0f, 0xFF0000FFu)));
        int paintId = builder.AddPaint(Paint.LinearGradient((uint)stopsId));
        var rect = new Rect(50, 50, w - 50, h - 50);
        builder.FillRect(rect, paintId, identity);
        builder.EndFrame();
        return builder.End();
    }

    private static SceneBuffer BuildStarStrokeScene(int w, int h)
    {
        var builder = SceneBuilder.Begin();
        builder.BeginFrame();
        int identity = builder.AddTransform(Affine.Identity);
        int paintId = builder.AddPaint(Paint.Solid(0xFF00FF00u));

        double cx = w / 2.0;
        double cy = h / 2.0;
        double outerR = 120.0;
        double innerR = 50.0;
        int points = 5;

        using var pb = BezPathBuilder.Begin();
        for (int i = 0; i <= points * 2; i++)
        {
            double angle = (i * Math.PI / points) - Math.PI / 2.0;
            double r = (i % 2 == 0) ? outerR : innerR;
            double x = cx + r * Math.Cos(angle);
            double y = cy + r * Math.Sin(angle);
            if (i == 0)
            {
                pb.MoveTo(new Point(x, y));
            }
            else
            {
                pb.LineTo(new Point(x, y));
            }
        }
        pb.Close();

        int pathId = builder.AddPath(pb.Build());
        builder.StrokePath(pathId, paintId, identity, 4.0f, default);
        builder.EndFrame();
        return builder.End();
    }
}
