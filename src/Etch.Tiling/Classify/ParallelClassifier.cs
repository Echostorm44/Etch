using System;
using System.Buffers;
using System.Threading;
using Etch.Geometry;
using Etch.Primitives;
using Etch.Scene;
using Etch.Tiling.Scheduler;

namespace Etch.Tiling.Classify;

public static class ParallelClassifier
{
    public static ClassifiedScene Classify<TTile>(SceneBuffer scene, TileGrid<TTile> grid, ITileScheduler? scheduler)
        where TTile : struct, ITileSize
    {
#pragma warning disable CA1062
        if (scheduler == null)
            Etch.Panic.Invariant(Etch.PanicCodes.SchedulerRequired, "scheduler must not be null");

        int workerCount = Environment.ProcessorCount;
        if (scheduler is WorkStealingTileScheduler wsts)
            workerCount = wsts.WorkerCount;
        else if (scheduler is SingleThreadedTileScheduler)
            workerCount = 1;

        var result = ClassifyInternal(scene, grid, workerCount);

        scheduler.Dispose();

        return result;
    }

    public static ClassifiedScene Classify<TTile>(SceneBuffer scene, TileGrid<TTile> grid, int threadCount)
        where TTile : struct, ITileSize
    {
        int workerCount = threadCount <= 1 ? 1 : threadCount;
        return ClassifyInternal(scene, grid, workerCount);
    }

    private static ClassifiedScene ClassifyInternal<TTile>(SceneBuffer scene, TileGrid<TTile> grid, int workerCount)
        where TTile : struct, ITileSize
    {
        int commandsPerWorker = (scene.Commands.Length + workerCount - 1) / workerCount;

        ClassificationEntry[][] perThreadEntries = new ClassificationEntry[workerCount][];
        Thread[] threads = new Thread[workerCount - 1];

        for (int w = 0; w < workerCount - 1; w++)
        {
            int workerIndex = w;
            int startCmd = workerIndex * commandsPerWorker;
            int endCmd = Math.Min(startCmd + commandsPerWorker, scene.Commands.Length);
            threads[w] = new Thread(() =>
            {
                perThreadEntries[workerIndex] = ClassifyCommands(scene, grid, startCmd, endCmd);
            });
            threads[w].Start();
        }

        {
            int w = workerCount - 1;
            int startCmd = w * commandsPerWorker;
            int endCmd = Math.Min(startCmd + commandsPerWorker, scene.Commands.Length);
            perThreadEntries[w] = ClassifyCommands(scene, grid, startCmd, endCmd);
        }

        for (int w = 0; w < workerCount - 1; w++)
        {
            threads[w].Join();
        }

        return ClassificationMerge.Merge(perThreadEntries, grid);
    }

    private static ClassificationEntry[] ClassifyCommands<TTile>(SceneBuffer scene, TileGrid<TTile> grid, int startCmd, int endCmd)
        where TTile : struct, ITileSize
    {
        if (startCmd >= scene.Commands.Length)
            return [];

        var entries = new System.Collections.Generic.List<ClassificationEntry>(1024);
        var commands = scene.Commands;
        var xform = Affine.Identity;
        int order = startCmd;

        for (int i = startCmd; i < endCmd; i++)
        {
            ref readonly var cmd = ref commands[i];
            switch (cmd.Op)
            {
                case SceneOpcode.SetTransform:
                    xform = scene.GetTransform(cmd.SetTransform.TransformId);
                    break;

                case SceneOpcode.FillPath:
                    {
                        if (!scene.TryGetPath(cmd.FillPath.PathId, out var pathData))
                            break;
                        var pathAabb = pathData.Path.Aabb();
                        if (pathAabb.IsEmpty)
                            break;
                        var deviceAabb = TransformRect(xform * scene.GetTransform(cmd.FillPath.TransformId), pathAabb);
                        EmitTilesForAabb(deviceAabb, grid, entries, order, ClassificationKind.FillPath);
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
                        EmitTilesForAabb(deviceAabb, grid, entries, order, ClassificationKind.StrokePath);
                        break;
                    }

                case SceneOpcode.FillRect:
                    {
                        var rect = scene.GetRect(cmd.FillRect.RectId);
                        if (rect.IsEmpty)
                            break;
                        var deviceAabb = TransformRect(xform * scene.GetTransform(cmd.FillRect.TransformId), rect);
                        EmitTilesForAabb(deviceAabb, grid, entries, order, ClassificationKind.FillRect);
                        break;
                    }

                case SceneOpcode.DrawImage:
                case SceneOpcode.DrawGlyphRun:
                    break;
            }
            order++;
        }

        return entries.ToArray();
    }

    private static void EmitTilesForAabb<TTile>(Rect aabb, TileGrid<TTile> grid, System.Collections.Generic.List<ClassificationEntry> entries, int order, ClassificationKind kind)
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
                entries.Add(new ClassificationEntry(tileIndex, order, kind, payload));
            }
        }
    }

    private static Rect TransformRect(Affine a, Rect r)
    {
        if (r.IsEmpty)
            return Rect.Empty;

        double minX = r.MinX;
        double minY = r.MinY;
        double maxX = r.MaxX;
        double maxY = r.MaxY;

        var p0 = a.Transform(new Point(minX, minY));
        var p1 = a.Transform(new Point(maxX, minY));
        var p2 = a.Transform(new Point(maxX, maxY));
        var p3 = a.Transform(new Point(minX, maxY));

        double resultMinX = Math.Min(Math.Min(p0.X, p1.X), Math.Min(p2.X, p3.X));
        double resultMinY = Math.Min(Math.Min(p0.Y, p1.Y), Math.Min(p2.Y, p3.Y));
        double resultMaxX = Math.Max(Math.Max(p0.X, p1.X), Math.Max(p2.X, p3.X));
        double resultMaxY = Math.Max(Math.Max(p0.Y, p1.Y), Math.Max(p2.Y, p3.Y));

        if (resultMinX >= resultMaxX || resultMinY >= resultMaxY)
            return Rect.Empty;

        return Rect.FromLTRB(resultMinX, resultMinY, resultMaxX, resultMaxY);
    }
}
