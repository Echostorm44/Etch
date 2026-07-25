using System;
using Etch.Geometry;
using Etch.Scene;

namespace Etch.Tiling.Classify;

public static class BBoxClassifier
{
    private const int MaxClipDepth = 16;

    public static void Classify<TTile>(SceneBuffer scene, TileGrid<TTile> grid, ref ClassificationAccumulator accum)
        where TTile : struct, ITileSize
    {
#pragma warning disable CA1062
        if (scene == null)
            Etch.Panic.Invariant(Etch.PanicCodes.ArgumentNull, "scene must not be null");

        var commands = scene.Commands;
#pragma warning restore CA1062
        var xform = Affine.Identity;
        int order = 0;

        Span<Rect> clipStack = stackalloc Rect[MaxClipDepth];
        int clipDepth = 0;

        for (int i = 0; i < commands.Length; i++)
        {
            ref readonly var cmd = ref commands[i];
            switch (cmd.Op)
            {
                case SceneOpcode.SetTransform:
                    xform = scene.GetTransform(cmd.SetTransform.TransformId);
                    break;

                case SceneOpcode.PushClip:
                    {
                        if (clipDepth < MaxClipDepth &&
                            scene.TryGetPath(cmd.PushClip.ClipId, out var pathData))
                        {
                            var clipAabb = pathData.Path.Aabb();
                            if (!clipAabb.IsEmpty)
                            {
                                var deviceAabb = TransformRect(xform, clipAabb);
                                if (cmd.PushClip.ClipMode == 0)
                                    clipStack[clipDepth] = deviceAabb;
                                else
                                    clipStack[clipDepth] = Rect.Empty;
                            }
                            else
                            {
                                clipStack[clipDepth] = Rect.Empty;
                            }
                            clipDepth++;
                        }
                        break;
                    }

                case SceneOpcode.PopClip:
                    {
                        if (clipDepth > 0)
                            clipDepth--;
                        break;
                    }

                case SceneOpcode.FillPath:
                    {
                        if (!scene.TryGetPath(cmd.FillPath.PathId, out var pathData))
                            break;
                        var pathAabb = pathData.Path.Aabb();
                        if (pathAabb.IsEmpty)
                            break;
                        var deviceAabb = TransformRect(xform * scene.GetTransform(cmd.FillPath.TransformId), pathAabb);
                        deviceAabb = IntersectWithClipStack(deviceAabb, clipStack, clipDepth);
                        EmitTilesForAabb(deviceAabb, grid, ref accum, order, ClassificationKind.FillPath);
                        break;
                    }

                case SceneOpcode.StrokePath:
                    {
                        if (!scene.TryGetPath(cmd.StrokePath.PathId, out var pathData))
                            break;
                        var pathAabb = pathData.Path.Aabb();
                        if (pathAabb.IsEmpty)
                            break;
                        float halfStroke = cmd.StrokePath.StrokeWidth * 0.5f;
                        var inflated = new Rect(
                            pathAabb.MinX - halfStroke,
                            pathAabb.MinY - halfStroke,
                            pathAabb.MaxX + halfStroke,
                            pathAabb.MaxY + halfStroke);
                        var deviceAabb = TransformRect(xform * scene.GetTransform(cmd.StrokePath.TransformId), inflated);
                        deviceAabb = IntersectWithClipStack(deviceAabb, clipStack, clipDepth);
                        EmitTilesForAabb(deviceAabb, grid, ref accum, order, ClassificationKind.StrokePath);
                        break;
                    }

                case SceneOpcode.FillRect:
                    {
                        var rect = scene.GetRect(cmd.FillRect.RectId);
                        if (rect.IsEmpty)
                            break;
                        var deviceAabb = TransformRect(xform * scene.GetTransform(cmd.FillRect.TransformId), rect);
                        deviceAabb = IntersectWithClipStack(deviceAabb, clipStack, clipDepth);
                        EmitTilesForAabb(deviceAabb, grid, ref accum, order, ClassificationKind.FillRect);
                        break;
                    }

                case SceneOpcode.DrawImage:
                case SceneOpcode.DrawGlyphRun:
                    break;
            }
            order++;
        }
    }

    private static Rect IntersectWithClipStack(Rect aabb, ReadOnlySpan<Rect> clipStack, int clipDepth)
    {
        if (aabb.IsEmpty || clipDepth == 0)
            return aabb;

        for (int i = 0; i < clipDepth; i++)
        {
            ref readonly var clip = ref clipStack[i];
            if (clip.IsEmpty)
                continue;

            aabb = IntersectRects(aabb, clip);
            if (aabb.IsEmpty)
                return Rect.Empty;
        }

        return aabb;
    }

    private static Rect IntersectRects(Rect a, Rect b)
    {
        if (a.IsEmpty || b.IsEmpty)
            return Rect.Empty;

        double minX = Math.Max(a.MinX, b.MinX);
        double minY = Math.Max(a.MinY, b.MinY);
        double maxX = Math.Min(a.MaxX, b.MaxX);
        double maxY = Math.Min(a.MaxY, b.MaxY);

        if (minX >= maxX || minY >= maxY)
            return Rect.Empty;

        return Rect.FromLTRB(minX, minY, maxX, maxY);
    }

    private static unsafe Rect TransformRect(Affine a, Rect r)
    {
        if (r.IsEmpty)
            return Rect.Empty;

        var pts = stackalloc Point[4];
        pts[0] = new Point(r.MinX, r.MinY);
        pts[1] = new Point(r.MaxX, r.MinY);
        pts[2] = new Point(r.MaxX, r.MaxY);
        pts[3] = new Point(r.MinX, r.MaxY);

        return BatchAabb.OfPointsTransformed(a, new ReadOnlySpan<Point>(pts, 4));
    }

    private static void EmitTilesForAabb<TTile>(Rect aabb, TileGrid<TTile> grid, ref ClassificationAccumulator accum, int order, ClassificationKind kind)
        where TTile : struct, ITileSize
    {
        if (aabb.IsEmpty)
            return;

        grid.TilesOverlappingPixelRect(aabb, out var minX, out var minY, out var maxX, out var maxY);

        if (minX > maxX || minY > maxY)
            return;

        var payload = default(CoveragePayload);
        for (int ty = minY; ty <= maxY; ty++)
        {
            for (int tx = minX; tx <= maxX; tx++)
            {
                int tileIndex = grid.TileIndex(tx, ty);
                accum.Append(new ClassificationEntry(tileIndex, order, kind, payload));
            }
        }
    }
}
