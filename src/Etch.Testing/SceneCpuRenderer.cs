using System;
using System.Collections.Generic;
using Etch.Geometry;
using Etch.Gpu;
using Etch.Raster.Cpu;
using Etch.Scene;
using Etch.Tiling;
using Etch.Tiling.Classify;
using Etch.Tiling.Scheduler;
using Etch.Tiling.Strips;

namespace Etch.Testing;

public static class SceneCpuRenderer
{
    public static byte[] RenderToRgba8(SceneBuffer scene, int width, int height)
    {
        return RenderToOutput(scene, width, height, ColorSpace.Srgb);
    }

    public static byte[] RenderToOutput(SceneBuffer scene, int width, int height, ColorSpace colorSpace)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (width <= 0 || height <= 0)
            Etch.Panic.Invariant(Etch.PanicCodes.InvalidSurfaceSize, "Render dimensions must be positive");

        var grid = new TileGrid<TTile8>(width, height);
        var classified = ClassifySingleThreaded(scene, grid);

        return RenderClassified(scene, grid, classified, width, height, colorSpace);
    }

    public static byte[] RenderToRgba8Parallel(SceneBuffer scene, int width, int height, ITileScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(scheduler);
        if (width <= 0 || height <= 0)
            Etch.Panic.Invariant(Etch.PanicCodes.InvalidSurfaceSize, "Render dimensions must be positive");

        var grid = new TileGrid<TTile8>(width, height);
        var classified = ParallelClassifier.Classify(scene, grid, scheduler);

        return RenderClassified(scene, grid, classified, width, height, ColorSpace.Srgb);
    }

    private static ClassifiedScene ClassifySingleThreaded(SceneBuffer scene, TileGrid<TTile8> grid)
    {
        var accum = new ClassificationAccumulator(4096);
#pragma warning disable CA1062
        BBoxClassifier.Classify(scene, grid, ref accum);
#pragma warning restore CA1062
        var entries = accum.Finish().ToArray();
        return ClassificationMerge.Merge([entries], grid);
    }

    private static byte[] RenderClassified(SceneBuffer scene, TileGrid<TTile8> grid, ClassifiedScene classified, int width, int height, ColorSpace colorSpace = ColorSpace.Srgb)
    {

        var fbBuffer = new Rgba16f[width * height];
        for (int i = 0; i < fbBuffer.Length; i++)
            fbBuffer[i] = Rgba16f.From(0, 0, 0, 0);
        var fb = new Framebuffer(width, height, width, fbBuffer);

        var scratch = FramebufferPool.Rent(width, height);
        try
        {
            var commands = scene.Commands;
            var clipCommands = new List<SceneCommand>();

            // Track the active (SetTransform) matrix so the direct rasterizers
            // (circle, sector) place geometry in the same space as the strip
            // pipeline — notably the CPU-fallback downscale lives here.
            var currentXform = Etch.Geometry.Affine.Identity;

            for (int i = 0; i < commands.Length; i++)
            {
                ref readonly var cmd = ref commands[i];
                switch (cmd.Op)
                {
                    case SceneOpcode.SetTransform:
                        currentXform = scene.GetTransform(cmd.SetTransform.TransformId);
                        clipCommands.Add(cmd);
                        break;

                    case SceneOpcode.PushClip:
                    case SceneOpcode.PopClip:
                        clipCommands.Add(cmd);
                        break;

                    case SceneOpcode.FillRect:
                    case SceneOpcode.FillPath:
                    case SceneOpcode.StrokePath:
                    {
                        var mask = BuildClipMask(scene, clipCommands, scratch);

                        // Detect circle paths and rasterize directly (bypass strip pipeline)
                        if (cmd.Op == SceneOpcode.FillPath &&
                            scene.TryGetPath(cmd.FillPath.PathId, out var pathData) &&
                            TryDetectCircle(pathData.Path, out var circleCenter, out var circleRadius))
                        {
                            var paint = scene.GetPaint(cmd.FillPath.PaintId);
                            if (paint.Kind == PaintKind.Solid)
                            {
                                var xf = currentXform * scene.GetTransform(cmd.FillPath.TransformId);
                                RasterizeCircle(fb, circleCenter, circleRadius, xf, paint.Color, mask);
                            }
                        }
                        else
                        {
                            var filtered = FilterClassifiedByCommandOrder(classified, i);
                            var strips = StripEmitter.Emit(scene, filtered, grid);
                            StripRenderer.Render(scene, strips, grid, fb, mask);
                        }

                        if (mask.Coverage.Width > 0)
                        {
                            var coverage = mask.Coverage;
                            FramebufferPool.Return(ref coverage);
                        }
                        break;
                    }

                    case SceneOpcode.FillSector:
                    {
                        // WP-3514: the strip pipeline has no sector primitive, so
                        // pie/donut slices (FillSector) are rasterized directly —
                        // otherwise they silently vanish on the CPU fallback.
                        var paint = scene.GetPaint(cmd.FillSector.PaintId);
                        if (paint.Kind == PaintKind.Solid)
                        {
                            var mask = BuildClipMask(scene, clipCommands, scratch);
                            var xf = currentXform * scene.GetTransform(cmd.FillSector.TransformId);
                            RasterizeSector(fb, in cmd.FillSector, xf, paint.Color, mask);
                            if (mask.Coverage.Width > 0)
                            {
                                var coverage = mask.Coverage;
                                FramebufferPool.Return(ref coverage);
                            }
                        }
                        break;
                    }
                }
            }
        }
        finally
        {
            FramebufferPool.Return(ref scratch);
        }

        var outputBytes = width * height * ColorSpaceFormat.BytesPerPixel(colorSpace);
        var result = new byte[outputBytes];
        ColorSpaceEncoder.Encode(fbBuffer, result, colorSpace, width, height);

        return result;
    }

    private static ClipMask BuildClipMask(SceneBuffer scene, List<SceneCommand> clipCommands, Framebuffer scratch)
    {
        if (clipCommands.Count == 0)
            return ClipMask.Empty;

        Span<SceneCommand> span = clipCommands.Count <= 256
            ? stackalloc SceneCommand[clipCommands.Count]
            : new SceneCommand[clipCommands.Count];
        for (int i = 0; i < clipCommands.Count; i++)
            span[i] = clipCommands[i];

        return ClipMask.Build(scene, span, scratch);
    }

    private static ClassifiedScene FilterClassifiedByCommandOrder(ClassifiedScene source, int commandOrder)
    {
        var all = source.AllEntries;
        int count = 0;
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].CommandOrder == commandOrder)
                count++;
        }

        if (count == 0)
            return new ClassifiedScene([], new int[source.TileCount + 1], source.TileCount);

        var filtered = new ClassificationEntry[count];
        var offsets = new int[source.TileCount + 1];

        int writeIdx = 0;
        for (int t = 0; t < source.TileCount; t++)
        {
            offsets[t] = writeIdx;
            var tileEntries = source.Entries(t);
            for (int e = 0; e < tileEntries.Length; e++)
            {
                if (tileEntries[e].CommandOrder == commandOrder)
                {
                    filtered[writeIdx++] = tileEntries[e];
                }
            }
        }
        offsets[source.TileCount] = writeIdx;

        return new ClassifiedScene(filtered, offsets, source.TileCount);
    }

    private static bool TryDetectCircle(BezPath path, out Point center, out double radius)
    {
        center = default;
        radius = 0;
        var enumerator = path.Iterate();
        int cubicCount = 0;
        int lineCount = 0;
        int quadCount = 0;
        double cx = 0, cy = 0;
        double curX = 0, curY = 0;
        bool hasMove = false;
        Span<double> endX = stackalloc double[4];
        Span<double> endY = stackalloc double[4];
        while (enumerator.MoveNext())
        {
            var seg = enumerator.Current;
            switch (seg.Verb)
            {
            case Etch.Geometry.PathVerb.MoveTo:
                curX = seg.End.X; curY = seg.End.Y; hasMove = true;
                break;
            case Etch.Geometry.PathVerb.LineTo:
                // A rounded rectangle has four straight edges between its corner
                // cubics; a true circle has none. Tolerate a zero-length closing
                // line that the iterator may synthesize.
                if (Math.Abs(seg.End.X - curX) > 0.01 || Math.Abs(seg.End.Y - curY) > 0.01)
                {
                    lineCount++;
                }
                curX = seg.End.X; curY = seg.End.Y;
                break;
            case Etch.Geometry.PathVerb.QuadTo:
                quadCount++;
                curX = seg.End.X; curY = seg.End.Y;
                break;
            case Etch.Geometry.PathVerb.CubicTo:
                if (cubicCount < 4)
                {
                    endX[cubicCount] = seg.End.X;
                    endY[cubicCount] = seg.End.Y;
                }
                cubicCount++;
                cx += seg.Control0.X + seg.Control1.X + seg.End.X;
                cy += seg.Control0.Y + seg.Control1.Y + seg.End.Y;
                curX = seg.End.X; curY = seg.End.Y;
                break;
            }
        }

        // A circle is exactly four cubics with no straight edges or quads. This
        // is what distinguishes it from a rounded rectangle (4 cubics + 4 lines),
        // whose corners would otherwise be mistaken for a circle and filled as a
        // solid disc (WP-3514).
        if (!hasMove || cubicCount != 4 || lineCount != 0 || quadCount != 0)
        {
            return false;
        }

        double centerX = cx / (cubicCount * 3);
        double centerY = cy / (cubicCount * 3);

        // The four cubic endpoints are the circle's cardinal points; require them
        // equidistant from the center so 4-cubic non-circles (e.g. ellipses,
        // teardrops) fall through to the general strip pipeline.
        double r0 = Math.Sqrt((endX[0] - centerX) * (endX[0] - centerX) + (endY[0] - centerY) * (endY[0] - centerY));
        if (r0 < 0.5)
        {
            return false;
        }
        for (int k = 1; k < 4; k++)
        {
            double rk = Math.Sqrt((endX[k] - centerX) * (endX[k] - centerX) + (endY[k] - centerY) * (endY[k] - centerY));
            if (Math.Abs(rk - r0) > r0 * 0.05 + 0.5)
            {
                return false;
            }
        }

        center = new Point(centerX, centerY);
        radius = r0;
        return true;
    }

    private static void RasterizeCircle(Framebuffer fb, Point center, double radius, Affine xform, uint color, ClipMask mask = default)
    {
        var tc = xform.Transform(center);
        float rLin = Srgb.DecodeChannelScalar((byte)((color >> 16) & 0xFF));
        float gLin = Srgb.DecodeChannelScalar((byte)((color >> 8) & 0xFF));
        float bLin = Srgb.DecodeChannelScalar((byte)(color & 0xFF));
        float aLin = ((color >> 24) & 0xFF) * (1.0f / 255.0f);

        // Scale the path-space radius into device space (the transform now carries
        // the CPU-fallback downscale, so the radius must follow the center).
        double r = radius * Math.Sqrt(Math.Abs(xform.Determinant()));
        if (r <= 0) return;
        int minX = Math.Max(0, (int)(tc.X - r - 1));
        int maxX = Math.Min(fb.Width - 1, (int)(tc.X + r + 1));
        int minY = Math.Max(0, (int)(tc.Y - r - 1));
        int maxY = Math.Min(fb.Height - 1, (int)(tc.Y + r + 1));

        bool hasMask = mask.Coverage.Width > 0;

        for (int y = minY; y <= maxY; y++)
        {
            var row = fb.RowSpan(y);
            var maskRow = hasMask ? mask.Coverage.RowSpan(y) : default;
            for (int x = minX; x <= maxX; x++)
            {
                double dx = x - tc.X + 0.5;
                double dy = y - tc.Y + 0.5;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                double coverage = Math.Clamp(r - dist + 0.5, 0.0, 1.0);

                if (coverage <= 0) continue;
                if (hasMask)
                {
                    coverage *= (double)maskRow[x].R;
                    if (coverage <= 0) continue;
                }

                float alpha = (float)(coverage * aLin);
                float invAlpha = 1.0f - alpha;
                ref var dst = ref row[x];
                float dr = (float)dst.R, dg = (float)dst.G, db = (float)dst.B, da = (float)dst.A;
                dst = Rgba16f.From(
                    rLin * alpha + dr * invAlpha,
                    gLin * alpha + dg * invAlpha,
                    bLin * alpha + db * invAlpha,
                    alpha + da * invAlpha);
            }
        }
    }

    /// <summary>
    /// Rasterizes a filled annular sector (pie/donut slice) directly into the
    /// framebuffer. The strip pipeline has no sector primitive, so without this
    /// FillSector commands render nothing on the CPU fallback (WP-3514). Angle
    /// convention matches the GPU sector instance: point = center + r·(cos θ, sin θ)
    /// with screen-Y down.
    /// </summary>
    private static void RasterizeSector(Framebuffer fb, in Etch.Scene.FillSectorPayload s, Affine xform, uint color, ClipMask mask)
    {
        var tc = xform.Transform(new Point(s.CenterX, s.CenterY));
        double scaleF = Math.Sqrt(Math.Abs(xform.Determinant()));
        double outerR = s.OuterRadius * scaleF;
        double innerR = s.InnerRadius * scaleF;
        if (outerR <= 0) return;

        float rLin = Srgb.DecodeChannelScalar((byte)((color >> 16) & 0xFF));
        float gLin = Srgb.DecodeChannelScalar((byte)((color >> 8) & 0xFF));
        float bLin = Srgb.DecodeChannelScalar((byte)(color & 0xFF));
        float aLin = ((color >> 24) & 0xFF) * (1.0f / 255.0f);

        double start = s.StartRad;
        double sweep = s.SweepRad;
        if (sweep < 0) { start += sweep; sweep = -sweep; }
        const double twoPi = Math.PI * 2;
        bool fullCircle = sweep >= twoPi - 1e-4;

        int minX = Math.Max(0, (int)(tc.X - outerR - 1));
        int maxX = Math.Min(fb.Width - 1, (int)(tc.X + outerR + 1));
        int minY = Math.Max(0, (int)(tc.Y - outerR - 1));
        int maxY = Math.Min(fb.Height - 1, (int)(tc.Y + outerR + 1));

        bool hasMask = mask.Coverage.Width > 0;

        for (int y = minY; y <= maxY; y++)
        {
            var row = fb.RowSpan(y);
            var maskRow = hasMask ? mask.Coverage.RowSpan(y) : default;
            for (int x = minX; x <= maxX; x++)
            {
                double dx = x - tc.X + 0.5;
                double dy = y - tc.Y + 0.5;
                double dist = Math.Sqrt(dx * dx + dy * dy);

                // Radial coverage: 1px anti-aliased band at the outer (and inner) edge.
                double cov = Math.Clamp(outerR - dist + 0.5, 0.0, 1.0);
                if (innerR > 0)
                {
                    cov = Math.Min(cov, Math.Clamp(dist - innerR + 0.5, 0.0, 1.0));
                }
                if (cov <= 0) continue;

                if (!fullCircle)
                {
                    double ang = Math.Atan2(dy, dx) - start;
                    while (ang < 0) ang += twoPi;
                    while (ang >= twoPi) ang -= twoPi;
                    if (ang > sweep)
                    {
                        // Outside the wedge — anti-alias the two radial edges by
                        // arc-length distance to the nearer edge.
                        double gap = Math.Min(ang - sweep, twoPi - ang);
                        double covA = Math.Clamp(0.5 - gap * dist, 0.0, 1.0);
                        cov *= covA;
                        if (cov <= 0) continue;
                    }
                }

                if (hasMask)
                {
                    cov *= (double)maskRow[x].R;
                    if (cov <= 0) continue;
                }

                float alpha = (float)(cov * aLin);
                float invAlpha = 1.0f - alpha;
                ref var dst = ref row[x];
                float dr = (float)dst.R, dg = (float)dst.G, db = (float)dst.B, da = (float)dst.A;
                dst = Rgba16f.From(
                    rLin * alpha + dr * invAlpha,
                    gLin * alpha + dg * invAlpha,
                    bLin * alpha + db * invAlpha,
                    alpha + da * invAlpha);
            }
        }
    }

    /// <summary>
    /// Reusable CPU render context that pre-computes tile/strip/mask data during
    /// construction so that <see cref="Render"/> is allocation-free.
    /// Create once for a given scene and output size, then call <see cref="Render"/> repeatedly.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses", Justification = "Instantiated by allocation regression tests in Etch.Correctness.Tests")]
    internal sealed class CpuRenderCache : IDisposable
    {
        private readonly SceneBuffer _scene;
        private readonly int _width;
        private readonly int _height;
        private readonly TileGrid<TTile8> _grid;
        private readonly Rgba16f[] _fbBuffer;
        private readonly Framebuffer _fb;
        private Framebuffer _scratch;
        private readonly byte[] _output;
        private readonly List<RenderOp> _ops;

        private struct RenderOp
        {
            public StripBuffer Strips;
            public ClipMask Mask;
        }

        internal CpuRenderCache(SceneBuffer scene, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(scene);
            if (width <= 0 || height <= 0)
                Etch.Panic.Invariant(Etch.PanicCodes.InvalidSurfaceSize, "Render dimensions must be positive");

            _scene = scene;
            _width = width;
            _height = height;
            _grid = new TileGrid<TTile8>(width, height);
            _fbBuffer = new Rgba16f[width * height];
            _fb = new Framebuffer(width, height, width, _fbBuffer);
            _scratch = FramebufferPool.Rent(width, height);
            _output = new byte[width * height * 4];
            _ops = new List<RenderOp>();

            WarmUp();
        }

        private void WarmUp()
        {
            var accum = new ClassificationAccumulator(4096);
            try
            {
                BBoxClassifier.Classify(_scene, _grid, ref accum);
                var entries = accum.Finish().ToArray();
                var classified = ClassificationMerge.Merge([entries], _grid);

                var clipCommands = new List<SceneCommand>(256);
                var commands = _scene.Commands;

                for (int i = 0; i < commands.Length; i++)
                {
                    ref readonly var cmd = ref commands[i];
                    switch (cmd.Op)
                    {
                        case SceneOpcode.SetTransform:
                        case SceneOpcode.PushClip:
                        case SceneOpcode.PopClip:
                            clipCommands.Add(cmd);
                            break;

                        case SceneOpcode.FillRect:
                        case SceneOpcode.FillPath:
                            var mask = BuildClipMask(_scene, clipCommands, _scratch);
                            var filtered = FilterClassifiedByCommandOrder(classified, i);
                            var strips = StripEmitter.Emit(_scene, filtered, _grid);
                            _ops.Add(new RenderOp { Strips = strips, Mask = mask });
                            break;
                    }
                }
            }
            finally
            {
                accum.Dispose();
            }
        }

        public byte[] Render()
        {
            _fbBuffer.AsSpan().Clear();

            foreach (var op in _ops)
            {
                StripRenderer.Render(_scene, op.Strips, _grid, _fb, op.Mask);
            }

            ColorSpaceEncoder.Encode(_fbBuffer, _output, ColorSpace.Srgb, _width, _height);
            return _output;
        }

        public void Dispose()
        {
            foreach (var op in _ops)
            {
                if (op.Mask.Coverage.Width > 0)
                {
                    var coverage = op.Mask.Coverage;
                    FramebufferPool.Return(ref coverage);
                }
            }
            _ops.Clear();

            FramebufferPool.Return(ref _scratch);
        }
    }
}
