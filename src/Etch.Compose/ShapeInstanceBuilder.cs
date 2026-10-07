using Etch.Geometry;
using Etch.Scene;
using EGeometry = Etch.Geometry;

namespace Etch.Compose;

/// <summary>
/// Turns a <see cref="SceneBuffer"/> into the analytic <see cref="ShapeInstance"/>s both composers
/// execute: transforms are applied, clip rects intersected, and each draw recognised as one of the
/// shape types the instance shader evaluates.
/// </summary>
public static class ShapeInstanceBuilder
{
    // Antialiasing margin the quad is grown by around SDF shapes; must stay in step with the
    // adaptive_aa clamp in the shape shader.
    private const float SdfAntialiasBand = 1.5f;

    /// <summary>
    /// Builds the scene's shape instances. When <paramref name="sceneMarks"/> is given (scene
    /// command index before each host command),
    /// also fills <paramref name="instanceAt"/> with the number of instances emitted before each
    /// backend command (plus one entry for the end) — the paint-order positions glyph runs and
    /// images are drawn at. Without marks <paramref name="instanceAt"/> is left empty.
    /// </summary>
    public static void Build(SceneBuffer scene, List<ShapeInstance> instances,
        IReadOnlyList<int>? sceneMarks, List<int> instanceAt)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(instanceAt);
        instances.Clear();
        instanceAt.Clear();
        int markIndex = 0;
        int markCount = sceneMarks?.Count ?? 0;
        Affine cur = Affine.Identity;
        var clipStack = new Stack<EGeometry.Rect>();

        EGeometry.Rect GetClip()
        {
            if (clipStack.Count == 0)
            {
                return EGeometry.Rect.Empty;
            }
            var result = clipStack.Peek();
            foreach (var r in clipStack)
            {
                result = result.Intersect(r);
                if (result.IsEmpty)
                {
                    break;
                }
            }
            return result;
        }

        for (int i = 0; i < scene.Commands.Length; i++)
        {
            while (markIndex < markCount && sceneMarks![markIndex] <= i)
            {
                instanceAt.Add(instances.Count);
                markIndex++;
            }

            ref readonly var cmd = ref scene.Commands[i];
            switch (cmd.Op)
            {
                case SceneOpcode.SetTransform:
                    cur = scene.GetTransform(cmd.SetTransform.TransformId);
                    break;

                case SceneOpcode.PushClip:
                    {
                        if (scene.TryGetPath(cmd.PushClip.ClipId, out var pathData))
                        {
                            var aabb = pathData.Path.Aabb().Transform(cur);
                            if (!aabb.IsEmpty)
                            {
                                clipStack.Push(aabb);
                            }
                        }
                        break;
                    }

                case SceneOpcode.PopClip:
                    if (clipStack.Count > 0)
                    {
                        clipStack.Pop();
                    }
                    break;

                case SceneOpcode.FillRect:
                    {
                        var paint = scene.GetPaint(cmd.FillRect.PaintId);
                        var rect = scene.GetRect(cmd.FillRect.RectId);
                        var xf = cur * scene.GetTransform(cmd.FillRect.TransformId);
                        var deviceRect = rect.Transform(xf);
                        if (deviceRect.IsEmpty)
                        {
                            break;
                        }

                        var clip = GetClip();
                        if (clipStack.Count > 0)
                        {
                            if (clip.IsEmpty)
                            {
                                break;
                            }
                            deviceRect = deviceRect.Intersect(clip);
                            if (deviceRect.IsEmpty)
                            {
                                break;
                            }
                        }

                        if (paint.Kind == PaintKind.Solid)
                        {
                            instances.Add(BuildSolidInstance(deviceRect, paint.Color, 0));
                        }
                        else if (paint.Kind == PaintKind.LinearGradient || paint.Kind == PaintKind.RadialGradient)
                        {
                            var stops = scene.GetGradientStops((int)paint.GradientId);
                            if (stops.Count >= 2)
                            {
                                var (stop0, color0) = stops.GetStop(0);
                                var (stop1, color1) = stops.GetStop(stops.Count - 1);
                                instances.Add(BuildGradientInstance(deviceRect, paint.Kind, stop0, color0, stop1, color1));
                            }
                            else if (stops.Count == 1)
                            {
                                var (stop0, color0) = stops.GetStop(0);
                                instances.Add(BuildSolidInstance(deviceRect, color0, 0));
                            }
                        }
                        break;
                    }

                case SceneOpcode.FillPath:
                    {
                        var paint = scene.GetPaint(cmd.FillPath.PaintId);
                        if (paint.Kind != PaintKind.Solid && paint.Kind != PaintKind.LinearGradient && paint.Kind != PaintKind.RadialGradient)
                        {
                            break;
                        }
                        if (!scene.TryGetPath(cmd.FillPath.PathId, out var pathData))
                        {
                            break;
                        }
                        var xf = cur * scene.GetTransform(cmd.FillPath.TransformId);
                        float fillScale = (float)Math.Sqrt(xf.M00 * xf.M00 + xf.M10 * xf.M10);
                        var (isCircle, center, radius) = TryDetectCircle(pathData.Path);
                        if (isCircle)
                        {
                            var tc = xf.Transform(center);
                            float r = (float)radius * fillScale;
                            var bbox = new EGeometry.Rect(tc.X - r, tc.Y - r, tc.X + r, tc.Y + r);
                            var clip = GetClip();
                            if (clipStack.Count > 0)
                            {
                                if (clip.IsEmpty)
                                {
                                    break;
                                }
                                bbox = bbox.Intersect(clip);
                                if (bbox.IsEmpty)
                                {
                                    break;
                                }
                            }
                            if (paint.Kind == PaintKind.Solid)
                            {
                                instances.Add(BuildCircleInstance(bbox, tc, r, paint.Color));
                            }
                            else
                            {
                                var stops = scene.GetGradientStops((int)paint.GradientId);
                                if (stops.Count >= 2)
                                {
                                    var (stop0, color0) = stops.GetStop(0);
                                    var (stop1, color1) = stops.GetStop(stops.Count - 1);
                                    instances.Add(BuildGradientInstance(bbox, paint.Kind, stop0, color0, stop1, color1));
                                }
                                else if (stops.Count == 1)
                                {
                                    var (stop0, color0) = stops.GetStop(0);
                                    instances.Add(BuildSolidInstance(bbox, color0, 0));
                                }
                            }
                        }
                        else if (TryDetectRoundedRect(pathData.Path, out var rrRect, out var rrRadius))
                        {
                            var deviceRect = rrRect.Transform(xf);
                            if (!deviceRect.IsEmpty)
                            {
                                var clip = GetClip();
                                if (clipStack.Count > 0)
                                {
                                    if (clip.IsEmpty)
                                    {
                                        break;
                                    }
                                    deviceRect = deviceRect.Intersect(clip);
                                    if (deviceRect.IsEmpty)
                                    {
                                        break;
                                    }
                                }
                                float physicalRadius = rrRadius * fillScale;
                                if (paint.Kind == PaintKind.Solid)
                                {
                                    instances.Add(BuildRoundedRectFillInstance(deviceRect, physicalRadius, paint.Color));
                                }
                                else
                                {
                                    var stops = scene.GetGradientStops((int)paint.GradientId);
                                    if (stops.Count >= 2)
                                    {
                                        var (stop0, color0) = stops.GetStop(0);
                                        var (stop1, color1) = stops.GetStop(stops.Count - 1);
                                        instances.Add(BuildRoundedRectGradientInstance(deviceRect, physicalRadius, paint.Kind, stop0, color0, stop1, color1));
                                    }
                                    else if (stops.Count == 1)
                                    {
                                        var (stop0, color0) = stops.GetStop(0);
                                        instances.Add(BuildRoundedRectFillInstance(deviceRect, physicalRadius, color0));
                                    }
                                }
                            }
                        }
                        else
                        {
                            // Arbitrary paths (non-circle, non-rounded-rect): AABB fallback.
                            var aabb = pathData.Path.Aabb();
                            if (!aabb.IsEmpty)
                            {
                                var deviceRect = aabb.Transform(xf);
                                if (!deviceRect.IsEmpty)
                                {
                                    var clip = GetClip();
                                    if (clipStack.Count > 0)
                                    {
                                        if (clip.IsEmpty)
                                        {
                                            break;
                                        }
                                        deviceRect = deviceRect.Intersect(clip);
                                        if (deviceRect.IsEmpty)
                                        {
                                            break;
                                        }
                                    }
                                    if (paint.Kind == PaintKind.Solid)
                                    {
                                        instances.Add(BuildSolidInstance(deviceRect, paint.Color, 0));
                                    }
                                    else
                                    {
                                        var stops = scene.GetGradientStops((int)paint.GradientId);
                                        if (stops.Count >= 2)
                                        {
                                            var (stop0, color0) = stops.GetStop(0);
                                            var (stop1, color1) = stops.GetStop(stops.Count - 1);
                                            instances.Add(BuildGradientInstance(deviceRect, paint.Kind, stop0, color0, stop1, color1));
                                        }
                                        else if (stops.Count == 1)
                                        {
                                            var (stop0, color0) = stops.GetStop(0);
                                            instances.Add(BuildSolidInstance(deviceRect, color0, 0));
                                        }
                                    }
                                }
                            }
                        }
                        break;
                    }

                case SceneOpcode.DrawShadow:
                    {
                        if (!scene.TryGetPath(cmd.DrawShadow.PathId, out var shadowPath)
                            || !ShadowShape.TryResolve(shadowPath.Path, out var shadowRect, out double shadowCorner))
                        {
                            break;
                        }
                        var xf = cur * scene.GetTransform(cmd.DrawShadow.TransformId);
                        float scale = (float)Math.Sqrt(xf.M00 * xf.M00 + xf.M10 * xf.M10);
                        var shifted = new EGeometry.Rect(
                            shadowRect.MinX + cmd.DrawShadow.ShadowOffsetX, shadowRect.MinY + cmd.DrawShadow.ShadowOffsetY,
                            shadowRect.MaxX + cmd.DrawShadow.ShadowOffsetX, shadowRect.MaxY + cmd.DrawShadow.ShadowOffsetY);
                        var device = shifted.Transform(xf);
                        if (device.IsEmpty)
                        {
                            break;
                        }
                        float sigma = (float)ShadowShape.EffectiveSigma(cmd.DrawShadow.BlurRadius * scale);
                        float extent = (float)ShadowShape.Extent(sigma);
                        var quad = new EGeometry.Rect(device.MinX - extent, device.MinY - extent, device.MaxX + extent, device.MaxY + extent);
                        var clip = GetClip();
                        if (clipStack.Count > 0)
                        {
                            if (clip.IsEmpty)
                            {
                                break;
                            }
                            quad = quad.Intersect(clip);
                            if (quad.IsEmpty)
                            {
                                break;
                            }
                        }
                        instances.Add(BuildShadowInstance(quad, device, (float)(shadowCorner * scale), sigma, cmd.DrawShadow.ShadowColor));
                        break;
                    }

                case SceneOpcode.FillSector:
                    {
                        var paint = scene.GetPaint(cmd.FillSector.PaintId);
                        if (paint.Kind != PaintKind.Solid)
                        {
                            break;
                        }
                        var xf = cur * scene.GetTransform(cmd.FillSector.TransformId);
                        float cx = (float)(cmd.FillSector.CenterX * xf.M00 + cmd.FillSector.CenterY * xf.M01 + xf.M02);
                        float cy = (float)(cmd.FillSector.CenterX * xf.M10 + cmd.FillSector.CenterY * xf.M11 + xf.M12);
                        float scale = (float)Math.Sqrt(xf.M00 * xf.M00 + xf.M10 * xf.M10);
                        float outerR = cmd.FillSector.OuterRadius * scale;
                        float innerR = cmd.FillSector.InnerRadius * scale;
                        var clip = GetClip();
                        if (clipStack.Count > 0 && clip.IsEmpty)
                        {
                            break;
                        }
                        instances.Add(BuildSectorInstance(cx, cy, outerR, innerR,
                            cmd.FillSector.StartRad, cmd.FillSector.SweepRad, paint.Color, clip));
                        break;
                    }

                case SceneOpcode.StrokePath:
                    {
                        var paint = scene.GetPaint(cmd.StrokePath.PaintId);
                        if (paint.Kind != PaintKind.Solid)
                        {
                            break;
                        }
                        if (!scene.TryGetPath(cmd.StrokePath.PathId, out var pathData))
                        {
                            break;
                        }
                        var xf = cur * scene.GetTransform(cmd.StrokePath.TransformId);
                        float scale = (float)Math.Sqrt(xf.M00 * xf.M00 + xf.M10 * xf.M10);
                        float strokeWidth = cmd.StrokePath.StrokeWidth * scale;
                        var clip = GetClip();

                        // Try circle first
                        var (isCircle, center, radius) = TryDetectCircle(pathData.Path);
                        if (isCircle)
                        {
                            var tc = xf.Transform(center);
                            float r = (float)radius * scale;
                            float halfSw = strokeWidth * 0.5f;
                            var bbox = new EGeometry.Rect(tc.X - r - halfSw, tc.Y - r - halfSw,
                                tc.X + r + halfSw, tc.Y + r + halfSw);
                            if (clipStack.Count > 0)
                            {
                                if (clip.IsEmpty)
                                {
                                    break;
                                }
                                bbox = bbox.Intersect(clip);
                                if (bbox.IsEmpty)
                                {
                                    break;
                                }
                            }
                            instances.Add(BuildRingInstance(bbox, tc, r + halfSw, strokeWidth, paint.Color));
                            break;
                        }

                        // Try line next
                        if (TryDetectLine(pathData.Path, out var lp0, out var lp1))
                        {
                            var t0 = xf.Transform(lp0);
                            var t1 = xf.Transform(lp1);
                            float halfSw = strokeWidth * 0.5f;
                            var minX = Math.Min(t0.X, t1.X) - halfSw;
                            var minY = Math.Min(t0.Y, t1.Y) - halfSw;
                            var maxX = Math.Max(t0.X, t1.X) + halfSw;
                            var maxY = Math.Max(t0.Y, t1.Y) + halfSw;
                            var bbox = new EGeometry.Rect(minX, minY, maxX, maxY);
                            if (clipStack.Count > 0)
                            {
                                if (clip.IsEmpty)
                                {
                                    break;
                                }
                                bbox = bbox.Intersect(clip);
                                if (bbox.IsEmpty)
                                {
                                    break;
                                }
                            }
                            instances.Add(BuildLineStrokeInstance(bbox, t0, t1, strokeWidth, paint.Color));
                            break;
                        }

                        // Try arc (stroked circular arc — render as true sector instead of faceted polyline)
                        if (TryDetectArc(pathData.Path, out var arcCenter, out var arcRadius, out var arcStartRad, out var arcSweepRad))
                        {
                            // Arc fast path only works for uniform scale + translation (no rotation or shear).
                            // If the transform contains rotation, fall through to the polyline path which
                            // correctly transforms each vertex.
                            if (Math.Abs(xf.M01) < 0.0001f && Math.Abs(xf.M10) < 0.0001f)
                            {
                                var tc = xf.Transform(arcCenter);
                                float r = (float)(arcRadius * scale);
                                float halfSw = strokeWidth * 0.5f;
                                var bbox = new EGeometry.Rect(tc.X - r - halfSw, tc.Y - r - halfSw,
                                    tc.X + r + halfSw, tc.Y + r + halfSw);
                                if (clipStack.Count > 0)
                                {
                                    if (clip.IsEmpty)
                                    {
                                        break;
                                    }
                                    bbox = bbox.Intersect(clip);
                                    if (bbox.IsEmpty)
                                    {
                                        break;
                                    }
                                }
                                instances.Add(BuildSectorInstance((float)tc.X, (float)tc.Y, r + halfSw, Math.Max(0f, r - halfSw),
                                    (float)arcStartRad, (float)arcSweepRad, paint.Color, clip));
                                break;
                            }
                        }

                        // Try polyline (multi-segment line, e.g. checkmark)
                        if (TryDetectPolyline(pathData.Path, out var polyPoints))
                        {
                            bool anyVisible = false;
                            for (int segIdx = 0; segIdx < polyPoints.Count - 1; segIdx++)
                            {
                                var t0 = xf.Transform(polyPoints[segIdx]);
                                var t1 = xf.Transform(polyPoints[segIdx + 1]);
                                float halfSw = strokeWidth * 0.5f;
                                var minX = Math.Min(t0.X, t1.X) - halfSw;
                                var minY = Math.Min(t0.Y, t1.Y) - halfSw;
                                var maxX = Math.Max(t0.X, t1.X) + halfSw;
                                var maxY = Math.Max(t0.Y, t1.Y) + halfSw;
                                var bbox = new EGeometry.Rect(minX, minY, maxX, maxY);
                                if (clipStack.Count > 0)
                                {
                                    if (clip.IsEmpty)
                                    {
                                        continue;
                                    }
                                    bbox = bbox.Intersect(clip);
                                    if (bbox.IsEmpty)
                                    {
                                        continue;
                                    }
                                }
                                instances.Add(BuildLineStrokeInstance(bbox, t0, t1, strokeWidth, paint.Color));
                                anyVisible = true;
                            }
                            if (anyVisible)
                            {
                                break;
                            }
                        }

                        // Try rect
                        if (TryDetectRect(pathData.Path, out var rect))
                        {
                            var deviceRect = rect.Transform(xf);
                            if (clipStack.Count > 0)
                            {
                                if (clip.IsEmpty)
                                {
                                    break;
                                }
                                deviceRect = deviceRect.Intersect(clip);
                                if (deviceRect.IsEmpty)
                                {
                                    break;
                                }
                            }
                            AddRectStrokeInstances(deviceRect, strokeWidth, paint.Color, instances);
                            break;
                        }

                        // Try rounded rect
                        if (TryDetectRoundedRect(pathData.Path, out var rrRect, out var rrRadius))
                        {
                            var deviceRect = rrRect.Transform(xf);
                            if (clipStack.Count > 0)
                            {
                                if (clip.IsEmpty)
                                {
                                    break;
                                }
                                deviceRect = deviceRect.Intersect(clip);
                                if (deviceRect.IsEmpty)
                                {
                                    break;
                                }
                            }
                            float physicalRRRadius = rrRadius * scale;
                            instances.Add(BuildRoundedRectStrokeInstance(deviceRect, physicalRRRadius, strokeWidth, paint.Color));
                            break;
                        }

                        // Complex path strokes are handled by the strip-coverage pipeline
                        break;
                    }

            }
        }

        while (markIndex < markCount)
        {
            instanceAt.Add(instances.Count);
            markIndex++;
        }
    }

    // Paint colours are sRGB-encoded (PaintColor); the shaders blend into the sRGB swapchain in
    // linear light, so instances carry linear channels.
    private static ShapeInstance BuildSolidInstance(EGeometry.Rect rect, uint argb, uint shapeType)
    {
        var (r, g, b, a) = PaintColor.ToLinear(argb);
        return new ShapeInstance
        {
            MinX = (float)rect.MinX,
            MinY = (float)rect.MinY,
            MaxX = (float)rect.MaxX,
            MaxY = (float)rect.MaxY,
            R0 = r,
            G0 = g,
            B0 = b,
            A0 = a,
            ShapeType = shapeType,
        };
    }

    private static ShapeInstance BuildCircleInstance(EGeometry.Rect bbox, EGeometry.Point center, float radius, uint argb)
    {
        var inst = BuildSolidInstance(bbox, argb, 1);
        inst.CenterX = (float)center.X;
        inst.CenterY = (float)center.Y;
        inst.Radius = radius;
        inst.Expand = SdfAntialiasBand;
        return inst;
    }

    private static ShapeInstance BuildGradientInstance(EGeometry.Rect rect, PaintKind kind, float stop0, uint color0, float stop1, uint color1)
    {
        var (r0, g0, b0, a0) = PaintColor.ToLinear(color0);
        var (r1, g1, b1, a1) = PaintColor.ToLinear(color1);

        float x0 = (float)rect.MinX;
        float y0 = (float)rect.MinY;
        float x1 = (float)rect.MaxX;
        float y1 = (float)rect.MaxY;

        var inst = new ShapeInstance
        {
            MinX = x0,
            MinY = y0,
            MaxX = x1,
            MaxY = y1,
            R0 = r0,
            G0 = g0,
            B0 = b0,
            A0 = a0,
            R1 = r1,
            G1 = g1,
            B1 = b1,
            A1 = a1,
            ShapeType = 2,
        };

        if (kind == PaintKind.LinearGradient)
        {
            inst.P0X = x0;
            inst.P0Y = y0;
            inst.P1X = x1;
            inst.P1Y = y0;
        }
        else
        {
            // Radial — approximate as linear for now
            float cx = (x0 + x1) * 0.5f;
            float cy = (y0 + y1) * 0.5f;
            inst.P0X = cx;
            inst.P0Y = cy;
            inst.P1X = x1;
            inst.P1Y = cy;
        }

        return inst;
    }

    private static ShapeInstance BuildRingInstance(EGeometry.Rect bbox, EGeometry.Point center, float outerRadius, float strokeWidth, uint argb)
    {
        var inst = BuildSolidInstance(bbox, argb, 3);
        inst.CenterX = (float)center.X;
        inst.CenterY = (float)center.Y;
        inst.Radius = outerRadius;
        inst.StrokeWidth = strokeWidth;
        inst.Expand = strokeWidth * 0.5f + SdfAntialiasBand;
        return inst;
    }

    private static ShapeInstance BuildLineStrokeInstance(EGeometry.Rect bbox, EGeometry.Point p0, EGeometry.Point p1, float strokeWidth, uint argb)
    {
        var inst = BuildSolidInstance(bbox, argb, 4);
        inst.P0X = (float)p0.X;
        inst.P0Y = (float)p0.Y;
        inst.P1X = (float)p1.X;
        inst.P1Y = (float)p1.Y;
        inst.StrokeWidth = strokeWidth;
        inst.Expand = strokeWidth * 0.5f + SdfAntialiasBand;
        return inst;
    }

    // Shape 9: the quad is the (clipped) 3σ-grown extent; the shader evaluates the blur of the
    // shadow rect in P0..P1, so clipping the quad never reshapes the shadow.
    private static ShapeInstance BuildShadowInstance(EGeometry.Rect quad, EGeometry.Rect shadow, float corner, float sigma, uint argb)
    {
        var inst = BuildSolidInstance(quad, argb, 9);
        inst.P0X = (float)shadow.MinX;
        inst.P0Y = (float)shadow.MinY;
        inst.P1X = (float)shadow.MaxX;
        inst.P1Y = (float)shadow.MaxY;
        inst.Radius = corner;
        inst.StrokeWidth = sigma;
        return inst;
    }

    private static ShapeInstance BuildRoundedRectFillInstance(EGeometry.Rect rect, float radius, uint argb)
    {
        var inst = BuildSolidInstance(rect, argb, 5);
        inst.Radius = radius;
        inst.Expand = SdfAntialiasBand;
        return inst;
    }

    private static ShapeInstance BuildRoundedRectGradientInstance(EGeometry.Rect rect, float radius, PaintKind kind, float stop0, uint color0, float stop1, uint color1)
    {
        var inst = BuildGradientInstance(rect, kind, stop0, color0, stop1, color1);
        inst.ShapeType = 7;
        inst.Radius = radius;
        inst.Expand = SdfAntialiasBand;
        return inst;
    }

    private static ShapeInstance BuildRoundedRectStrokeInstance(EGeometry.Rect rect, float radius, float strokeWidth, uint argb)
    {
        var inst = BuildSolidInstance(rect, argb, 6);
        inst.Radius = radius;
        inst.StrokeWidth = strokeWidth;
        inst.Expand = strokeWidth * 0.5f + SdfAntialiasBand;
        return inst;
    }

    private static ShapeInstance BuildSectorInstance(
        float cx, float cy, float outerRadius, float innerRadius,
        float startRad, float sweepRad, uint argb, EGeometry.Rect clip)
    {
        // Compute bounding box of the sector
        float endRad = startRad + sweepRad;
        float minX = cx, minY = cy, maxX = cx, maxY = cy;

        // Check outer arc endpoints
        float ox1 = cx + outerRadius * MathF.Cos(startRad);
        float oy1 = cy + outerRadius * MathF.Sin(startRad);
        float ox2 = cx + outerRadius * MathF.Cos(endRad);
        float oy2 = cy + outerRadius * MathF.Sin(endRad);
        minX = MathF.Min(minX, MathF.Min(ox1, ox2));
        minY = MathF.Min(minY, MathF.Min(oy1, oy2));
        maxX = MathF.Max(maxX, MathF.Max(ox1, ox2));
        maxY = MathF.Max(maxY, MathF.Max(oy1, oy2));

        // Check inner arc endpoints
        float ix1 = cx + innerRadius * MathF.Cos(startRad);
        float iy1 = cy + innerRadius * MathF.Sin(startRad);
        float ix2 = cx + innerRadius * MathF.Cos(endRad);
        float iy2 = cy + innerRadius * MathF.Sin(endRad);
        minX = MathF.Min(minX, MathF.Min(ix1, ix2));
        minY = MathF.Min(minY, MathF.Min(iy1, iy2));
        maxX = MathF.Max(maxX, MathF.Max(ix1, ix2));
        maxY = MathF.Max(maxY, MathF.Max(iy1, iy2));

        // Check cardinal directions on outer arc
        float step = MathF.Sign(sweepRad) * MathF.PI / 2f;
        float checkAngle = MathF.Floor(startRad / step) * step + step;
        if (sweepRad > 0)
        {
            while (checkAngle < endRad)
            {
                float x = cx + outerRadius * MathF.Cos(checkAngle);
                float y = cy + outerRadius * MathF.Sin(checkAngle);
                minX = MathF.Min(minX, x);
                minY = MathF.Min(minY, y);
                maxX = MathF.Max(maxX, x);
                maxY = MathF.Max(maxY, y);
                checkAngle += step;
            }
        }
        else
        {
            while (checkAngle > endRad)
            {
                float x = cx + outerRadius * MathF.Cos(checkAngle);
                float y = cy + outerRadius * MathF.Sin(checkAngle);
                minX = MathF.Min(minX, x);
                minY = MathF.Min(minY, y);
                maxX = MathF.Max(maxX, x);
                maxY = MathF.Max(maxY, y);
                checkAngle += step;
            }
        }

        if (!clip.IsEmpty)
        {
            minX = MathF.Max(minX, (float)clip.MinX);
            minY = MathF.Max(minY, (float)clip.MinY);
            maxX = MathF.Min(maxX, (float)clip.MaxX);
            maxY = MathF.Min(maxY, (float)clip.MaxY);
        }
        if (minX >= maxX || minY >= maxY)
        {
            return default;
        }

        var inst = BuildSolidInstance(new EGeometry.Rect(minX, minY, maxX, maxY), argb, 8);
        inst.CenterX = cx;
        inst.CenterY = cy;
        inst.Radius = outerRadius;
        inst.StrokeWidth = innerRadius;
        inst.P0X = startRad;
        inst.P0Y = sweepRad;
        inst.Expand = SdfAntialiasBand;
        return inst;
    }

    private static void AddRectStrokeInstances(EGeometry.Rect rect, float strokeWidth, uint argb, List<ShapeInstance> instances)
    {
        float x0 = (float)rect.MinX, y0 = (float)rect.MinY;
        float x1 = (float)rect.MaxX, y1 = (float)rect.MaxY;
        float sw = strokeWidth;

        // Top edge
        instances.Add(BuildSolidInstance(new EGeometry.Rect(x0, y0, x1, y0 + sw), argb, 0));
        // Bottom edge
        instances.Add(BuildSolidInstance(new EGeometry.Rect(x0, y1 - sw, x1, y1), argb, 0));
        // Left edge
        instances.Add(BuildSolidInstance(new EGeometry.Rect(x0, y0, x0 + sw, y1), argb, 0));
        // Right edge
        instances.Add(BuildSolidInstance(new EGeometry.Rect(x1 - sw, y0, x1, y1), argb, 0));
    }

    private static bool TryDetectLine(BezPath path, out EGeometry.Point p0, out EGeometry.Point p1)
    {
        p0 = default;
        p1 = default;
        var enumerator = path.Iterate();
        bool hasMove = false, hasLine = false;

        while (enumerator.MoveNext())
        {
            var seg = enumerator.Current;
            switch (seg.Verb)
            {
                case PathVerb.MoveTo:
                    if (hasMove)
                    {
                        return false;
                    }
                    hasMove = true;
                    p0 = seg.End;
                    break;
                case PathVerb.LineTo:
                    if (!hasMove || hasLine)
                    {
                        return false;
                    }
                    hasLine = true;
                    p1 = seg.End;
                    break;
                case PathVerb.Close:
                    return false;
                default:
                    return false;
            }
        }

        return hasMove && hasLine;
    }

    private static bool TryDetectPolyline(BezPath path, out List<EGeometry.Point> points)
    {
        points = new List<EGeometry.Point>();
        var enumerator = path.Iterate();
        bool hasMove = false;

        while (enumerator.MoveNext())
        {
            var seg = enumerator.Current;
            switch (seg.Verb)
            {
                case PathVerb.MoveTo:
                    if (hasMove)
                    {
                        return false;
                    }
                    hasMove = true;
                    points.Add(seg.End);
                    break;
                case PathVerb.LineTo:
                    if (!hasMove)
                    {
                        return false;
                    }
                    points.Add(seg.End);
                    break;
                case PathVerb.Close:
                    return false;
                default:
                    return false;
            }
        }

        return hasMove && points.Count >= 2;
    }

    /// <summary>
    /// Detects whether a polyline path is a circular arc approximation.
    /// Returns true if all points lie on a common circle with consistent angular spacing.
    /// </summary>
    private static bool TryDetectArc(BezPath path, out EGeometry.Point center, out double radius, out double startRad, out double sweepRad)
    {
        center = default;
        radius = 0;
        startRad = 0;
        sweepRad = 0;

        var points = new List<EGeometry.Point>();
        var enumerator = path.Iterate();
        bool hasMove = false;

        while (enumerator.MoveNext())
        {
            var seg = enumerator.Current;
            switch (seg.Verb)
            {
                case PathVerb.MoveTo:
                    if (hasMove) { return false; }
                    hasMove = true;
                    points.Add(seg.End);
                    break;
                case PathVerb.LineTo:
                    if (!hasMove) { return false; }
                    points.Add(seg.End);
                    break;
                default:
                    return false;
            }
        }

        if (points.Count < 5) { return false; }

        // Compute circumcenter from first, middle, and last points
        var p0 = points[0];
        var pm = points[points.Count / 2];
        var pn = points[points.Count - 1];

        double d = 2 * ((p0.X - pn.X) * (pm.Y - pn.Y) - (pm.X - pn.X) * (p0.Y - pn.Y));
        if (Math.Abs(d) < 1e-10) { return false; }

        double ux = ((p0.Y - pn.Y) * (p0.Y - pm.Y) * (pm.Y - pn.Y)
                   + (p0.X - pn.X) * (p0.X - pm.X) * (pm.X - pn.X)
                   - (p0.X - pm.X) * (pm.Y - pn.Y) * (p0.Y - pn.Y)) / d;
        double uy = ((p0.X - pn.X) * (p0.X - pm.X) * (pm.X - pn.X)
                   + (p0.Y - pn.Y) * (p0.Y - pm.Y) * (pm.Y - pn.Y)
                   - (p0.Y - pm.Y) * (pm.X - pn.X) * (p0.X - pn.X)) / d;

        // Actually, let me use the standard circumcenter formula
        double ax = p0.X, ay = p0.Y;
        double bx = pm.X, by = pm.Y;
        double cx = pn.X, cy = pn.Y;
        double d2 = 2 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
        if (Math.Abs(d2) < 1e-10) { return false; }

        double ux2 = ((ax * ax + ay * ay) * (by - cy)
                    + (bx * bx + by * by) * (cy - ay)
                    + (cx * cx + cy * cy) * (ay - by)) / d2;
        double uy2 = ((ax * ax + ay * ay) * (cx - bx)
                    + (bx * bx + by * by) * (ax - cx)
                    + (cx * cx + cy * cy) * (bx - ax)) / d2;

        center = new EGeometry.Point(ux2, uy2);
        radius = Math.Sqrt((ax - ux2) * (ax - ux2) + (ay - uy2) * (ay - uy2));

        if (radius < 1.0) { return false; }

        // Verify all points are on this circle
        double maxDev = 0;
        foreach (var p in points)
        {
            double dist = Math.Sqrt((p.X - ux2) * (p.X - ux2) + (p.Y - uy2) * (p.Y - uy2));
            maxDev = Math.Max(maxDev, Math.Abs(dist - radius));
        }
        if (maxDev > Math.Max(radius * 0.02, 0.5))
        {
            return false;
        }

        // Compute start and sweep angles
        startRad = Math.Atan2(points[0].Y - uy2, points[0].X - ux2);
        double endRad = Math.Atan2(points[points.Count - 1].Y - uy2, points[points.Count - 1].X - ux2);
        sweepRad = endRad - startRad;

        // Determine the correct sweep direction by checking intermediate points
        double signedArea = 0;
        for (int i = 0; i < points.Count - 1; i++)
        {
            signedArea += (points[i].X - ux2) * (points[i + 1].Y - uy2)
                        - (points[i + 1].X - ux2) * (points[i].Y - uy2);
        }

        // Normalize sweep to match the direction of the points
        if (signedArea > 0 && sweepRad < 0)
        {
            sweepRad += 2 * Math.PI;
        }
        else if (signedArea < 0 && sweepRad > 0)
        {
            sweepRad -= 2 * Math.PI;
        }

        // Handle wrap-around: if |sweep| is very small but points span nearly 2PI,
        // the arc likely goes the long way around
        if (Math.Abs(sweepRad) < 0.1 && points.Count > 10)
        {
            if (signedArea > 0)
            {
                sweepRad = 2 * Math.PI;
            }
            else
            {
                sweepRad = -2 * Math.PI;
            }
        }

        return true;
    }

    private static bool TryDetectRect(BezPath path, out EGeometry.Rect rect)
    {
        rect = EGeometry.Rect.Empty;
        var enumerator = path.Iterate();
        int lineCount = 0;
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        bool hasMove = false;

        while (enumerator.MoveNext())
        {
            var seg = enumerator.Current;
            switch (seg.Verb)
            {
                case PathVerb.MoveTo:
                    hasMove = true;
                    minX = Math.Min(minX, seg.End.X);
                    minY = Math.Min(minY, seg.End.Y);
                    maxX = Math.Max(maxX, seg.End.X);
                    maxY = Math.Max(maxY, seg.End.Y);
                    break;
                case PathVerb.LineTo:
                    lineCount++;
                    minX = Math.Min(minX, seg.End.X);
                    minY = Math.Min(minY, seg.End.Y);
                    maxX = Math.Max(maxX, seg.End.X);
                    maxY = Math.Max(maxY, seg.End.Y);
                    break;
                case PathVerb.Close:
                    break;
                default:
                    return false;
            }
        }

        if (!hasMove || lineCount != 4)
        {
            return false;
        }

        rect = new EGeometry.Rect(minX, minY, maxX, maxY);
        return true;
    }

    private static bool TryDetectRoundedRect(BezPath path, out EGeometry.Rect rect, out float radius)
    {
        rect = EGeometry.Rect.Empty;
        radius = 0;
        var enumerator = path.Iterate();
        int lineCount = 0;
        int cubicCount = 0;
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        bool hasMove = false;
        EGeometry.Point firstMove = default;
        EGeometry.Point firstCubicEnd = default;

        while (enumerator.MoveNext())
        {
            var seg = enumerator.Current;
            switch (seg.Verb)
            {
                case PathVerb.MoveTo:
                    hasMove = true;
                    firstMove = seg.End;
                    minX = Math.Min(minX, seg.End.X);
                    minY = Math.Min(minY, seg.End.Y);
                    maxX = Math.Max(maxX, seg.End.X);
                    maxY = Math.Max(maxY, seg.End.Y);
                    break;
                case PathVerb.LineTo:
                    lineCount++;
                    minX = Math.Min(minX, seg.End.X);
                    minY = Math.Min(minY, seg.End.Y);
                    maxX = Math.Max(maxX, seg.End.X);
                    maxY = Math.Max(maxY, seg.End.Y);
                    break;
                case PathVerb.CubicTo:
                    if (cubicCount == 0)
                    {
                        firstCubicEnd = seg.End;
                    }
                    cubicCount++;
                    minX = Math.Min(minX, seg.End.X);
                    minY = Math.Min(minY, seg.End.Y);
                    maxX = Math.Max(maxX, seg.End.X);
                    maxY = Math.Max(maxY, seg.End.Y);
                    break;
                case PathVerb.Close:
                    break;
                default:
                    return false;
            }
        }

        if (!hasMove || lineCount != 4 || cubicCount != 4)
        {
            return false;
        }

        rect = new EGeometry.Rect(minX, minY, maxX, maxY);

        float rx = (float)(firstMove.X - minX);
        float ry = (float)(firstCubicEnd.Y - minY);
        radius = (rx + ry) * 0.5f;

        float halfMinDim = (float)(Math.Min(rect.Width, rect.Height) * 0.5);
        if (radius <= 0 || radius > halfMinDim + 1)
        {
            return false;
        }

        return true;
    }

    private static (bool, EGeometry.Point, double) TryDetectCircle(BezPath path)
    {
        var enumerator = path.Iterate();
        int cubicCount = 0;
        double cx = 0, cy = 0, rx = 0, ry = 0;
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        bool hasMove = false;
        bool hasLine = false;
        while (enumerator.MoveNext())
        {
            var seg = enumerator.Current;
            switch (seg.Verb)
            {
                case PathVerb.MoveTo:
                    rx = seg.End.X; ry = seg.End.Y; hasMove = true;
                    Extend(seg.End);
                    break;
                case PathVerb.LineTo: hasLine = true; break;
                case PathVerb.CubicTo:
                    cubicCount++;
                    cx += seg.Control0.X + seg.Control1.X + seg.End.X;
                    cy += seg.Control0.Y + seg.Control1.Y + seg.End.Y;
                    Extend(seg.Control0);
                    Extend(seg.Control1);
                    Extend(seg.End);
                    break;
            }
        }
        if (cubicCount == 4 && hasMove && !hasLine)
        {
            double avgX = cx / (cubicCount * 3), avgY = cy / (cubicCount * 3);
            double dx = rx - avgX, dy = ry - avgY;
            double radius = Math.Sqrt(dx * dx + dy * dy);

            // Validate: bounding box must be roughly square with size ≈ 2*radius
            // (rejects rounded rects which also have 4 cubic segments). The box of the control
            // points is used: for a cubic circle the controls lie on the tangents at the extremes,
            // so it equals the curve's box, and it costs nothing. The exact curve AABB (solving
            // for cubic extrema) made this check ~17% of a canvas-heavy frame (EnergyOrbs).
            double width = maxX - minX;
            double height = maxY - minY;
            double diameter = radius * 2;
            double tolerance = diameter * 0.15;

            if (Math.Abs(width - diameter) <= tolerance && Math.Abs(height - diameter) <= tolerance)
            {
                return (true, new EGeometry.Point(avgX, avgY), radius);
            }
        }
        return (false, default, 0);

        void Extend(EGeometry.Point p)
        {
            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X);
            maxY = Math.Max(maxY, p.Y);
        }
    }
}

