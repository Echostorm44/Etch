using System;
using System.Buffers;
using Etch.Geometry;

namespace Etch.Scene.Damage;

public sealed class SubpixelDamageTracker
{
    private const int MaxRects = 32;
    private const double TileSize = 32.0;

    private readonly int _deviceWidth;
    private readonly int _deviceHeight;
    private readonly int _tileCountX;
    private readonly int _tileCountY;
    private Rect[] _rectBuffer;
    private int _rectCount;

    public static SubpixelDamageTracker Create(int deviceWidth, int deviceHeight)
    {
        int tileCountX = (int)Math.Ceiling((double)deviceWidth / TileSize);
        int tileCountY = (int)Math.Ceiling((double)deviceHeight / TileSize);
        return new SubpixelDamageTracker(deviceWidth, deviceHeight, tileCountX, tileCountY);
    }

    private SubpixelDamageTracker(int deviceWidth, int deviceHeight, int tileCountX, int tileCountY)
    {
        _deviceWidth = deviceWidth;
        _deviceHeight = deviceHeight;
        _tileCountX = tileCountX;
        _tileCountY = tileCountY;
        _rectBuffer = ArrayPool<Rect>.Shared.Rent(MaxRects);
        _rectCount = 0;
    }

    public DamageResult DiffSubpixel(SceneBuffer prev, SceneBuffer curr)
    {
#pragma warning disable CA1062
        _rectCount = 0;

        var prevAABBs = ComputeAABBs(prev);
        var currAABBs = ComputeAABBs(curr);

        foreach (var rect in prevAABBs)
        {
            AddRect(rect);
        }

        foreach (var rect in currAABBs)
        {
            AddRect(rect);
        }

        if (_rectCount > MaxRects)
        {
            return new DamageResult(Array.Empty<Rect>());
        }

        var result = new Rect[_rectCount];
        Array.Copy(_rectBuffer, result, _rectCount);
        return new DamageResult(result);
#pragma warning restore CA1062
    }

    private static List<Rect> ComputeAABBs(SceneBuffer buffer)
    {
        var aabbs = new List<Rect>();
        var currentXform = Affine.Identity;

        foreach (ref readonly var cmd in buffer.Commands)
        {
            switch (cmd.Op)
            {
                case SceneOpcode.SetTransform:
                    currentXform = buffer.GetTransform(cmd.SetTransform.TransformId);
                    break;

                case SceneOpcode.FillPath:
                    if (buffer.TryGetPath(cmd.FillPath.PathId, out var pathData))
                    {
                        var aabb = pathData.Path.Aabb();
                        if (!aabb.IsEmpty)
                        {
                            var deviceAabb = TransformRect(currentXform * buffer.GetTransform(cmd.FillPath.TransformId), aabb);
                            if (!deviceAabb.IsEmpty)
                            {
                                aabbs.Add(deviceAabb);
                            }
                        }
                    }
                    break;

                case SceneOpcode.StrokePath:
                    if (buffer.TryGetPath(cmd.StrokePath.PathId, out var strokePathData))
                    {
                        var aabb = strokePathData.Path.Aabb();
                        if (!aabb.IsEmpty)
                        {
                            float halfStroke = cmd.StrokePath.StrokeWidth * 0.5f;
                            var inflated = new Rect(aabb.MinX - halfStroke, aabb.MinY - halfStroke, aabb.MaxX + halfStroke, aabb.MaxY + halfStroke);
                            var deviceAabb = TransformRect(currentXform * buffer.GetTransform(cmd.StrokePath.TransformId), inflated);
                            if (!deviceAabb.IsEmpty)
                            {
                                aabbs.Add(deviceAabb);
                            }
                        }
                    }
                    break;

                case SceneOpcode.FillRect:
                    var rect = buffer.GetRect(cmd.FillRect.RectId);
                    if (!rect.IsEmpty)
                    {
                        var deviceAabb = TransformRect(currentXform * buffer.GetTransform(cmd.FillRect.TransformId), rect);
                        if (!deviceAabb.IsEmpty)
                        {
                            aabbs.Add(deviceAabb);
                        }
                    }
                    break;
            }
        }

        return aabbs;
    }

    private void AddRect(Rect rect)
    {
        for (int i = 0; i < _rectCount; i++)
        {
            if (_rectBuffer[i].Intersects(rect))
            {
                _rectBuffer[i] = _rectBuffer[i].Union(rect);
                return;
            }
        }

        if (_rectCount < MaxRects)
        {
            _rectBuffer[_rectCount++] = rect;
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

    public void Dispose()
    {
        ArrayPool<Rect>.Shared.Return(_rectBuffer);
    }
}