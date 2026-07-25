using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using Etch.Geometry;

namespace Etch.Scene;

public static class SceneValidator
{
    private const int MaxStackDepth = 32;
    private const int ReportBufferSize = 256;

    public static void ValidateStrict(SceneBuffer scene)
    {
        if (scene == null)
            Etch.Panic.Invariant(Etch.PanicCodes.ArgumentNull, "scene is null");

        var buffer = ArrayPool<SceneValidationError>.Shared.Rent(ReportBufferSize);
        try
        {
            var report = new SceneValidationReport(buffer);
            ValidateAccumulated(scene, ref report);
            if (report.Count > 0)
            {
                var firstError = report.Errors[0];
                var panicCode = ToPanicCode(firstError.ErrorCode, firstError.CommandIndex);
                Etch.Panic.Invariant(panicCode, "Scene validation failed");
            }
        }
        finally
        {
            ArrayPool<SceneValidationError>.Shared.Return(buffer);
        }
    }

    public static void ValidateAccumulated(SceneBuffer scene, ref SceneValidationReport report)
    {
#pragma warning disable CA1062
        if (scene == null)
            Etch.Panic.Invariant(Etch.PanicCodes.ArgumentNull, "scene is null");

        int layerDepth = 0;
        int clipDepth = 0;
        bool sawBegin = false;
        bool sawEnd = false;
        int beginIndex = -1;
        int endIndex = -1;

        var commands = scene.Commands;
        for (int i = 0; i < commands.Length; i++)
        {
            ref readonly var cmd = ref commands[i];

            switch (cmd.Op)
            {
                case SceneOpcode.BeginFrame:
                    if (sawBegin)
                    {
                        report.Add(Etch.PanicCodes.BadFrameMarkers, i);
                    }
                    sawBegin = true;
                    beginIndex = i;
                    break;

                case SceneOpcode.EndFrame:
                    if (endIndex != -1)
                    {
                        report.Add(Etch.PanicCodes.BadFrameMarkers, i);
                    }
                    sawEnd = true;
                    endIndex = i;
                    break;

                case SceneOpcode.PushLayer:
                    if (++layerDepth > MaxStackDepth)
                    {
                        report.Add(Etch.PanicCodes.LayerStackOverflow, i);
                    }
                    ValidateLayerPayload(cmd.PushLayer, scene, i, ref report);
                    break;

                case SceneOpcode.PopLayer:
                    if (--layerDepth < 0)
                    {
                        report.Add(Etch.PanicCodes.UnbalancedLayerStack, i);
                    }
                    break;

                case SceneOpcode.PushClip:
                    if (++clipDepth > MaxStackDepth)
                    {
                        report.Add(Etch.PanicCodes.LayerStackOverflow, i);
                    }
                    ValidateClipPayload(cmd.PushClip, scene, i, ref report);
                    break;

                case SceneOpcode.PopClip:
                    if (--clipDepth < 0)
                    {
                        report.Add(Etch.PanicCodes.UnbalancedClipStack, i);
                    }
                    break;

                case SceneOpcode.FillPath:
                    ValidateFillPathPayload(cmd.FillPath, scene, i, ref report);
                    break;

                case SceneOpcode.StrokePath:
                    ValidateStrokePathPayload(cmd.StrokePath, scene, i, ref report);
                    break;

                case SceneOpcode.SetTransform:
                    ValidateTransformPayload(cmd.SetTransform, scene, i, ref report);
                    break;

                case SceneOpcode.FillRect:
                    ValidateFillRectPayload(cmd.FillRect, scene, i, ref report);
                    break;

                case SceneOpcode.DrawImage:
                    ValidateDrawImagePayload(cmd.DrawImage, scene, i, ref report);
                    break;

                case SceneOpcode.DrawGlyphRun:
                    ValidateDrawGlyphRunPayload(cmd.DrawGlyphRun, scene, i, ref report);
                    break;

                case SceneOpcode.DrawShadow:
                    ValidateDrawShadowPayload(cmd.DrawShadow, scene, i, ref report);
                    break;
                case SceneOpcode.DrawMaterialRegion:
                    ValidateDrawMaterialRegionPayload(cmd.DrawMaterialRegion, scene, i, ref report);
                    break;
                case SceneOpcode.PushColorFilter:
                    ValidatePushColorFilterPayload(cmd.PushColorFilter, scene, i, ref report);
                    break;
                case SceneOpcode.PopColorFilter:
                    break;
            }
        }

        if (layerDepth != 0)
        {
            report.Add(Etch.PanicCodes.UnbalancedLayerStack, commands.Length);
        }

        if (clipDepth != 0)
        {
            report.Add(Etch.PanicCodes.UnbalancedClipStack, commands.Length);
        }

        if (!sawBegin)
        {
            report.Add(Etch.PanicCodes.BadFrameMarkers, 0);
        }

        if (!sawEnd)
        {
            report.Add(Etch.PanicCodes.BadFrameMarkers, commands.Length);
        }
        else if (beginIndex >= endIndex && beginIndex != -1)
        {
            report.Add(Etch.PanicCodes.BadFrameMarkers, endIndex);
        }
    }

    private static void ValidateLayerPayload(PushLayerPayload payload, SceneBuffer scene, int cmdIndex, ref SceneValidationReport report)
    {
        if (payload.Opacity < 0f || payload.Opacity > 1f)
        {
            report.Add(Etch.PanicCodes.BadLayerOpacity, cmdIndex);
        }

        if (payload.LayerId < 0 || payload.LayerId >= scene.RectCount)
        {
            report.Add(Etch.PanicCodes.InvalidResourceId, cmdIndex);
        }
    }

    private static void ValidateClipPayload(PushClipPayload payload, SceneBuffer scene, int cmdIndex, ref SceneValidationReport report)
    {
        if (payload.ClipId < 0 || payload.ClipId >= scene.PathCount)
        {
            report.Add(Etch.PanicCodes.InvalidResourceId, cmdIndex);
        }
    }

    private static void ValidateFillPathPayload(FillPathPayload payload, SceneBuffer scene, int cmdIndex, ref SceneValidationReport report)
    {
        ValidatePathReference(payload.PathId, payload.PaintId, payload.TransformId, scene, cmdIndex, ref report);

        if (payload.PathId >= 0 && payload.PathId < scene.PathCount)
        {
            if (!scene.TryGetPath(payload.PathId, out var pathData))
            {
                return;
            }
            if (pathData.Path.VerbCount < 2)
            {
                report.Add(Etch.PanicCodes.EmptyPath, cmdIndex);
            }
        }
    }

    private static void ValidateStrokePathPayload(StrokePathPayload payload, SceneBuffer scene, int cmdIndex, ref SceneValidationReport report)
    {
        ValidatePathReference(payload.PathId, payload.PaintId, payload.TransformId, scene, cmdIndex, ref report);

        if (payload.StrokeWidth <= 0f || !IsFinite(payload.StrokeWidth))
        {
            report.Add(Etch.PanicCodes.BadStrokeParam, cmdIndex);
        }

        if (payload.PathId >= 0 && payload.PathId < scene.PathCount)
        {
            if (!scene.TryGetPath(payload.PathId, out var pathData))
            {
                return;
            }
            if (pathData.Path.VerbCount < 2)
            {
                report.Add(Etch.PanicCodes.EmptyPath, cmdIndex);
            }
        }
    }

    private static void ValidateFillRectPayload(FillRectPayload payload, SceneBuffer scene, int cmdIndex, ref SceneValidationReport report)
    {
        if (payload.RectId < 0 || payload.RectId >= scene.RectCount)
        {
            report.Add(Etch.PanicCodes.InvalidResourceId, cmdIndex);
        }

        ValidatePaintAndTransform(payload.PaintId, payload.TransformId, scene, cmdIndex, ref report);
    }

    private static void ValidateTransformPayload(SetTransformPayload payload, SceneBuffer scene, int cmdIndex, ref SceneValidationReport report)
    {
        if (payload.TransformId < 0 || payload.TransformId >= scene.TransformCount)
        {
            report.Add(Etch.PanicCodes.InvalidResourceId, cmdIndex);
        }
        else
        {
            var transform = scene.GetTransform(payload.TransformId);
            ValidateAffine(transform, cmdIndex, ref report);
        }
    }

    private static void ValidateDrawImagePayload(DrawImagePayload payload, SceneBuffer scene, int cmdIndex, ref SceneValidationReport report)
    {
        ValidatePaintAndTransform(payload.PaintId, payload.TransformId, scene, cmdIndex, ref report);
    }

    private static void ValidateDrawGlyphRunPayload(DrawGlyphRunPayload payload, SceneBuffer scene, int cmdIndex, ref SceneValidationReport report)
    {
        ValidatePaintAndTransform(payload.PaintId, payload.TransformId, scene, cmdIndex, ref report);
    }

    private static void ValidateDrawShadowPayload(DrawShadowPayload payload, SceneBuffer scene, int cmdIndex, ref SceneValidationReport report)
    {
        ValidatePathReference(payload.PathId, payload.PaintId, payload.TransformId, scene, cmdIndex, ref report);
    }

    private static void ValidateDrawMaterialRegionPayload(DrawMaterialRegionPayload payload, SceneBuffer scene, int cmdIndex, ref SceneValidationReport report)
    {
        if (payload.RectId < 0 || payload.RectId >= scene.RectCount)
            report.Add(Etch.PanicCodes.InvalidResourceId, cmdIndex);
        if (payload.TransformId < 0 || payload.TransformId >= scene.TransformCount)
            report.Add(Etch.PanicCodes.InvalidResourceId, cmdIndex);
    }

    private static void ValidatePushColorFilterPayload(PushColorFilterPayload payload, SceneBuffer scene, int cmdIndex, ref SceneValidationReport report)
    {
        if (payload.ColorFilterId < 0 || payload.ColorFilterId >= scene.ColorFilterCount)
            report.Add(Etch.PanicCodes.InvalidResourceId, cmdIndex);
    }

    private static void ValidatePathReference(int pathId, int paintId, int transformId, SceneBuffer scene, int cmdIndex, ref SceneValidationReport report)
    {
        if (pathId < 0 || pathId >= scene.PathCount)
        {
            report.Add(Etch.PanicCodes.InvalidResourceId, cmdIndex);
        }

        ValidatePaintAndTransform(paintId, transformId, scene, cmdIndex, ref report);
    }

    private static void ValidatePaintAndTransform(int paintId, int transformId, SceneBuffer scene, int cmdIndex, ref SceneValidationReport report)
    {
        if (paintId < 0 || paintId >= scene.PaintCount)
        {
            report.Add(Etch.PanicCodes.InvalidResourceId, cmdIndex);
        }
        else
        {
            var paint = scene.GetPaint(paintId);
            ValidatePaint(paint, scene, cmdIndex, ref report);
        }

        if (transformId < 0 || transformId >= scene.TransformCount)
        {
            report.Add(Etch.PanicCodes.InvalidResourceId, cmdIndex);
        }
        else
        {
            var transform = scene.GetTransform(transformId);
            ValidateAffine(transform, cmdIndex, ref report);
        }
    }

    private static void ValidatePaint(Paint paint, SceneBuffer scene, int cmdIndex, ref SceneValidationReport report)
    {
        if (paint.Kind == PaintKind.LinearGradient || paint.Kind == PaintKind.RadialGradient)
        {
            int gradientId = (int)paint.GradientId;
            if (gradientId < 0 || gradientId >= scene.GradientStopsCount)
            {
                report.Add(Etch.PanicCodes.InvalidResourceId, cmdIndex);
            }
            else
            {
                var gradient = scene.GetGradientStops(gradientId);
                ValidateGradientStops(gradient, cmdIndex, ref report);
            }
        }
        else if (paint.Kind == PaintKind.MeshGradient)
        {
            int meshId = (int)paint.GradientId;
            if (meshId < 0 || meshId >= scene.MeshGradientCount)
            {
                report.Add(Etch.PanicCodes.InvalidResourceId, cmdIndex);
            }
        }
        else if (paint.Kind == PaintKind.Noise)
        {
            int noiseId = (int)paint.GradientId;
            if (noiseId < 0 || noiseId >= scene.NoiseSpecCount)
            {
                report.Add(Etch.PanicCodes.InvalidResourceId, cmdIndex);
            }
        }
    }

    private static void ValidateGradientStops(GradientStops gradient, int cmdIndex, ref SceneValidationReport report)
    {
        if (gradient.Count < 2)
        {
            report.Add(Etch.PanicCodes.BadGradient, cmdIndex);
            return;
        }

        float lastOffset = -1f;
        for (int i = 0; i < gradient.Count; i++)
        {
            var (offset, _) = gradient.GetStop(i);
            if (offset < 0f || offset > 1f)
            {
                report.Add(Etch.PanicCodes.BadGradient, cmdIndex);
                return;
            }
            if (offset < lastOffset)
            {
                report.Add(Etch.PanicCodes.BadGradient, cmdIndex);
                return;
            }
            lastOffset = offset;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ValidateAffine(Affine a, int cmdIndex, ref SceneValidationReport report)
    {
        if (!IsFinite(a.M00) || !IsFinite(a.M01) || !IsFinite(a.M02) ||
            !IsFinite(a.M10) || !IsFinite(a.M11) || !IsFinite(a.M12))
        {
            report.Add(Etch.PanicCodes.NonFiniteGeometry, cmdIndex);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static Etch.PanicCode ToPanicCode(SceneValidationErrorCode errorCode, int commandIndex)
    {
        return errorCode switch
        {
            SceneValidationErrorCode.BadFrameMarkers => Etch.PanicCodes.BadFrameMarkers,
            SceneValidationErrorCode.UnbalancedLayerStack => Etch.PanicCodes.UnbalancedLayerStack,
            SceneValidationErrorCode.LayerStackOverflow => Etch.PanicCodes.LayerStackOverflow,
            SceneValidationErrorCode.UnbalancedClipStack => Etch.PanicCodes.UnbalancedClipStack,
            SceneValidationErrorCode.InvalidResourceId => Etch.PanicCodes.InvalidResourceId,
            SceneValidationErrorCode.NonFiniteGeometry => Etch.PanicCodes.NonFiniteGeometry,
            SceneValidationErrorCode.EmptyPath => Etch.PanicCodes.EmptyPath,
            SceneValidationErrorCode.BadGradient => Etch.PanicCodes.BadGradient,
            SceneValidationErrorCode.BadLayerOpacity => Etch.PanicCodes.BadLayerOpacity,
            SceneValidationErrorCode.BadStrokeParam => Etch.PanicCodes.BadStrokeParam,
            _ => Etch.PanicCodes.BadFrameMarkers,
        };
    }
}
