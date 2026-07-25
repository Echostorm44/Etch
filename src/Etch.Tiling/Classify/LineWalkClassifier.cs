using System;
using Etch.Geometry;
using Etch.Geometry.Flatten;
using Etch.Scene;

namespace Etch.Tiling.Classify;

public static class LineWalkClassifier
{
    private const int MaxSeenTiles = 256;
    private const int PolylineBufferSize = 2048;
    private const double DefaultTolerance = 0.25;

    public static void Classify<TTile>(SceneBuffer scene, TileGrid<TTile> grid, ref ClassificationAccumulator accum, double tolerance = DefaultTolerance)
        where TTile : struct, ITileSize
    {
#pragma warning disable CA1062
        if (scene == null)
            Etch.Panic.Invariant(Etch.PanicCodes.ArgumentNull, "scene must not be null");

        var commands = scene.Commands;
#pragma warning restore CA1062
        var xform = Affine.Identity;
        int order = 0;

        for (int i = 0; i < commands.Length; i++)
        {
            ref readonly var cmd = ref commands[i];

            switch (cmd.Op)
            {
                case SceneOpcode.SetTransform:
                    xform = scene.GetTransform(cmd.SetTransform.TransformId);
                    break;

                case SceneOpcode.FillPath:
                    ClassifyPath(scene, grid, ref accum, xform, cmd.FillPath.PathId, cmd.FillPath.TransformId, order, ClassificationKind.FillPath, tolerance);
                    break;

                case SceneOpcode.StrokePath:
                    ClassifyPath(scene, grid, ref accum, xform, cmd.StrokePath.PathId, cmd.StrokePath.TransformId, order, ClassificationKind.StrokePath, tolerance);
                    break;
            }
            order++;
        }
    }

    private static void ClassifyPath<TTile>(SceneBuffer scene, TileGrid<TTile> grid, ref ClassificationAccumulator accum, Affine baseXform, int pathId, int transformId, int order, ClassificationKind kind, double tolerance)
        where TTile : struct, ITileSize
    {
        if (!scene.TryGetPath(pathId, out var pathData))
            return;

        var pathAabb = pathData.Path.Aabb();
        if (pathAabb.IsEmpty)
            return;

        int tileCountX = grid.TileCountX;
        int tileCountY = grid.TileCountY;

        grid.TilesOverlappingPixelRect(pathAabb, out var minX, out var minY, out var maxX, out var maxY);
        int bboxTileCount = (maxX - minX + 1) * (maxY - minY + 1);

        if (bboxTileCount <= 4)
        {
            EmitBbox(grid, minX, minY, maxX, maxY, order, kind, ref accum);
            return;
        }

        if (bboxTileCount >= MaxSeenTiles)
        {
            EmitBbox(grid, minX, minY, maxX, maxY, order, kind, ref accum);
            return;
        }

        Span<Point> polyline = stackalloc Point[PolylineBufferSize];
        Span<int> seenTiles = stackalloc int[MaxSeenTiles];

        var sink = new FlattenSink(polyline, autoflush: true);
        var worldXform = baseXform * scene.GetTransform(transformId);
        CurveFlattener.BezPath(pathData.Path, tolerance, ref sink);

        var written = sink.Written;
        if (written.Length < 2)
        {
            EmitBbox(grid, minX, minY, maxX, maxY, order, kind, ref accum);
            return;
        }

        int seenCount = 0;
        for (int j = 1; j < written.Length; j++)
        {
            Point p0 = worldXform * written[j - 1];
            Point p1 = worldXform * written[j];

            int walked = SupercoverDda.Walk(p0, p1, TTile.Log2Width, TTile.Log2Height, seenTiles.Slice(seenCount));

            for (int k = 0; k < walked && seenCount < MaxSeenTiles; k++)
            {
                int tileKey = seenTiles[seenCount++];
                int tx = tileKey & 0xFFFF;
                int ty = tileKey >> 16;
                if ((uint)tx < (uint)tileCountX && (uint)ty < (uint)tileCountY)
                {
                    int tileIndex = ty * tileCountX + tx;
                    accum.Append(new ClassificationEntry(tileIndex, order, kind, default));
                }
            }

            if (seenCount >= MaxSeenTiles)
                break;
        }

        if (seenCount == 0)
            EmitBbox(grid, minX, minY, maxX, maxY, order, kind, ref accum);
    }

    private static void EmitBbox<TTile>(TileGrid<TTile> grid, int minX, int minY, int maxX, int maxY, int order, ClassificationKind kind, ref ClassificationAccumulator accum)
        where TTile : struct, ITileSize
    {
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
