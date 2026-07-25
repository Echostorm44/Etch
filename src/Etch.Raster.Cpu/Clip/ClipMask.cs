using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using Etch.Geometry;
using Etch.Geometry.Flatten;
using Etch.Scene;
using Etch.Tiling.Strips;

namespace Etch.Raster.Cpu;

public readonly struct ClipMask
{
    public Framebuffer Coverage { get; }

    public ClipMask(Framebuffer coverage)
    {
        Coverage = coverage;
    }

    public static ClipMask Empty => new(FramebufferPool.Rent(0, 0));

    [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeAnalysis", "CA1062:Validate arguments before using them", Justification = "Project uses Panic system for validation")]
    public static ClipMask Build(SceneBuffer scene, ReadOnlySpan<SceneCommand> clipCommands, Framebuffer scratch)
    {
        if (scene is null)
            Panic.ArgumentNull(nameof(scene));

        var accumulator = new ClipMaskAccumulator();

        for (int i = 0; i < clipCommands.Length; i++)
        {
            ref readonly var cmd = ref clipCommands[i];
            switch (cmd.Op)
            {
                case SceneOpcode.SetTransform:
                    accumulator.SetTransform(scene.GetTransform(cmd.SetTransform.TransformId));
                    break;
                case SceneOpcode.PushClip:
                    accumulator.PushClip(cmd.PushClip, scene);
                    break;
                case SceneOpcode.PopClip:
                    accumulator.PopClip();
                    break;
            }
        }

        return accumulator.Build(scratch);
    }

    private sealed class ClipMaskAccumulator
    {
        private const int MaxClipDepth = 16;
        private const int MaxEdges = 4096;

        private readonly ClipLayer[] _layers;
        private int _depth;
        private Affine _currentTransform;

        public ClipMaskAccumulator()
        {
            _layers = new ClipLayer[MaxClipDepth];
            _depth = 0;
            _currentTransform = Affine.Identity;
        }

        public void SetTransform(Affine transform)
        {
            _currentTransform = transform;
        }

        public void PushClip(PushClipPayload payload, SceneBuffer scene)
        {
            if (_depth >= MaxClipDepth)
                Panic.Invariant(PanicCodes.ClipStackOverflow, $"Clip depth {_depth} exceeds limit of {MaxClipDepth}");

            if (!scene.TryGetPath(payload.ClipId, out var pathEntry))
                return;

            var path = pathEntry.Path;
            var fillRule = payload.FillRule == 0 ? FillRule.NonZero : FillRule.EvenOdd;
            var clipMode = payload.ClipMode == 0 ? ClipMode.Intersect : ClipMode.Difference;

            _layers[_depth] = new ClipLayer(path, fillRule, clipMode, _currentTransform);
            _depth++;
        }

        public void PopClip()
        {
            if (_depth <= 0)
                Panic.Invariant(PanicCodes.UnbalancedClipStack, "PopClip without matching PushClip");

            _depth--;
        }

        public ClipMask Build(Framebuffer scratch)
        {
            if (_depth == 0)
                return ClipMask.Empty;

            if (scratch.Width <= 0 || scratch.Height <= 0)
                Panic.Invariant(PanicCodes.InvariantViolation, "ClipMask.Build requires non-zero scratch dimensions");

            var mask = FramebufferPool.Rent(scratch.Width, scratch.Height);
            try
            {
                var maskPixels = mask.Pixels.Span;
                for (int i = 0; i < maskPixels.Length; i++)
                    maskPixels[i] = Rgba16f.From(1.0f, 0, 0, 0);

                var edges = ArrayPool<(Point, Point)>.Shared.Rent(MaxEdges);
                try
                {
                    for (int i = 0; i < _depth; i++)
                    {
                        ref readonly var layer = ref _layers[i];

                        ClearFramebuffer(scratch);
                        RasterizeLayer(layer, edges, scratch);
                        CompositeLayer(layer, mask, scratch);
                    }
                }
                finally
                {
                    ArrayPool<(Point, Point)>.Shared.Return(edges);
                }

                return new ClipMask(mask);
            }
            catch
            {
                FramebufferPool.Return(ref mask);
                throw;
            }
        }

        private static void ClearFramebuffer(Framebuffer fb)
        {
            var span = fb.Pixels.Span;
            for (int i = 0; i < span.Length; i++)
                span[i] = Rgba16f.From(0, 0, 0, 0);
        }

        private static void RasterizeLayer(ClipLayer layer, (Point, Point)[] edgeBuffer, Framebuffer target)
        {
            int edgeCount = 0;
            FlattenPathToEdges(layer.Path, layer.Transform, edgeBuffer.AsSpan(), out edgeCount);

            if (edgeCount == 0)
                return;

            var coverageBytes = ArrayPool<byte>.Shared.Rent(target.Width);
            try
            {
                var edgeSpan = edgeBuffer.AsSpan(0, edgeCount);

                for (int y = 0; y < target.Height; y++)
                {
                    if (layer.FillRule == FillRule.EvenOdd)
                    {
                        AnalyticCoverage.ComputeColumnCoverage(edgeSpan, coverageBytes.AsSpan(0, target.Width), 0, 0, target.Width, y);
                    }
                    else
                    {
                        AnalyticCoverage.ComputeColumnCoverageNonZero(edgeSpan, coverageBytes.AsSpan(0, target.Width), 0, 0, target.Width, y);
                    }

                    var row = target.RowSpan(y);
                    for (int x = 0; x < target.Width; x++)
                    {
                        float alpha = coverageBytes[x] * (1.0f / 255.0f);
                        row[x] = Rgba16f.From(alpha, 0, 0, 0);
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(coverageBytes);
            }
        }

        private static void CompositeLayer(ClipLayer layer, Framebuffer mask, Framebuffer scratch)
        {
            var maskPixels = mask.Pixels.Span;
            var scratchPixels = scratch.Pixels.Span;

            if (layer.ClipMode == ClipMode.Intersect)
            {
                for (int i = 0; i < maskPixels.Length; i++)
                {
                    float maskAlpha = (float)maskPixels[i].R;
                    float scratchAlpha = (float)scratchPixels[i].R;
                    maskPixels[i] = Rgba16f.From(maskAlpha * scratchAlpha, 0, 0, 0);
                }
            }
            else
            {
                for (int i = 0; i < maskPixels.Length; i++)
                {
                    float maskAlpha = (float)maskPixels[i].R;
                    float scratchAlpha = (float)scratchPixels[i].R;
                    maskPixels[i] = Rgba16f.From(maskAlpha * (1.0f - scratchAlpha), 0, 0, 0);
                }
            }
        }

        private static void FlattenPathToEdges(BezPath path, Affine transform, Span<(Point, Point)> edges, out int edgeCount)
        {
            edgeCount = 0;
            Point current = default;
            Point subpathStart = default;
            bool hasCurrent = false;

            var tempPoints = ArrayPool<Point>.Shared.Rent(256);
            try
            {
                foreach (PathSegment seg in path.Iterate())
                {
                    switch (seg.Verb)
                    {
                        case PathVerb.MoveTo:
                            current = transform * seg.End;
                            subpathStart = current;
                            hasCurrent = true;
                            break;

                        case PathVerb.LineTo:
                            if (!hasCurrent)
                                continue;
                            Point lineNext = transform * seg.End;
                            if (edgeCount < edges.Length)
                                edges[edgeCount++] = (current, lineNext);
                            current = lineNext;
                            break;

                        case PathVerb.QuadTo:
                            if (!hasCurrent)
                                continue;
                            var q = new QuadBez(current, transform * seg.Control0, transform * seg.End);
                            FlattenQuadToEdges(in q, tempPoints, edges, ref edgeCount);
                            current = transform * seg.End;
                            break;

                        case PathVerb.CubicTo:
                            if (!hasCurrent)
                                continue;
                            var c = new CubicBez(current, transform * seg.Control0, transform * seg.Control1, transform * seg.End);
                            FlattenCubicToEdges(in c, tempPoints, edges, ref edgeCount);
                            current = transform * seg.End;
                            break;

                        case PathVerb.Close:
                            if (hasCurrent && current != subpathStart && edgeCount < edges.Length)
                                edges[edgeCount++] = (current, subpathStart);
                            current = subpathStart;
                            break;
                    }
                }
            }
            finally
            {
                ArrayPool<Point>.Shared.Return(tempPoints);
            }

            if (edgeCount >= edges.Length)
                Panic.Invariant(PanicCodes.BufferOverflow, "Clip mask edge buffer exceeded maximum capacity");
        }

        private static void FlattenQuadToEdges(in QuadBez q, Point[] temp, Span<(Point, Point)> edges, ref int edgeCount)
        {
            FlattenSink sink = new FlattenSink(temp.AsSpan(), autoflush: false);
            CurveFlattener.QuadBez(in q, 0.05, ref sink);
            if (sink.IsFull)
                Panic.Invariant(PanicCodes.FlattenSinkOverflow, "Clip mask quad flattening overflow");

            var written = sink.Written;
            for (int i = 0; i < written.Length - 1 && edgeCount < edges.Length; i++)
                edges[edgeCount++] = (written[i], written[i + 1]);
        }

        private static void FlattenCubicToEdges(in CubicBez c, Point[] temp, Span<(Point, Point)> edges, ref int edgeCount)
        {
            FlattenSink sink = new FlattenSink(temp.AsSpan(), autoflush: false);
            CurveFlattener.CubicBez(in c, 0.05, ref sink);
            if (sink.IsFull)
                Panic.Invariant(PanicCodes.FlattenSinkOverflow, "Clip mask cubic flattening overflow");

            var written = sink.Written;
            for (int i = 0; i < written.Length - 1 && edgeCount < edges.Length; i++)
                edges[edgeCount++] = (written[i], written[i + 1]);
        }
    }

    private readonly struct ClipLayer
    {
        public BezPath Path { get; }
        public FillRule FillRule { get; }
        public ClipMode ClipMode { get; }
        public Affine Transform { get; }

        public ClipLayer(BezPath path, FillRule fillRule, ClipMode clipMode, Affine transform)
        {
            Path = path;
            FillRule = fillRule;
            ClipMode = clipMode;
            Transform = transform;
        }
    }
}
