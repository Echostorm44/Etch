using System;
using Etch.Geometry;
using Etch.Scene;

namespace Etch.ClipBlendGradient.Tests;

internal static class ClipFixtureScenes
{
    public const int FixtureWidth = 64;
    public const int FixtureHeight = 64;

    public static SceneBuffer NestedCircles()
    {
        var sb = SceneBuilder.Begin(32);
        sb.BeginFrame();

        var outerCircle = CreateCirclePath(32, 32, 28, 32);
        var innerCircle = CreateCirclePath(32, 32, 14, 24);
        var red = sb.AddPaint(Paint.Solid(0xFFFF0000));
        var identity = sb.AddTransform(Affine.Identity);

        int outerId = sb.AddPath(outerCircle);
        int innerId = sb.AddPath(innerCircle);

        sb.PushClip(outerId, FillRule.NonZero, ClipMode.Intersect);
        sb.PushClip(innerId, FillRule.NonZero, ClipMode.Intersect);
        sb.FillRect(new Rect(0, 0, 64, 64), red, identity);
        sb.PopClip();
        sb.PopClip();
        sb.EndFrame();
        return sb.End();
    }

    public static SceneBuffer RectMinusCircle()
    {
        var sb = SceneBuilder.Begin(32);
        sb.BeginFrame();

        var rect = CreateRectPath(4, 4, 60, 60);
        var circle = CreateCirclePath(32, 32, 16, 24);
        var red = sb.AddPaint(Paint.Solid(0xFFFF0000));
        var identity = sb.AddTransform(Affine.Identity);

        int rectId = sb.AddPath(rect);
        int circleId = sb.AddPath(circle);

        sb.PushClip(rectId, FillRule.NonZero, ClipMode.Intersect);
        sb.PushClip(circleId, FillRule.NonZero, ClipMode.Difference);
        sb.FillRect(new Rect(0, 0, 64, 64), red, identity);
        sb.PopClip();
        sb.PopClip();
        sb.EndFrame();
        return sb.End();
    }

    public static SceneBuffer SoftClippedRect()
    {
        var sb = SceneBuilder.Begin(32);
        sb.BeginFrame();

        var roundedRect = CreateRoundedRectPath(8, 8, 56, 56, 8);
        var red = sb.AddPaint(Paint.Solid(0xFFFF0000));
        var identity = sb.AddTransform(Affine.Identity);

        int clipId = sb.AddPath(roundedRect);

        sb.PushClip(clipId, FillRule.NonZero, ClipMode.Intersect);
        sb.FillRect(new Rect(0, 0, 64, 64), red, identity);
        sb.PopClip();
        sb.EndFrame();
        return sb.End();
    }

    public static SceneBuffer EightLevelNesting()
    {
        var sb = SceneBuilder.Begin(48);
        sb.BeginFrame();

        var red = sb.AddPaint(Paint.Solid(0xFFFF0000));
        var identity = sb.AddTransform(Affine.Identity);

        for (int i = 0; i < 8; i++)
        {
            double inset = i * 3 + 2;
            var rect = CreateRectPath(inset, inset, 64 - inset, 64 - inset);
            int rectId = sb.AddPath(rect);
            sb.PushClip(rectId, FillRule.NonZero, ClipMode.Intersect);
        }

        sb.FillRect(new Rect(0, 0, 64, 64), red, identity);

        for (int i = 0; i < 8; i++)
            sb.PopClip();

        sb.EndFrame();
        return sb.End();
    }

    public static SceneBuffer ClipAroundSolid()
    {
        var sb = SceneBuilder.Begin(32);
        sb.BeginFrame();

        var star = CreateStarPath(32, 32, 28, 12, 5);
        var blue = sb.AddPaint(Paint.Solid(0xFF0000FF));
        var identity = sb.AddTransform(Affine.Identity);

        int clipId = sb.AddPath(star);

        sb.PushClip(clipId, FillRule.NonZero, ClipMode.Intersect);
        sb.FillRect(new Rect(0, 0, 64, 64), blue, identity);
        sb.PopClip();
        sb.EndFrame();
        return sb.End();
    }

    public static SceneBuffer OverlappingClips()
    {
        var sb = SceneBuilder.Begin(32);
        sb.BeginFrame();

        var leftCircle = CreateCirclePath(20, 32, 16, 24);
        var rightCircle = CreateCirclePath(44, 32, 16, 24);
        var red = sb.AddPaint(Paint.Solid(0xFFFF0000));
        var blue = sb.AddPaint(Paint.Solid(0xFF0000FF));
        var identity = sb.AddTransform(Affine.Identity);

        int leftId = sb.AddPath(leftCircle);
        int rightId = sb.AddPath(rightCircle);

        sb.PushClip(leftId, FillRule.NonZero, ClipMode.Intersect);
        sb.FillRect(new Rect(0, 0, 64, 64), red, identity);
        sb.PopClip();

        sb.PushClip(rightId, FillRule.NonZero, ClipMode.Intersect);
        sb.FillRect(new Rect(0, 0, 64, 64), blue, identity);
        sb.PopClip();

        sb.EndFrame();
        return sb.End();
    }

    public static SceneBuffer ClipThenTranslate()
    {
        var sb = SceneBuilder.Begin(32);
        sb.BeginFrame();

        var rect = CreateRectPath(8, 8, 24, 24);
        var red = sb.AddPaint(Paint.Solid(0xFFFF0000));
        var translate = sb.AddTransform(Affine.Translate(16.0, 16.0));
        var identity = sb.AddTransform(Affine.Identity);

        int clipId = sb.AddPath(rect);

        sb.PushClip(clipId, FillRule.NonZero, ClipMode.Intersect);
        sb.SetTransform(translate);
        sb.FillRect(new Rect(0, 0, 16, 16), red, identity);
        sb.PopClip();
        sb.EndFrame();
        return sb.End();
    }

    public static SceneBuffer ClipRotate()
    {
        var sb = SceneBuilder.Begin(32);
        sb.BeginFrame();

        var rect = CreateRectPath(20, 12, 44, 52);
        var red = sb.AddPaint(Paint.Solid(0xFFFF0000));
        var rotate = sb.AddTransform(Affine.Translate(32, 32) * Affine.Rotate(Math.PI / 6.0) * Affine.Translate(-32, -32));
        var identity = sb.AddTransform(Affine.Identity);

        int clipId = sb.AddPath(rect);

        sb.PushClip(clipId, FillRule.NonZero, ClipMode.Intersect);
        sb.SetTransform(rotate);
        sb.FillRect(new Rect(16, 16, 48, 48), red, identity);
        sb.PopClip();
        sb.EndFrame();
        return sb.End();
    }

    public static SceneBuffer ClipScale()
    {
        var sb = SceneBuilder.Begin(32);
        sb.BeginFrame();

        var rect = CreateRectPath(16, 16, 48, 48);
        var red = sb.AddPaint(Paint.Solid(0xFFFF0000));
        var scale = sb.AddTransform(Affine.Translate(32, 32) * Affine.Scale(0.5, 0.5) * Affine.Translate(-32, -32));
        var identity = sb.AddTransform(Affine.Identity);

        int clipId = sb.AddPath(rect);

        sb.PushClip(clipId, FillRule.NonZero, ClipMode.Intersect);
        sb.SetTransform(scale);
        sb.FillRect(new Rect(0, 0, 128, 128), red, identity);
        sb.PopClip();
        sb.EndFrame();
        return sb.End();
    }

    public static SceneBuffer NonConvexClip()
    {
        var sb = SceneBuilder.Begin(32);
        sb.BeginFrame();

        var star = CreateStarPath(32, 32, 30, 10, 6);
        var green = sb.AddPaint(Paint.Solid(0xFF00FF00));
        var identity = sb.AddTransform(Affine.Identity);

        int clipId = sb.AddPath(star);

        sb.PushClip(clipId, FillRule.NonZero, ClipMode.Intersect);
        sb.FillRect(new Rect(0, 0, 64, 64), green, identity);
        sb.PopClip();
        sb.EndFrame();
        return sb.End();
    }

    private static BezPath CreateRectPath(double x0, double y0, double x1, double y1)
    {
        using var b = BezPathBuilder.Begin();
        b.MoveTo(new Point(x0, y0));
        b.LineTo(new Point(x1, y0));
        b.LineTo(new Point(x1, y1));
        b.LineTo(new Point(x0, y1));
        b.Close();
        return b.Build();
    }

    private static BezPath CreateRoundedRectPath(double x0, double y0, double x1, double y1, double r)
    {
        using var b = BezPathBuilder.Begin();
        b.MoveTo(new Point(x0 + r, y0));
        b.LineTo(new Point(x1 - r, y0));
        b.QuadTo(new Point(x1, y0), new Point(x1, y0 + r));
        b.LineTo(new Point(x1, y1 - r));
        b.QuadTo(new Point(x1, y1), new Point(x1 - r, y1));
        b.LineTo(new Point(x0 + r, y1));
        b.QuadTo(new Point(x0, y1), new Point(x0, y1 - r));
        b.LineTo(new Point(x0, y0 + r));
        b.QuadTo(new Point(x0, y0), new Point(x0 + r, y0));
        b.Close();
        return b.Build();
    }

    private static BezPath CreateCirclePath(double cx, double cy, double radius, int segments)
    {
        using var b = BezPathBuilder.Begin();
        for (int i = 0; i < segments; i++)
        {
            double a0 = 2 * Math.PI * i / segments;
            double a1 = 2 * Math.PI * (i + 1) / segments;
            double x0 = cx + radius * Math.Cos(a0);
            double y0 = cy + radius * Math.Sin(a0);
            double x1 = cx + radius * Math.Cos(a1);
            double y1 = cy + radius * Math.Sin(a1);

            if (i == 0)
                b.MoveTo(new Point(x0, y0));

            double mx = cx + radius * Math.Cos((a0 + a1) / 2);
            double my = cy + radius * Math.Sin((a0 + a1) / 2);
            double cpx = 2 * mx - (x0 + x1) / 2;
            double cpy = 2 * my - (y0 + y1) / 2;

            b.QuadTo(new Point(cpx, cpy), new Point(x1, y1));
        }
        b.Close();
        return b.Build();
    }

    private static BezPath CreateStarPath(double cx, double cy, double outerR, double innerR, int points)
    {
        using var b = BezPathBuilder.Begin();
        int total = points * 2;
        for (int i = 0; i < total; i++)
        {
            double angle = Math.PI / 2 + 2 * Math.PI * i / total;
            double r = (i % 2 == 0) ? outerR : innerR;
            double x = cx + r * Math.Cos(angle);
            double y = cy - r * Math.Sin(angle);
            if (i == 0)
                b.MoveTo(new Point(x, y));
            else
                b.LineTo(new Point(x, y));
        }
        b.Close();
        return b.Build();
    }
}
