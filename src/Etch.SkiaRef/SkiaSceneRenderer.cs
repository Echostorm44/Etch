using System;
using Etch.Geometry;
using Etch.Scene;
using SkiaSharp;

namespace Etch.SkiaRef;

public static class SkiaSceneRenderer
{
    public static byte[] Render(SceneBuffer scene, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (width <= 0 || height <= 0)
            Etch.Panic.Invariant(Etch.PanicCodes.InvalidSurfaceSize, "Render dimensions must be positive");

        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        var commands = scene.Commands;
        using var skPaint = new SKPaint { IsAntialias = true };
        Affine currentAffine = Affine.Identity;

        for (int i = 0; i < commands.Length; i++)
        {
            ref readonly var cmd = ref commands[i];
            switch (cmd.Op)
            {
                case SceneOpcode.SetTransform:
                {
                    currentAffine = scene.GetTransform(cmd.SetTransform.TransformId);
                    break;
                }

                case SceneOpcode.FillRect:
                {
                    var rect = scene.GetRect(cmd.FillRect.RectId);
                    var paint = scene.GetPaint(cmd.FillRect.PaintId);
                    var extraXform = scene.GetTransform(cmd.FillRect.TransformId);
                    var totalAffine = extraXform != Affine.Identity
                        ? extraXform * currentAffine
                        : currentAffine;

                    ApplyPaint(skPaint, paint);
                    canvas.SetMatrix(AffineToSkMatrix(totalAffine));
                    canvas.DrawRect(new SKRect(
                        (float)rect.MinX, (float)rect.MinY,
                        (float)rect.MaxX, (float)rect.MaxY), skPaint);
                    break;
                }

                case SceneOpcode.FillPath:
                {
                    if (!scene.TryGetPath(cmd.FillPath.PathId, out var pathData))
                        break;

                    var paint = scene.GetPaint(cmd.FillPath.PaintId);
                    var extraXform = scene.GetTransform(cmd.FillPath.TransformId);
                    var totalAffine = extraXform != Affine.Identity
                        ? extraXform * currentAffine
                        : currentAffine;

                    ApplyPaint(skPaint, paint);
                    canvas.SetMatrix(AffineToSkMatrix(totalAffine));

                    using var skPath = ConvertBezPath(pathData.Path);
                    canvas.DrawPath(skPath, skPaint);
                    break;
                }

                case SceneOpcode.StrokePath:
                {
                    if (!scene.TryGetPath(cmd.StrokePath.PathId, out var pathData))
                        break;

                    var paint = scene.GetPaint(cmd.StrokePath.PaintId);
                    var extraXform = scene.GetTransform(cmd.StrokePath.TransformId);
                    var totalAffine = extraXform != Affine.Identity
                        ? extraXform * currentAffine
                        : currentAffine;

                    ApplyPaint(skPaint, paint);
                    skPaint.Style = SKPaintStyle.Stroke;
                    skPaint.StrokeWidth = cmd.StrokePath.StrokeWidth;
                    canvas.SetMatrix(AffineToSkMatrix(totalAffine));

                    using var skPath = ConvertBezPath(pathData.Path);
                    canvas.DrawPath(skPath, skPaint);
                    skPaint.Style = SKPaintStyle.Fill;
                    break;
                }
            }
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static void ApplyPaint(SKPaint skPaint, Paint paint)
    {
        uint argb = paint.Color;
        byte a = (byte)((argb >> 24) & 0xFF);
        byte r = (byte)((argb >> 16) & 0xFF);
        byte g = (byte)((argb >> 8) & 0xFF);
        byte b = (byte)(argb & 0xFF);
        skPaint.Color = new SKColor(r, g, b, a);
    }

    private static SKPath ConvertBezPath(BezPath path)
    {
        var skPath = new SKPath();
        var enumerator = path.Iterate();

        while (enumerator.MoveNext())
        {
            var segment = enumerator.Current;
            switch (segment.Verb)
            {
                case PathVerb.MoveTo:
                    skPath.MoveTo((float)segment.End.X, (float)segment.End.Y);
                    break;

                case PathVerb.LineTo:
                    skPath.LineTo((float)segment.End.X, (float)segment.End.Y);
                    break;

                case PathVerb.QuadTo:
                    skPath.QuadTo(
                        (float)segment.Control0.X, (float)segment.Control0.Y,
                        (float)segment.End.X, (float)segment.End.Y);
                    break;

                case PathVerb.CubicTo:
                    skPath.CubicTo(
                        (float)segment.Control0.X, (float)segment.Control0.Y,
                        (float)segment.Control1.X, (float)segment.Control1.Y,
                        (float)segment.End.X, (float)segment.End.Y);
                    break;

                case PathVerb.Close:
                    skPath.Close();
                    break;
            }
        }

        return skPath;
    }

    private static SKMatrix AffineToSkMatrix(Affine a)
    {
        return new SKMatrix(
            (float)a.M00, (float)a.M01, (float)a.M02,
            (float)a.M10, (float)a.M11, (float)a.M12,
            0, 0, 1);
    }
}
