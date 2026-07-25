using System;
using System.IO.Hashing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Etch.Geometry;

namespace Etch.Scene.Damage;

internal static class CommandHasher
{
    private const int MaxHashesPerTile = 32;
    private const double TileSize = 32.0;
    private const int HashBufferSize = 64;

    [ThreadStatic]
    private static XxHash3? s_hasher;

    [ThreadStatic]
    private static byte[]? s_hashBuffer;

    private static XxHash3 GetHasher()
    {
        if (s_hasher == null)
            s_hasher = new XxHash3();
        else
            s_hasher.Reset();
        return s_hasher;
    }

    private static Span<byte> GetHashBuffer()
    {
        if (s_hashBuffer == null)
            s_hashBuffer = new byte[HashBufferSize];
        return s_hashBuffer.AsSpan();
    }

    public static void HashCommandsToTiles(ReadOnlySpan<SceneCommand> commands, SceneBuffer scene, int tileCountX, int tileCountY, Span<ulong> tileHashes)
    {
        tileHashes.Clear();

        Affine currentXform = Affine.Identity;

        for (int i = 0; i < commands.Length; i++)
        {
            ref readonly var cmd = ref commands[i];
            switch (cmd.Op)
            {
                case SceneOpcode.SetTransform:
                    currentXform = scene.GetTransform(cmd.SetTransform.TransformId);
                    break;

                case SceneOpcode.FillPath:
                    {
                        if (!scene.TryGetPath(cmd.FillPath.PathId, out var pathData))
                            break;
                        var aabb = pathData.Path.Aabb();
                        if (aabb.IsEmpty)
                            break;
                        var deviceAabb = TransformRect(currentXform * scene.GetTransform(cmd.FillPath.TransformId), aabb);
                        var (minTX, minTY, maxTX, maxTY) = TileRange(deviceAabb, tileCountX, tileCountY);
                        if (minTX > maxTX || minTY > maxTY)
                            break;

                        var paint = scene.GetPaint(cmd.FillPath.PaintId);
                        ulong hash = HashFillPath(cmd, pathData, paint);
                        AddHashToTiles(tileHashes, minTX, minTY, maxTX, maxTY, tileCountX, hash);
                        break;
                    }

                case SceneOpcode.StrokePath:
                    {
                        if (!scene.TryGetPath(cmd.StrokePath.PathId, out var pathData))
                            break;
                        var aabb = pathData.Path.Aabb();
                        if (aabb.IsEmpty)
                            break;
                        float halfStroke = cmd.StrokePath.StrokeWidth * 0.5f;
                        var inflated = new Rect(aabb.MinX - halfStroke, aabb.MinY - halfStroke, aabb.MaxX + halfStroke, aabb.MaxY + halfStroke);
                        var deviceAabb = TransformRect(currentXform * scene.GetTransform(cmd.StrokePath.TransformId), inflated);
                        var (minTX, minTY, maxTX, maxTY) = TileRange(deviceAabb, tileCountX, tileCountY);
                        if (minTX > maxTX || minTY > maxTY)
                            break;

                        var paint = scene.GetPaint(cmd.StrokePath.PaintId);
                        ulong hash = HashStrokePath(cmd, pathData, paint);
                        AddHashToTiles(tileHashes, minTX, minTY, maxTX, maxTY, tileCountX, hash);
                        break;
                    }

                case SceneOpcode.FillRect:
                    {
                        var rect = scene.GetRect(cmd.FillRect.RectId);
                        if (rect.IsEmpty)
                            break;
                        var deviceAabb = TransformRect(currentXform * scene.GetTransform(cmd.FillRect.TransformId), rect);
                        var (minTX, minTY, maxTX, maxTY) = TileRange(deviceAabb, tileCountX, tileCountY);
                        if (minTX > maxTX || minTY > maxTY)
                            break;

                        var paint = scene.GetPaint(cmd.FillRect.PaintId);
                        ulong hash = HashFillRect(cmd, rect, paint);
                        AddHashToTiles(tileHashes, minTX, minTY, maxTX, maxTY, tileCountX, hash);
                        break;
                    }
            }
        }
    }

    private static (int minX, int minY, int maxX, int maxY) TileRange(Rect aabb, int tileCountX, int tileCountY)
    {
        int minX = Math.Max(0, (int)Math.Floor(aabb.MinX / TileSize));
        int minY = Math.Max(0, (int)Math.Floor(aabb.MinY / TileSize));
        int maxX = Math.Max(minX, Math.Min(tileCountX - 1, (int)Math.Floor((aabb.MaxX - 1e-10) / TileSize)));
        int maxY = Math.Max(minY, Math.Min(tileCountY - 1, (int)Math.Floor((aabb.MaxY - 1e-10) / TileSize)));
        return (minX, minY, maxX, maxY);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong HashFillPath(SceneCommand cmd, PathData pathData, Paint paint)
    {
        var hasher = GetHasher();
        var buffer = GetHashBuffer();

        buffer[0] = (byte)SceneOpcode.FillPath;
        Unsafe.WriteUnaligned(ref buffer[1], cmd.FillPath.PathId);
        Unsafe.WriteUnaligned(ref buffer[5], cmd.FillPath.PaintId);
        Unsafe.WriteUnaligned(ref buffer[9], cmd.FillPath.TransformId);
        buffer[13] = cmd.FillPath.FillRule;
        Unsafe.WriteUnaligned(ref buffer[14], pathData.Path.VerbCount);
        buffer[18] = (byte)paint.Kind;
        Unsafe.WriteUnaligned(ref buffer[19], paint.ColorOrGradientId);

        hasher.Append(buffer[..27]);
        return hasher.GetCurrentHashAsUInt64();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong HashStrokePath(SceneCommand cmd, PathData pathData, Paint paint)
    {
        var hasher = GetHasher();
        var buffer = GetHashBuffer();

        buffer[0] = (byte)SceneOpcode.StrokePath;
        Unsafe.WriteUnaligned(ref buffer[1], cmd.StrokePath.PathId);
        Unsafe.WriteUnaligned(ref buffer[5], cmd.StrokePath.PaintId);
        Unsafe.WriteUnaligned(ref buffer[9], cmd.StrokePath.TransformId);
        Unsafe.WriteUnaligned(ref buffer[13], cmd.StrokePath.StrokeWidth);
        Unsafe.WriteUnaligned(ref buffer[17], pathData.Path.VerbCount);
        buffer[21] = (byte)paint.Kind;
        Unsafe.WriteUnaligned(ref buffer[22], paint.ColorOrGradientId);

        hasher.Append(buffer[..30]);
        return hasher.GetCurrentHashAsUInt64();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong HashFillRect(SceneCommand cmd, Rect rect, Paint paint)
    {
        var hasher = GetHasher();
        var buffer = GetHashBuffer();

        buffer[0] = (byte)SceneOpcode.FillRect;
        Unsafe.WriteUnaligned(ref buffer[1], cmd.FillRect.RectId);
        Unsafe.WriteUnaligned(ref buffer[5], cmd.FillRect.PaintId);
        Unsafe.WriteUnaligned(ref buffer[9], cmd.FillRect.TransformId);
        Unsafe.WriteUnaligned(ref buffer[13], rect.MinX);
        Unsafe.WriteUnaligned(ref buffer[21], rect.MinY);
        Unsafe.WriteUnaligned(ref buffer[29], rect.MaxX);
        Unsafe.WriteUnaligned(ref buffer[37], rect.MaxY);
        buffer[45] = (byte)paint.Kind;
        Unsafe.WriteUnaligned(ref buffer[46], paint.ColorOrGradientId);

        hasher.Append(buffer[..54]);
        return hasher.GetCurrentHashAsUInt64();
    }

    private static void AddHashToTiles(Span<ulong> tileHashes, int minTX, int minTY, int maxTX, int maxTY, int tileCountX, ulong hash)
    {
        for (int ty = minTY; ty <= maxTY; ty++)
        {
            int rowStart = ty * tileCountX;
            for (int tx = minTX; tx <= maxTX; tx++)
            {
                int tileIdx = rowStart + tx;
                int baseIdx = tileIdx * MaxHashesPerTile;
                int endIdx = baseIdx + MaxHashesPerTile;
                for (int idx = baseIdx; idx < endIdx; idx++)
                {
                    if (tileHashes[idx] == 0)
                    {
                        tileHashes[idx] = hash;
                        break;
                    }
                }
            }
        }
    }

    private static Rect TransformRect(Affine xform, Rect r)
    {
        if (r.IsEmpty)
            return Rect.Empty;

        var p0 = xform.Transform(new Point(r.MinX, r.MinY));
        var p1 = xform.Transform(new Point(r.MaxX, r.MinY));
        var p2 = xform.Transform(new Point(r.MaxX, r.MaxY));
        var p3 = xform.Transform(new Point(r.MinX, r.MaxY));

        double resultMinX = Math.Min(Math.Min(p0.X, p1.X), Math.Min(p2.X, p3.X));
        double resultMinY = Math.Min(Math.Min(p0.Y, p1.Y), Math.Min(p2.Y, p3.Y));
        double resultMaxX = Math.Max(Math.Max(p0.X, p1.X), Math.Max(p2.X, p3.X));
        double resultMaxY = Math.Max(Math.Max(p0.Y, p1.Y), Math.Max(p2.Y, p3.Y));

        if (resultMinX >= resultMaxX || resultMinY >= resultMaxY)
            return Rect.Empty;

        return Rect.FromLTRB(resultMinX, resultMinY, resultMaxX, resultMaxY);
    }
}