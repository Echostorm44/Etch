using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Etch.Geometry;
using Etch.Geometry.Flatten;
using Etch.Scene;

namespace Etch.Tiling.Strips;

/// <summary>
/// The transform and clip stack each scene command sees, resolved once per emit. The emitter visits
/// classification entries in tile order, so it cannot track this state by walking the command
/// stream as it goes (doing so applied the last-seen state to every later tile).
/// </summary>
internal sealed class CommandState : IDisposable
{
    private const int MaxClipDepth = 16;

    private readonly List<Rect> clipRects = new();
    private readonly List<(int Start, int Length)> snapshots = new();
    private Affine[] transforms;
    private int[] snapshotOf;

    private CommandState(int commandCount)
    {
        transforms = ArrayPool<Affine>.Shared.Rent(Math.Max(1, commandCount));
        snapshotOf = ArrayPool<int>.Shared.Rent(Math.Max(1, commandCount));
    }

    /// <summary>Transform in effect at <paramref name="order"/> (after that command's own op).</summary>
    public Affine TransformAt(int order) => transforms[order];

    /// <summary>Clip rectangles in effect at <paramref name="order"/>; empty entries mean "no restriction".</summary>
    public ReadOnlySpan<Rect> ClipStackAt(int order)
    {
        var (start, length) = snapshots[snapshotOf[order]];
        return CollectionsMarshal.AsSpan(clipRects).Slice(start, length);
    }

    public static CommandState Resolve(SceneBuffer scene, ReadOnlySpan<SceneCommand> commands)
    {
        var state = new CommandState(commands.Length);
        var xform = Affine.Identity;
        Span<Rect> stack = stackalloc Rect[MaxClipDepth];
        int depth = 0;
        int current = state.AddSnapshot(stack.Slice(0, 0));

        for (int order = 0; order < commands.Length; order++)
        {
            ref readonly var cmd = ref commands[order];
            switch (cmd.Op)
            {
                case SceneOpcode.SetTransform:
                    xform = scene.GetTransform(cmd.SetTransform.TransformId);
                    break;
                case SceneOpcode.PushClip:
                    if (depth < MaxClipDepth && scene.TryGetPath(cmd.PushClip.ClipId, out var pathData))
                    {
                        var clipAabb = pathData.Path.Aabb();
                        stack[depth++] = !clipAabb.IsEmpty && cmd.PushClip.ClipMode == 0
                            ? StripEmitter.TransformRect(xform, clipAabb)
                            : Rect.Empty;
                        current = state.AddSnapshot(stack.Slice(0, depth));
                    }
                    break;
                case SceneOpcode.PopClip:
                    if (depth > 0)
                    {
                        depth--;
                        current = state.AddSnapshot(stack.Slice(0, depth));
                    }
                    break;
            }
            state.transforms[order] = xform;
            state.snapshotOf[order] = current;
        }
        return state;
    }

    private int AddSnapshot(ReadOnlySpan<Rect> stack)
    {
        int start = clipRects.Count;
        foreach (var rect in stack)
        {
            clipRects.Add(rect);
        }
        snapshots.Add((start, stack.Length));
        return snapshots.Count - 1;
    }

    public void Dispose()
    {
        ArrayPool<Affine>.Shared.Return(transforms);
        ArrayPool<int>.Shared.Return(snapshotOf);
        transforms = [];
        snapshotOf = [];
    }
}

/// <summary>
/// Device-space geometry per command, built on first use within one emit. A path covering N tiles
/// used to be flattened and transformed N times; now it is once.
/// </summary>
internal sealed class GeometryCache : IDisposable
{
    private const double FlattenTolerance = 0.05;
    private const int FlattenCapacity = 65536;

    private readonly Dictionary<int, (Point[] Points, int Length)> fills = new();
    private readonly Dictionary<int, List<(Point, Point)>> strokes = new();
    private Point[]? flattenScratch;

    /// <summary>The flattened path in device space (same tolerance and order as before caching).</summary>
    public ReadOnlySpan<Point> DeviceFill(int order, in BezPath path, Affine xform)
    {
        if (fills.TryGetValue(order, out var cached))
        {
            return cached.Points.AsSpan(0, cached.Length);
        }

        flattenScratch ??= ArrayPool<Point>.Shared.Rent(FlattenCapacity);
        var sink = new FlattenSink(flattenScratch.AsSpan(), autoflush: true);
        CurveFlattener.BezPath(in path, FlattenTolerance, ref sink);
        var written = sink.Written;

        var points = ArrayPool<Point>.Shared.Rent(Math.Max(1, written.Length));
        for (int i = 0; i < written.Length; i++)
        {
            points[i] = xform.Transform(written[i]);
        }
        fills[order] = (points, written.Length);
        return points.AsSpan(0, written.Length);
    }

    /// <summary>Every edge of the stroke outline in device space, in emission order.</summary>
    public ReadOnlySpan<(Point, Point)> DeviceStrokeOutline(int order, in BezPath path, Affine xform, float halfWidth)
    {
        if (!strokes.TryGetValue(order, out var outline))
        {
            outline = BuildStrokeOutline(in path, xform, halfWidth);
            strokes[order] = outline;
        }
        return CollectionsMarshal.AsSpan(outline);
    }

    private static List<(Point, Point)> BuildStrokeOutline(in BezPath path, Affine xform, float halfWidth)
    {
        var outline = new List<(Point, Point)>();
        void Collect(Point a, Point b) => outline.Add((a, b));

        var subpathBuilder = BezPathBuilder.Begin(64);
        bool hasMoveTo = false;
        bool currentClosed = false;

        foreach (var seg in path.Iterate())
        {
            switch (seg.Verb)
            {
                case PathVerb.MoveTo:
                    if (hasMoveTo)
                    {
                        StripEmitter.ProcessStrokeSubpath(subpathBuilder.Build(), currentClosed, halfWidth, Collect);
                        subpathBuilder.Dispose();
                        subpathBuilder = BezPathBuilder.Begin(64);
                    }
                    subpathBuilder.MoveTo(xform.Transform(seg.End));
                    hasMoveTo = true;
                    currentClosed = false;
                    break;
                case PathVerb.LineTo:
                    subpathBuilder.LineTo(xform.Transform(seg.End));
                    break;
                case PathVerb.QuadTo:
                    subpathBuilder.QuadTo(xform.Transform(seg.Control0), xform.Transform(seg.End));
                    break;
                case PathVerb.CubicTo:
                    subpathBuilder.CubicTo(xform.Transform(seg.Control0), xform.Transform(seg.Control1), xform.Transform(seg.End));
                    break;
                case PathVerb.Close:
                    subpathBuilder.Close();
                    currentClosed = true;
                    break;
            }
        }

        if (hasMoveTo)
        {
            StripEmitter.ProcessStrokeSubpath(subpathBuilder.Build(), currentClosed, halfWidth, Collect);
        }
        subpathBuilder.Dispose();
        return outline;
    }

    public void Dispose()
    {
        foreach (var (points, _) in fills.Values)
        {
            ArrayPool<Point>.Shared.Return(points);
        }
        fills.Clear();
        strokes.Clear();
        if (flattenScratch is not null)
        {
            ArrayPool<Point>.Shared.Return(flattenScratch);
            flattenScratch = null;
        }
    }
}
