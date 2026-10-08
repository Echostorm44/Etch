using System.IO.Hashing;
using System.Runtime.InteropServices;
using Etch.Compose.Coverage;
using Etch.Geometry;
using Etch.Scene;
using Etch.Text.Atlas;

namespace Etch.Compose;

/// <summary>
/// Replays <see cref="DrawRecording"/>s into a frame's <see cref="DrawList"/>: applies transforms,
/// normalizes the clip stack into clip-table entries, turns each draw into the analytic shape that
/// renders it exactly — or, when no analytic shape can (arbitrary paths, non-uniform scale, skew,
/// dashes), into a coverage mask rasterized once by <see cref="CoverageRasterizer"/> and shared by
/// both backends through the <see cref="MaskAtlas"/>.
/// </summary>
/// <remarks>
/// The builder owns no GPU or CPU resources; the composer that will execute the list supplies its
/// atlases. Everything it needs per frame is reused, so a steady-state frame allocates nothing.
/// </remarks>
public sealed class DrawListBuilder
{
    /// <summary>Curve flattening tolerance, device pixels.</summary>
    public const float FlattenTolerance = 0.1f;

    // Antialiasing margin around analytic shapes: coverage reaches zero one pixel outside the edge.
    private const float AaMargin = 1.5f;

    // Path masks larger than this are split into tiles (the atlas packs smaller masks better).
    private const int MaskTileSize = 512;

    private readonly CoverageRasterizer rasterizer = new();
    private readonly PathStroker stroker = new();
    private readonly FlatPath flat = new();
    private readonly FlatPath strokeInput = new();
    private readonly FlatPath strokeOutput = new();
    private readonly XxHash3 hasher = new();
    private readonly List<ClipLayer> clipStack = new();
    private readonly List<GradientStopEntry> stopScratch = new();
    private readonly byte[] coverage = new byte[MaskTileSize * MaskTileSize];
    private readonly byte[] clipCoverage = new byte[MaskAtlas.ClipMaskTile * MaskAtlas.ClipMaskTile];
    private readonly List<MaskTileEntry> tileScratch = new();

    // Mask tiles found to be uniform (all one coverage value) take no atlas space; their value is
    // remembered here by tile key for as long as the atlas keeps its content.
    private readonly Dictionary<ulong, byte> uniformTiles = new();
    private int uniformGeneration = -1;

    private DrawList list = null!;
    private MaskAtlas masks = null!;
    private GlyphAtlas monoAtlas = null!;
    private GlyphAtlas colorAtlas = null!;
    private List<PlacedGlyph>? placedGlyphs;
    private float width;
    private float height;
    private int currentClip = -1;

    private enum ClipKind : byte
    {
        Rect,
        RoundRect,
        Shape,
    }

    // A clip as pushed, in device space. Shape clips keep what is needed to rasterize them: either a
    // recorded path or a rect/rounded rect, with the transform in force when they were pushed.
    private struct ClipLayer
    {
        public ClipKind Kind;
        public float MinX, MinY, MaxX, MaxY;
        public float Radius;
        public DrawRecording? Recording;
        public int PathIndex;
        public FillRule Rule;
        public Affine Transform;
        public float LocalX, LocalY, LocalW, LocalH, LocalRadius;
        public int CachedIndex;
    }

    /// <summary>Glyphs skipped because their quad missed the clip, since <see cref="Begin"/>.</summary>
    public int CulledGlyphs { get; private set; }

    /// <summary>Draws dropped because the mask atlas was full, since <see cref="Begin"/>.</summary>
    public int DroppedMasks { get; private set; }

    /// <summary>
    /// Starts a frame: resets <paramref name="target"/> for a <paramref name="targetWidth"/> ×
    /// <paramref name="targetHeight"/> target. <paramref name="placed"/>, when given, receives where
    /// every glyph was placed.
    /// </summary>
    public void Begin(DrawList target, uint targetWidth, uint targetHeight, MaskAtlas maskAtlas,
        GlyphAtlas mono, GlyphAtlas color, List<PlacedGlyph>? placed = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(maskAtlas);
        ArgumentNullException.ThrowIfNull(mono);
        ArgumentNullException.ThrowIfNull(color);
        list = target;
        masks = maskAtlas;
        if (uniformGeneration != maskAtlas.ContentGeneration)
        {
            uniformTiles.Clear();
            uniformGeneration = maskAtlas.ContentGeneration;
        }
        monoAtlas = mono;
        colorAtlas = color;
        placedGlyphs = placed;
        width = targetWidth;
        height = targetHeight;
        target.Reset(targetWidth, targetHeight);
        clipStack.Clear();
        currentClip = 0;
        CulledGlyphs = 0;
        DroppedMasks = 0;
    }

    /// <summary>Replays <paramref name="recording"/> at the top level of the frame.</summary>
    public void Replay(DrawRecording recording) => Replay(recording, 0f, 0f, 1f);

    /// <summary>Finishes the frame (assigns batches); the list is ready to execute.</summary>
    public void End()
    {
        list.Finish();
    }

    private void Replay(DrawRecording recording, float offsetX, float offsetY, float opacity)
    {
        ArgumentNullException.ThrowIfNull(recording);
        var offset = Affine.Translate(offsetX, offsetY);
        Affine transform = offset;
        int clipDepth = clipStack.Count;
        var transforms = recording.Transforms;
        var paints = recording.Paints;

        foreach (ref readonly var cmd in recording.Commands)
        {
            switch (cmd.Op)
            {
                case DrawRecording.Op.Transform:
                    transform = offset * transforms[cmd.Ref];
                    break;

                case DrawRecording.Op.ClipRect:
                    PushRectClip(transform, cmd.A, cmd.B, cmd.C, cmd.D, 0f);
                    break;

                case DrawRecording.Op.ClipRoundedRect:
                    PushRectClip(transform, cmd.A, cmd.B, cmd.C, cmd.D, ClampRadius(cmd.E, cmd.C, cmd.D));
                    break;

                case DrawRecording.Op.ClipPath:
                    PushPathClip(recording, cmd.Ref, (FillRule)cmd.Flag, transform);
                    break;

                case DrawRecording.Op.PopClip:
                    // Never pop a clip the enclosing replay pushed.
                    if (clipStack.Count > clipDepth)
                    {
                        PopClip();
                    }
                    break;

                case DrawRecording.Op.FillRect:
                    FillRect(transform, cmd.A, cmd.B, cmd.C, cmd.D, paints[cmd.Ref], opacity);
                    break;

                case DrawRecording.Op.FillRoundedRect:
                    FillRoundedRect(transform, cmd.A, cmd.B, cmd.C, cmd.D, ClampRadius(cmd.E, cmd.C, cmd.D), paints[cmd.Ref], opacity);
                    break;

                case DrawRecording.Op.Border:
                    Border(transform, cmd.A, cmd.B, cmd.C, cmd.D, ClampRadius(cmd.E, cmd.C, cmd.D), cmd.F, paints[cmd.Ref].Color.WithOpacity(opacity));
                    break;

                case DrawRecording.Op.FillCircle:
                    FillCircle(transform, cmd.A, cmd.B, cmd.C, paints[cmd.Ref], opacity);
                    break;

                case DrawRecording.Op.StrokeCircle:
                    StrokeCircle(transform, cmd.A, cmd.B, cmd.C, cmd.D, paints[cmd.Ref].Color.WithOpacity(opacity));
                    break;

                case DrawRecording.Op.FillSector:
                    FillSector(transform, cmd.A, cmd.B, cmd.C, cmd.D, cmd.E, cmd.F, paints[cmd.Ref].Color.WithOpacity(opacity));
                    break;

                case DrawRecording.Op.StrokeLine:
                    StrokeLine(transform, cmd.A, cmd.B, cmd.C, cmd.D, recording.Strokes[cmd.Ref2], paints[cmd.Ref].Color.WithOpacity(opacity));
                    break;

                case DrawRecording.Op.StrokeArc:
                    StrokeArc(transform, cmd.A, cmd.B, cmd.C, cmd.D, cmd.E, recording.Strokes[cmd.Ref2], paints[cmd.Ref].Color.WithOpacity(opacity));
                    break;

                case DrawRecording.Op.FillPath:
                    FillPath(recording, cmd.Ref, (FillRule)cmd.Flag, transform, paints[cmd.Ref2], opacity);
                    break;

                case DrawRecording.Op.StrokePath:
                    StrokePath(recording, cmd.Ref, recording.Strokes[cmd.Ref3], transform, paints[cmd.Ref2], opacity);
                    break;

                case DrawRecording.Op.Shadow:
                    Shadow(transform, cmd.A, cmd.B, cmd.C, cmd.D, cmd.E, cmd.F, paints[cmd.Ref].Color.WithOpacity(opacity));
                    break;

                case DrawRecording.Op.BackdropBlur:
                    BackdropBlur(cmd.A + offsetX, cmd.B + offsetY, cmd.C, cmd.D, cmd.E, cmd.F, paints[cmd.Ref].Color, opacity);
                    break;

                case DrawRecording.Op.Image:
                    {
                        var (handle, image) = recording.Images[cmd.Ref];
                        Image(transform, handle, image, cmd.A, cmd.B, cmd.C, cmd.D, cmd.E * opacity);
                        break;
                    }

                case DrawRecording.Op.Glyphs:
                    Glyphs(recording.GlyphRuns[cmd.Ref], offsetX, offsetY, opacity);
                    break;

                case DrawRecording.Op.Layer:
                    {
                        var layer = recording.Layers[cmd.Ref];
                        Replay(layer.Recording, offsetX + layer.OffsetX, offsetY + layer.OffsetY, opacity * layer.Opacity);
                        break;
                    }
            }
        }

        // A recording that leaves clips pushed does not leak them into its caller.
        while (clipStack.Count > clipDepth)
        {
            PopClip();
        }
    }

    private static float ClampRadius(float radius, float w, float h) => Math.Max(0f, Math.Min(radius, Math.Min(w, h) * 0.5f));

    // ── Transforms ──────────────────────────────────────────────────────

    private static bool IsAxisAligned(in Affine t) => t.M01 == 0 && t.M10 == 0;

    // Rotation (or reflection) plus uniform scale: distances scale by one factor in every direction.
    private static bool IsSimilarity(in Affine t, out float scale)
    {
        double a = t.M00, b = t.M01, c = t.M10, d = t.M11;
        double col0 = a * a + c * c;
        double col1 = b * b + d * d;
        scale = (float)Math.Sqrt(col0);
        double tolerance = 1e-6 * Math.Max(col0, 1e-12);
        return Math.Abs(col0 - col1) <= tolerance && Math.Abs(a * b + c * d) <= tolerance && col0 > 0;
    }

    // Sets the instance's device → local frame to the inverse of the similarity transform t.
    private static void SetFrame(ref ShapeInstance inst, in Affine t, float scale)
    {
        var inv = t.Inverse();
        inst.FrameA = (float)inv.M00;
        inst.FrameB = (float)inv.M01;
        inst.FrameC = (float)inv.M10;
        inst.FrameD = (float)inv.M11;
        inst.FrameTx = (float)inv.M02;
        inst.FrameTy = (float)inv.M12;
        inst.Scale = scale;
    }

    private static void DeviceBounds(in Affine t, float x, float y, float w, float h,
        out float minX, out float minY, out float maxX, out float maxY)
    {
        var p0 = t.Transform(new Point(x, y));
        var p1 = t.Transform(new Point(x + w, y));
        var p2 = t.Transform(new Point(x, y + h));
        var p3 = t.Transform(new Point(x + w, y + h));
        minX = (float)Math.Min(Math.Min(p0.X, p1.X), Math.Min(p2.X, p3.X));
        minY = (float)Math.Min(Math.Min(p0.Y, p1.Y), Math.Min(p2.Y, p3.Y));
        maxX = (float)Math.Max(Math.Max(p0.X, p1.X), Math.Max(p2.X, p3.X));
        maxY = (float)Math.Max(Math.Max(p0.Y, p1.Y), Math.Max(p2.Y, p3.Y));
    }

    // Intersects a raster extent with the current hard clip rect and the target; false when empty.
    private bool ClipExtent(ref float minX, ref float minY, ref float maxX, ref float maxY, uint clipIndex)
    {
        ref readonly var clip = ref list.Clips[(int)clipIndex];
        minX = Math.Max(minX, clip.MinX);
        minY = Math.Max(minY, clip.MinY);
        maxX = Math.Min(maxX, clip.MaxX);
        maxY = Math.Min(maxY, clip.MaxY);
        return maxX > minX && maxY > minY;
    }

    // ── Clips ───────────────────────────────────────────────────────────

    private void PushRectClip(in Affine t, float x, float y, float w, float h, float radius)
    {
        var layer = new ClipLayer { CachedIndex = -1 };
        if (IsAxisAligned(t) && (radius <= 0f || Math.Abs(Math.Abs(t.M00) - Math.Abs(t.M11)) < 1e-6))
        {
            DeviceBounds(t, x, y, w, h, out layer.MinX, out layer.MinY, out layer.MaxX, out layer.MaxY);
            layer.Kind = radius > 0f ? ClipKind.RoundRect : ClipKind.Rect;
            layer.Radius = radius * (float)Math.Abs(t.M00);
        }
        else
        {
            DeviceBounds(t, x, y, w, h, out layer.MinX, out layer.MinY, out layer.MaxX, out layer.MaxY);
            layer.Kind = ClipKind.Shape;
            layer.Transform = t;
            layer.PathIndex = -1;
            layer.LocalX = x;
            layer.LocalY = y;
            layer.LocalW = w;
            layer.LocalH = h;
            layer.LocalRadius = radius;
            layer.Rule = FillRule.NonZero;
        }
        clipStack.Add(layer);
        currentClip = -1;
    }

    private void PushPathClip(DrawRecording recording, int pathIndex, FillRule rule, in Affine t)
    {
        var path = recording.GetPath(pathIndex);
        PathFlattener.Flatten(recording.Verbs.Slice(path.VerbStart, path.VerbCount), recording.Coords.Slice(path.CoordStart, path.CoordCount), t, FlattenTolerance, flat);
        var layer = new ClipLayer
        {
            Kind = ClipKind.Shape,
            Recording = recording,
            PathIndex = pathIndex,
            Rule = rule,
            Transform = t,
            CachedIndex = -1,
        };
        if (!flat.TryGetBounds(out layer.MinX, out layer.MinY, out layer.MaxX, out layer.MaxY))
        {
            layer.MinX = layer.MinY = layer.MaxX = layer.MaxY = 0f;
        }
        clipStack.Add(layer);
        currentClip = -1;
    }

    private void PopClip()
    {
        clipStack.RemoveAt(clipStack.Count - 1);
        currentClip = clipStack.Count == 0 ? 0 : clipStack[^1].CachedIndex;
    }

    /// <summary>The clip-table index of the current clip stack, adding the entry on first use.</summary>
    private uint CurrentClip()
    {
        if (currentClip >= 0)
        {
            return (uint)currentClip;
        }

        var stack = CollectionsMarshal.AsSpan(clipStack);
        var entry = new ClipEntry { MinX = 0, MinY = 0, MaxX = width, MaxY = height };
        int roundCount = 0;
        int shapeCount = 0;
        int roundIndex = -1;
        for (int i = 0; i < stack.Length; i++)
        {
            ref readonly var layer = ref stack[i];
            entry.MinX = Math.Max(entry.MinX, layer.MinX);
            entry.MinY = Math.Max(entry.MinY, layer.MinY);
            entry.MaxX = Math.Min(entry.MaxX, layer.MaxX);
            entry.MaxY = Math.Min(entry.MaxY, layer.MaxY);
            if (layer.Kind == ClipKind.RoundRect)
            {
                roundCount++;
                roundIndex = i;
            }
            else if (layer.Kind == ClipKind.Shape)
            {
                shapeCount++;
            }
        }
        if (entry.MaxX <= entry.MinX || entry.MaxY <= entry.MinY)
        {
            entry.MaxX = entry.MinX;
            entry.MaxY = entry.MinY;
        }

        if (shapeCount == 0 && roundCount == 1)
        {
            ref readonly var round = ref stack[roundIndex];
            entry.HasRound = 1;
            entry.RoundMinX = round.MinX;
            entry.RoundMinY = round.MinY;
            entry.RoundMaxX = round.MaxX;
            entry.RoundMaxY = round.MaxY;
            entry.RoundRadius = round.Radius;
        }
        else if (shapeCount + roundCount > 0 && entry.MaxX > entry.MinX)
        {
            BuildClipMask(ref entry, stack);
        }

        uint index = stack.Length == 0 ? 0 : list.AddClip(entry);
        currentClip = (int)index;
        if (stack.Length > 0)
        {
            stack[^1].CachedIndex = currentClip;
        }
        return index;
    }

    // Rasterizes the product of every non-rect clip over the hard rect into the mask atlas, as
    // ClipMaskTile-square tiles: tiles wholly inside or outside the clip are stored as a value, the
    // rest as atlas texels, so a clip mask of any size fits.
    private void BuildClipMask(ref ClipEntry entry, ReadOnlySpan<ClipLayer> stack)
    {
        // The mask covers the non-rect layers' own bounds, anchored at their integer floor, so a
        // clip that moves by whole pixels (scrolled) keeps its masks; parts outside the hard rect
        // are never read (the hard rect is tested first) and stay uniform 0 without rasterizing.
        // A very large clip shape is anchored at the visible region instead (its tile table
        // would otherwise grow with the shape).
        float uMinX = float.MaxValue, uMinY = float.MaxValue, uMaxX = float.MinValue, uMaxY = float.MinValue;
        foreach (ref readonly var layer in stack)
        {
            if (layer.Kind == ClipKind.Rect)
            {
                continue;
            }
            uMinX = Math.Min(uMinX, layer.MinX);
            uMinY = Math.Min(uMinY, layer.MinY);
            uMaxX = Math.Max(uMaxX, layer.MaxX);
            uMaxY = Math.Max(uMaxY, layer.MaxY);
        }
        const float MaxAnchoredExtent = 8192f;
        if (!(uMaxX - uMinX <= MaxAnchoredExtent && uMaxY - uMinY <= MaxAnchoredExtent))
        {
            uMinX = entry.MinX;
            uMinY = entry.MinY;
            uMaxX = entry.MaxX;
            uMaxY = entry.MaxY;
        }
        int x0 = (int)MathF.Floor(uMinX);
        int y0 = (int)MathF.Floor(uMinY);
        int x1 = (int)MathF.Ceiling(uMaxX);
        int y1 = (int)MathF.Ceiling(uMaxY);
        int w = x1 - x0;
        int h = y1 - y0;
        if (w <= 0 || h <= 0)
        {
            return;
        }
        const int tile = MaskAtlas.ClipMaskTile;
        int columns = (w + tile - 1) / tile;
        int rows = (h + tile - 1) / tile;

        hasher.Reset();
        Hash(w);
        Hash(h);
        foreach (ref readonly var layer in stack)
        {
            if (layer.Kind == ClipKind.Rect)
            {
                continue;
            }
            HashClipLayer(layer, x0, y0);
        }
        ulong clipKey = hasher.GetCurrentHashAsUInt64();

        tileScratch.Clear();
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < columns; col++)
            {
                int tx = x0 + col * tile;
                int ty = y0 + row * tile;
                int tw = Math.Min(tile, x1 - tx);
                int th = Math.Min(tile, y1 - ty);
                if (tx + tw <= entry.MinX || tx >= entry.MaxX || ty + th <= entry.MinY || ty >= entry.MaxY)
                {
                    tileScratch.Add(MaskTileEntry.Uniform(0));
                    continue;
                }
                ulong key = TileKey(clipKey, FillRule.NonZero, tx - x0, ty - y0, tw, th);
                if (uniformTiles.TryGetValue(key, out byte value))
                {
                    tileScratch.Add(MaskTileEntry.Uniform(value));
                    continue;
                }
                if (masks.TryLookup(key, out var region))
                {
                    tileScratch.Add(MaskTileEntry.Atlas(region));
                    continue;
                }
                var combined = clipCoverage.AsSpan(0, tw * th);
                RasterizeClipTile(stack, combined, tx, ty, tw, th);
                if (IsUniform(combined, out value))
                {
                    uniformTiles[key] = value;
                    tileScratch.Add(MaskTileEntry.Uniform(value));
                    continue;
                }
                if (!masks.TryInsert(key, combined, tw, th, out region))
                {
                    // Every page is full: this frame's tile reads as outside the clip (the owner
                    // resets the atlas before the next frame).
                    DroppedMasks++;
                    tileScratch.Add(MaskTileEntry.Uniform(0));
                    continue;
                }
                tileScratch.Add(MaskTileEntry.Atlas(region));
            }
        }

        entry.HasMask = 1;
        entry.MaskOriginX = x0;
        entry.MaskOriginY = y0;
        entry.MaskTileStart = list.AddMaskTiles(CollectionsMarshal.AsSpan(tileScratch));
        entry.MaskTileColumns = columns;
        entry.MaskWidth = w;
        entry.MaskHeight = h;
    }
    // The product of every non-rect clip layer's coverage over one tile.
    private void RasterizeClipTile(ReadOnlySpan<ClipLayer> stack, Span<byte> combined, int tx, int ty, int tw, int th)
    {
        combined.Fill(255);
        var layerCoverage = coverage.AsSpan(0, tw * th);
        foreach (ref readonly var layer in stack)
        {
            if (layer.Kind == ClipKind.Rect)
            {
                continue;
            }
            FlattenClipLayer(layer);
            rasterizer.Reset(tw, th);
            rasterizer.AddPath(flat, -tx, -ty);
            rasterizer.Resolve(layerCoverage, layer.Rule);
            for (int i = 0; i < combined.Length; i++)
            {
                combined[i] = (byte)((combined[i] * layerCoverage[i] + 127) / 255);
            }
        }
    }

    private static bool IsUniform(ReadOnlySpan<byte> texels, out byte value)
    {
        value = texels[0];
        return texels.IndexOfAnyExcept(value) < 0;
    }
    private void FlattenClipLayer(in ClipLayer layer)
    {
        if (layer.Kind == ClipKind.RoundRect)
        {
            RoundedRectFlat(Affine.Identity, layer.MinX, layer.MinY, layer.MaxX - layer.MinX, layer.MaxY - layer.MinY, layer.Radius, flat);
        }
        else if (layer.PathIndex < 0)
        {
            RoundedRectFlat(layer.Transform, layer.LocalX, layer.LocalY, layer.LocalW, layer.LocalH, layer.LocalRadius, flat);
        }
        else
        {
            var recording = layer.Recording!;
            var path = recording.GetPath(layer.PathIndex);
            PathFlattener.Flatten(recording.Verbs.Slice(path.VerbStart, path.VerbCount), recording.Coords.Slice(path.CoordStart, path.CoordCount), layer.Transform, FlattenTolerance, flat);
        }
    }

    // A clip layer's geometry relative to the mask's anchor pixel.
    private void HashClipLayer(in ClipLayer layer, int anchorX, int anchorY)
    {
        Hash((int)layer.Kind);
        Hash((int)layer.Rule);
        if (layer.Kind == ClipKind.RoundRect)
        {
            Span<double> rect = [layer.MinX - (double)anchorX, layer.MinY - (double)anchorY, layer.MaxX - (double)anchorX, layer.MaxY - (double)anchorY];
            hasher.Append(MemoryMarshal.AsBytes(rect));
            Hash(layer.Radius);
            return;
        }
        HashLinear(layer.Transform);
        HashAnchoredTranslation(layer.Transform, anchorX, anchorY);
        if (layer.PathIndex < 0)
        {
            Hash(layer.LocalX);
            Hash(layer.LocalY);
            Hash(layer.LocalW);
            Hash(layer.LocalH);
            Hash(layer.LocalRadius);
            return;
        }
        var recording = layer.Recording!;
        var path = recording.GetPath(layer.PathIndex);
        hasher.Append(recording.Verbs.Slice(path.VerbStart, path.VerbCount));
        hasher.Append(MemoryMarshal.AsBytes(recording.Coords.Slice(path.CoordStart, path.CoordCount)));
    }

    // ── Hashing (mask keys) ─────────────────────────────────────────────

    private void Hash(int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        hasher.Append(bytes);
    }

    private void Hash(float value) => Hash(BitConverter.SingleToInt32Bits(value));

    // A mask key covers the transform's linear part; the translation enters relative to the
    // integer pixel the mask is anchored at (HashAnchoredTranslation), so content that moves by
    // whole pixels — scrolling — reuses its masks instead of rasterizing new ones every frame.
    private void HashLinear(in Affine t)
    {
        Span<double> values = [t.M00, t.M01, t.M10, t.M11];
        hasher.Append(MemoryMarshal.AsBytes(values));
    }

    private void HashAnchoredTranslation(in Affine t, int anchorX, int anchorY)
    {
        Span<double> values = [t.M02 - anchorX, t.M12 - anchorY];
        hasher.Append(MemoryMarshal.AsBytes(values));
    }

    private void HashStroke(in StrokeParameters s)
    {
        Hash(s.Width);
        Hash((int)s.Cap | ((int)s.Join << 8));
        Hash(s.MiterLimit);
        Hash(s.DashOn);
        Hash(s.DashOff);
        Hash(s.DashOffset);
    }

    // ── Paint ───────────────────────────────────────────────────────────

    // Writes the paint into the instance: solid colour, or a gradient-table entry with the
    // instance colour as an alpha multiplier. False when the paint draws nothing.
    private bool ApplyPaint(ref ShapeInstance inst, in ComposePaint paint, in Affine t, float opacity)
    {
        if (paint.Kind == GradientKind.None)
        {
            var c = paint.Color;
            float a = c.A * opacity;
            if (a <= 0f)
            {
                return false;
            }
            inst.R0 = c.R;
            inst.G0 = c.G;
            inst.B0 = c.B;
            inst.A0 = a;
            inst.PaintIndex = 0;
            return true;
        }

        var stops = paint.Stops;
        if (stops is null || stops.Length == 0 || opacity <= 0f)
        {
            return false;
        }
        inst.R0 = 1f;
        inst.G0 = 1f;
        inst.B0 = 1f;
        inst.A0 = opacity;
        inst.PaintIndex = AddGradient(paint, t);
        return true;
    }

    private uint AddGradient(in ComposePaint paint, in Affine t)
    {
        var stops = paint.Stops!;
        stopScratch.Clear();
        float lastOffset = 0f;
        foreach (var stop in stops)
        {
            float offset = Math.Clamp(stop.Offset, lastOffset, 1f);
            lastOffset = offset;
            var c = stop.Color;
            stopScratch.Add(new GradientStopEntry { Offset = offset, R = c.R * c.A, G = c.G * c.A, B = c.B * c.A, A = c.A });
        }

        var entry = new GradientEntry { Kind = paint.Kind };
        double det = t.Determinant();
        if (Math.Abs(det) < 1e-12)
        {
            entry.Kind = GradientKind.Linear;
            entry.A = entry.B = entry.C = entry.D = 0f;
            entry.Tx = 1f;
            return list.AddGradient(entry, CollectionsMarshal.AsSpan(stopScratch));
        }

        var inv = t.Inverse();
        switch (paint.Kind)
        {
            case GradientKind.Linear:
                {
                    double vx = paint.X1 - paint.X0;
                    double vy = paint.Y1 - paint.Y0;
                    double len2 = vx * vx + vy * vy;
                    if (len2 < 1e-12)
                    {
                        // Degenerate: every pixel takes the last stop.
                        entry.Tx = 1f;
                        break;
                    }
                    double ux = vx / len2, uy = vy / len2;
                    entry.A = (float)(ux * inv.M00 + uy * inv.M10);
                    entry.B = (float)(ux * inv.M01 + uy * inv.M11);
                    entry.Tx = (float)(ux * (inv.M02 - paint.X0) + uy * (inv.M12 - paint.Y0));
                    break;
                }
            case GradientKind.Radial:
                {
                    double r = paint.X1;
                    if (r <= 1e-6)
                    {
                        entry.Kind = GradientKind.Linear;
                        entry.Tx = 1f;
                        break;
                    }
                    entry.A = (float)(inv.M00 / r);
                    entry.B = (float)(inv.M01 / r);
                    entry.C = (float)(inv.M10 / r);
                    entry.D = (float)(inv.M11 / r);
                    entry.Tx = (float)((inv.M02 - paint.X0) / r);
                    entry.Ty = (float)((inv.M12 - paint.Y0) / r);
                    break;
                }
            case GradientKind.Sweep:
                entry.A = (float)inv.M00;
                entry.B = (float)inv.M01;
                entry.C = (float)inv.M10;
                entry.D = (float)inv.M11;
                entry.Tx = (float)(inv.M02 - paint.X0);
                entry.Ty = (float)(inv.M12 - paint.Y0);
                entry.StartAngle = paint.X1;
                break;
        }
        return list.AddGradient(entry, CollectionsMarshal.AsSpan(stopScratch));
    }

    private static ShapeInstance SolidInstance(ComposeColor color)
    {
        var inst = new ShapeInstance { R0 = color.R, G0 = color.G, B0 = color.B, A0 = color.A };
        inst.SetIdentityFrame();
        return inst;
    }

    // Finalizes the raster extent (geometry bounds grown by margin, clipped) and places the shape.
    private void Place(ref ShapeInstance inst, float minX, float minY, float maxX, float maxY, float margin)
    {
        uint clip = CurrentClip();
        minX -= margin;
        minY -= margin;
        maxX += margin;
        maxY += margin;
        if (!ClipExtent(ref minX, ref minY, ref maxX, ref maxY, clip))
        {
            return;
        }
        inst.MinX = minX;
        inst.MinY = minY;
        inst.MaxX = maxX;
        inst.MaxY = maxY;
        inst.ClipIndex = clip;
        list.AddShape(inst);
    }

    // ── Shapes ──────────────────────────────────────────────────────────

    private void FillRect(in Affine t, float x, float y, float w, float h, in ComposePaint paint, float opacity)
    {
        if (!(w > 0f) || !(h > 0f))
        {
            return;
        }
        var inst = SolidInstance(default);
        if (!ApplyPaint(ref inst, paint, t, opacity))
        {
            return;
        }

        if (IsAxisAligned(t))
        {
            // UI rects keep hard edges: a pixel is in when its centre is (rasterizer rule).
            DeviceBounds(t, x, y, w, h, out float minX, out float minY, out float maxX, out float maxY);
            inst.Type = ShapeType.Rect;
            inst.P0 = minX;
            inst.P1 = minY;
            inst.P2 = maxX;
            inst.P3 = maxY;
            Place(ref inst, minX, minY, maxX, maxY, 0f);
            return;
        }
        if (IsSimilarity(t, out float scale))
        {
            DeviceBounds(t, x, y, w, h, out float minX, out float minY, out float maxX, out float maxY);
            SetFrame(ref inst, t, scale);
            inst.Type = ShapeType.RoundedRect;
            inst.P0 = x;
            inst.P1 = y;
            inst.P2 = x + w;
            inst.P3 = y + h;
            inst.Q0 = 0f;
            Place(ref inst, minX, minY, maxX, maxY, AaMargin);
            return;
        }
        RoundedRectFlat(t, x, y, w, h, 0f, flat);
        FillMask(flat, FillRule.NonZero, t, MaskKeyForShape(t, 1, x, y, w, h, 0f, 0f), inst);
    }

    private void FillRoundedRect(in Affine t, float x, float y, float w, float h, float radius, in ComposePaint paint, float opacity)
    {
        if (!(w > 0f) || !(h > 0f))
        {
            return;
        }
        var inst = SolidInstance(default);
        if (!ApplyPaint(ref inst, paint, t, opacity))
        {
            return;
        }

        bool aligned = IsAxisAligned(t);
        if (aligned && Math.Abs(Math.Abs(t.M00) - Math.Abs(t.M11)) < 1e-6)
        {
            DeviceBounds(t, x, y, w, h, out float minX, out float minY, out float maxX, out float maxY);
            inst.Type = ShapeType.RoundedRect;
            inst.P0 = minX;
            inst.P1 = minY;
            inst.P2 = maxX;
            inst.P3 = maxY;
            inst.Q0 = radius * (float)Math.Abs(t.M00);
            Place(ref inst, minX, minY, maxX, maxY, AaMargin);
            return;
        }
        if (!aligned && IsSimilarity(t, out float scale))
        {
            DeviceBounds(t, x, y, w, h, out float minX, out float minY, out float maxX, out float maxY);
            SetFrame(ref inst, t, scale);
            inst.Type = ShapeType.RoundedRect;
            inst.P0 = x;
            inst.P1 = y;
            inst.P2 = x + w;
            inst.P3 = y + h;
            inst.Q0 = radius;
            Place(ref inst, minX, minY, maxX, maxY, AaMargin);
            return;
        }
        RoundedRectFlat(t, x, y, w, h, radius, flat);
        FillMask(flat, FillRule.NonZero, t, MaskKeyForShape(t, 2, x, y, w, h, radius, 0f), inst);
    }

    private void Border(in Affine t, float x, float y, float w, float h, float radius, float borderWidth, ComposeColor color)
    {
        if (!(w > 0f) || !(h > 0f) || !(borderWidth > 0f) || color.A <= 0f)
        {
            return;
        }
        float half = Math.Min(w, h) * 0.5f;
        if (borderWidth >= half)
        {
            if (radius > 0f)
            {
                FillRoundedRect(t, x, y, w, h, radius, ComposePaint.Solid(color), 1f);
            }
            else
            {
                FillRect(t, x, y, w, h, ComposePaint.Solid(color), 1f);
            }
            return;
        }

        if (radius <= 0f && IsAxisAligned(t))
        {
            // Square borders stay crisp: four hard-edged rects inside the rect, as CSS draws them.
            var paint = ComposePaint.Solid(color);
            FillRect(t, x, y, w, borderWidth, paint, 1f);
            FillRect(t, x, y + h - borderWidth, w, borderWidth, paint, 1f);
            FillRect(t, x, y + borderWidth, borderWidth, h - 2 * borderWidth, paint, 1f);
            FillRect(t, x + w - borderWidth, y + borderWidth, borderWidth, h - 2 * borderWidth, paint, 1f);
            return;
        }

        var inst = SolidInstance(color);
        bool aligned = IsAxisAligned(t);
        if (aligned && Math.Abs(Math.Abs(t.M00) - Math.Abs(t.M11)) < 1e-6)
        {
            float s = (float)Math.Abs(t.M00);
            DeviceBounds(t, x, y, w, h, out float minX, out float minY, out float maxX, out float maxY);
            inst.Type = ShapeType.Border;
            inst.P0 = minX;
            inst.P1 = minY;
            inst.P2 = maxX;
            inst.P3 = maxY;
            inst.Q0 = radius * s;
            inst.Q1 = borderWidth * s;
            inst.Q2 = Math.Max(radius - borderWidth, 0f) * s;
            Place(ref inst, minX, minY, maxX, maxY, AaMargin);
            return;
        }
        if (!aligned && IsSimilarity(t, out float scale))
        {
            DeviceBounds(t, x, y, w, h, out float minX, out float minY, out float maxX, out float maxY);
            SetFrame(ref inst, t, scale);
            inst.Type = ShapeType.Border;
            inst.P0 = x;
            inst.P1 = y;
            inst.P2 = x + w;
            inst.P3 = y + h;
            inst.Q0 = radius;
            inst.Q1 = borderWidth;
            inst.Q2 = Math.Max(radius - borderWidth, 0f);
            Place(ref inst, minX, minY, maxX, maxY, AaMargin);
            return;
        }

        // Outer rounded rect minus the inner one, as two contours of opposite winding.
        RoundedRectFlat(t, x, y, w, h, radius, flat);
        strokeOutput.Clear();
        RoundedRectFlat(t, x + borderWidth, y + borderWidth, w - 2 * borderWidth, h - 2 * borderWidth, Math.Max(radius - borderWidth, 0f), strokeOutput);
        AppendReversed(strokeOutput, flat);
        FillMask(flat, FillRule.NonZero, t, MaskKeyForShape(t, 3, x, y, w, h, radius, borderWidth), inst);
    }

    private void FillCircle(in Affine t, float cx, float cy, float r, in ComposePaint paint, float opacity)
    {
        if (!(r > 0f))
        {
            return;
        }
        var inst = SolidInstance(default);
        if (!ApplyPaint(ref inst, paint, t, opacity))
        {
            return;
        }
        if (IsSimilarity(t, out float scale))
        {
            var c = t.Transform(new Point(cx, cy));
            float rd = r * scale;
            inst.Type = ShapeType.Circle;
            inst.P0 = (float)c.X;
            inst.P1 = (float)c.Y;
            inst.P2 = rd;
            Place(ref inst, inst.P0 - rd, inst.P1 - rd, inst.P0 + rd, inst.P1 + rd, AaMargin);
            return;
        }
        EllipseFlat(t, cx, cy, r, flat);
        FillMask(flat, FillRule.NonZero, t, MaskKeyForShape(t, 4, cx, cy, r, 0f, 0f, 0f), inst);
    }

    private void StrokeCircle(in Affine t, float cx, float cy, float r, float strokeWidth, ComposeColor color)
    {
        if (!(r > 0f) || !(strokeWidth > 0f) || color.A <= 0f)
        {
            return;
        }
        var inst = SolidInstance(color);
        if (IsSimilarity(t, out float scale))
        {
            var c = t.Transform(new Point(cx, cy));
            float outer = (r + strokeWidth * 0.5f) * scale;
            inst.Type = ShapeType.Ring;
            inst.P0 = (float)c.X;
            inst.P1 = (float)c.Y;
            inst.P2 = outer;
            inst.P3 = strokeWidth * scale;
            Place(ref inst, inst.P0 - outer, inst.P1 - outer, inst.P0 + outer, inst.P1 + outer, AaMargin);
            return;
        }
        EllipseFlat(Affine.Identity, cx, cy, r, strokeInput);
        StrokeMask(strokeInput, StrokeParameters.Solid(strokeWidth), t, MaskKeyForShape(t, 5, cx, cy, r, strokeWidth, 0f, 0f), inst);
    }

    private void FillSector(in Affine t, float cx, float cy, float outer, float inner, float start, float sweep, ComposeColor color)
    {
        if (!(outer > 0f) || color.A <= 0f || Math.Abs(sweep) < 1e-6f)
        {
            return;
        }
        var inst = SolidInstance(color);
        if (IsSimilarity(t, out float scale))
        {
            var c = t.Transform(new Point(cx, cy));
            MapAngles(t, ref start, ref sweep);
            inst.Type = ShapeType.Sector;
            inst.P0 = (float)c.X;
            inst.P1 = (float)c.Y;
            inst.P2 = outer * scale;
            inst.P3 = Math.Max(inner, 0f) * scale;
            inst.Q0 = start;
            inst.Q1 = sweep;
            inst.Q2 = 0f;
            float ro = inst.P2;
            Place(ref inst, inst.P0 - ro, inst.P1 - ro, inst.P0 + ro, inst.P1 + ro, AaMargin);
            return;
        }
        SectorFlat(cx, cy, outer, inner, start, sweep, flat);
        flat.Transform(t);
        FillMask(flat, FillRule.NonZero, t, MaskKeyForShape(t, 6, cx, cy, outer, inner, start, sweep), inst);
    }

    // Maps start/sweep angles through a similarity transform (rotation, and reflection's mirror).
    private static void MapAngles(in Affine t, ref float start, ref float sweep)
    {
        double rotation = Math.Atan2(t.M10, t.M00);
        if (t.Determinant() < 0)
        {
            start = (float)(rotation - start);
            sweep = -sweep;
        }
        else
        {
            start = (float)(start + rotation);
        }
    }

    private void StrokeLine(in Affine t, float x0, float y0, float x1, float y1, in StrokeParameters stroke, ComposeColor color)
    {
        if (!(stroke.Width > 0f) || color.A <= 0f)
        {
            return;
        }
        var inst = SolidInstance(color);
        var d0 = t.Transform(new Point(x0, y0));
        var d1 = t.Transform(new Point(x1, y1));
        // Degenerate in device space (a zero-length segment, or one a transform collapses): a butt
        // cap draws nothing; round and square caps draw the cap alone, a dot of the stroke width
        // (SVG's rule). The analytic line has no direction to measure distance along here.
        float dx = (float)(d1.X - d0.X), dy = (float)(d1.Y - d0.Y);
        if (!(dx * dx + dy * dy > 1e-8f))
        {
            if (stroke.Cap == StrokeCap.Butt || !IsSimilarity(t, out float dotScale))
            {
                return;
            }
            float r = stroke.Width * 0.5f * dotScale;
            if (stroke.Cap == StrokeCap.Round)
            {
                inst.Type = ShapeType.Circle;
                inst.P0 = (float)d0.X;
                inst.P1 = (float)d0.Y;
                inst.P2 = r;
            }
            else
            {
                inst.Type = ShapeType.RoundedRect;
                inst.P0 = (float)d0.X - r;
                inst.P1 = (float)d0.Y - r;
                inst.P2 = (float)d0.X + r;
                inst.P3 = (float)d0.Y + r;
                inst.Q0 = 0f;
            }
            Place(ref inst, (float)d0.X - r, (float)d0.Y - r, (float)d0.X + r, (float)d0.Y + r, AaMargin);
            return;
        }
        if (!stroke.IsDashed && IsSimilarity(t, out float scale))
        {
            var p0 = d0;
            var p1 = d1;
            float hw = stroke.Width * 0.5f * scale;
            inst.Type = ShapeType.Line;
            inst.P0 = (float)p0.X;
            inst.P1 = (float)p0.Y;
            inst.P2 = (float)p1.X;
            inst.P3 = (float)p1.Y;
            inst.Q0 = hw;
            inst.Q1 = (float)stroke.Cap;
            float extend = stroke.Cap == StrokeCap.Butt ? 0f : hw;
            float ext = hw + extend;
            Place(ref inst, Math.Min(inst.P0, inst.P2) - ext, Math.Min(inst.P1, inst.P3) - ext,
                Math.Max(inst.P0, inst.P2) + ext, Math.Max(inst.P1, inst.P3) + ext, AaMargin);
            return;
        }
        strokeInput.Clear();
        strokeInput.MoveTo(x0, y0);
        strokeInput.LineTo(x1, y1);
        strokeInput.EndContour(closed: false);
        StrokeMask(strokeInput, stroke, t, MaskKeyForShape(t, 7, x0, y0, x1, y1, 0f, 0f, stroke), inst);
    }

    private void StrokeArc(in Affine t, float cx, float cy, float r, float start, float sweep, in StrokeParameters stroke, ComposeColor color)
    {
        if (!(stroke.Width > 0f) || color.A <= 0f || !(r > 0f) || Math.Abs(sweep) < 1e-6f)
        {
            return;
        }
        var inst = SolidInstance(color);
        if (!stroke.IsDashed && stroke.Cap != StrokeCap.Square && IsSimilarity(t, out float scale))
        {
            var c = t.Transform(new Point(cx, cy));
            float hw = stroke.Width * 0.5f;
            MapAngles(t, ref start, ref sweep);
            inst.Type = ShapeType.Sector;
            inst.P0 = (float)c.X;
            inst.P1 = (float)c.Y;
            inst.P2 = (r + hw) * scale;
            inst.P3 = Math.Max(r - hw, 0f) * scale;
            inst.Q0 = start;
            inst.Q1 = sweep;
            inst.Q2 = stroke.Cap == StrokeCap.Round ? 1f : 0f;
            float ro = inst.P2;
            Place(ref inst, inst.P0 - ro, inst.P1 - ro, inst.P0 + ro, inst.P1 + ro, AaMargin);
            return;
        }
        ArcFlat(cx, cy, r, start, sweep, FlattenTolerance / Math.Max(MaxScale(t), 1e-3f), strokeInput);
        StrokeMask(strokeInput, stroke, t, MaskKeyForShape(t, 8, cx, cy, r, start, sweep, 0f, stroke), inst);
    }

    private void FillPath(DrawRecording recording, int pathIndex, FillRule rule, in Affine t, in ComposePaint paint, float opacity)
    {
        var path = recording.GetPath(pathIndex);
        var verbs = recording.Verbs.Slice(path.VerbStart, path.VerbCount);
        var coords = recording.Coords.Slice(path.CoordStart, path.CoordCount);

        // The shapes the UI builds as paths render exactly as their analytic shapes.
        if (PathShapes.TryRect(verbs, coords, out float rx, out float ry, out float rw, out float rh))
        {
            FillRect(t, rx, ry, rw, rh, paint, opacity);
            return;
        }
        if (PathShapes.TryCircle(verbs, coords, out float ccx, out float ccy, out float cr))
        {
            FillCircle(t, ccx, ccy, cr, paint, opacity);
            return;
        }
        if (PathShapes.TryRoundedRect(verbs, coords, out rx, out ry, out rw, out rh, out float radius))
        {
            FillRoundedRect(t, rx, ry, rw, rh, radius, paint, opacity);
            return;
        }

        var inst = SolidInstance(default);
        if (!ApplyPaint(ref inst, paint, t, opacity))
        {
            return;
        }
        PathFlattener.Flatten(verbs, coords, t, FlattenTolerance, flat);
        hasher.Reset();
        Hash(10);
        Hash((int)rule);
        HashLinear(t);
        hasher.Append(verbs);
        hasher.Append(MemoryMarshal.AsBytes(coords));
        FillMask(flat, rule, t, hasher.GetCurrentHashAsUInt64(), inst);
    }

    private void StrokePath(DrawRecording recording, int pathIndex, in StrokeParameters stroke, in Affine t, in ComposePaint paint, float opacity)
    {
        if (!(stroke.Width > 0f))
        {
            return;
        }
        var path = recording.GetPath(pathIndex);
        var verbs = recording.Verbs.Slice(path.VerbStart, path.VerbCount);
        var coords = recording.Coords.Slice(path.CoordStart, path.CoordCount);

        if (paint.Kind == GradientKind.None && !stroke.IsDashed)
        {
            if (PathShapes.TryLine(verbs, coords, out float lx0, out float ly0, out float lx1, out float ly1))
            {
                StrokeLine(t, lx0, ly0, lx1, ly1, stroke, paint.Color.WithOpacity(opacity));
                return;
            }
            if (PathShapes.TryCircle(verbs, coords, out float ccx, out float ccy, out float cr))
            {
                StrokeCircle(t, ccx, ccy, cr, stroke.Width, paint.Color.WithOpacity(opacity));
                return;
            }
        }

        var inst = SolidInstance(default);
        if (!ApplyPaint(ref inst, paint, t, opacity))
        {
            return;
        }
        float local = FlattenTolerance / Math.Max(MaxScale(t), 1e-3f);
        PathFlattener.Flatten(verbs, coords, Affine.Identity, local, strokeInput);
        hasher.Reset();
        Hash(11);
        HashStroke(stroke);
        HashLinear(t);
        hasher.Append(verbs);
        hasher.Append(MemoryMarshal.AsBytes(coords));
        StrokeMask(strokeInput, stroke, t, hasher.GetCurrentHashAsUInt64(), inst);
    }

    private void Shadow(in Affine t, float x, float y, float w, float h, float radius, float sigma, ComposeColor color)
    {
        if (!(w > 0f) || !(h > 0f) || color.A <= 0f)
        {
            return;
        }
        var inst = SolidInstance(color);
        inst.Type = ShapeType.Shadow;
        float corner = Math.Min(radius, Math.Min(w, h) * 0.5f);
        if (IsAxisAligned(t) && Math.Abs(Math.Abs(t.M00) - Math.Abs(t.M11)) < 1e-6)
        {
            float s = (float)Math.Abs(t.M00);
            DeviceBounds(t, x, y, w, h, out float minX, out float minY, out float maxX, out float maxY);
            float sg = (float)ShadowShape.EffectiveSigma(sigma * s);
            inst.P0 = minX;
            inst.P1 = minY;
            inst.P2 = maxX;
            inst.P3 = maxY;
            inst.Q0 = corner * s;
            inst.Q1 = sg;
            float extent = (float)ShadowShape.Extent(sg);
            Place(ref inst, minX - extent, minY - extent, maxX + extent, maxY + extent, 0f);
            return;
        }

        // Rotated (or otherwise transformed): evaluate in the local frame, where the blur is defined.
        float scale = MathF.Sqrt(MathF.Abs((float)t.Determinant()));
        if (Math.Abs(t.Determinant()) < 1e-12)
        {
            return;
        }
        SetFrame(ref inst, t, scale);
        float localSigma = (float)ShadowShape.EffectiveSigma(sigma * scale) / scale;
        float localExtent = (float)ShadowShape.Extent(localSigma);
        inst.P0 = x;
        inst.P1 = y;
        inst.P2 = x + w;
        inst.P3 = y + h;
        inst.Q0 = corner;
        inst.Q1 = localSigma;
        DeviceBounds(t, x - localExtent, y - localExtent, w + 2 * localExtent, h + 2 * localExtent,
            out float bMinX, out float bMinY, out float bMaxX, out float bMaxY);
        Place(ref inst, bMinX, bMinY, bMaxX, bMaxY, 0f);
    }

    private void BackdropBlur(float x, float y, float w, float h, float radius, float sigma, ComposeColor tint, float opacity)
    {
        if (!(w > 0f) || !(h > 0f) || opacity <= 0f)
        {
            return;
        }
        uint clip = CurrentClip();
        ref readonly var clipEntry = ref list.Clips[(int)clip];
        if (clipEntry.MaxX <= clipEntry.MinX || clipEntry.MaxY <= clipEntry.MinY)
        {
            return;
        }
        list.AddBlur(new BlurInstance
        {
            MinX = x,
            MinY = y,
            MaxX = x + w,
            MaxY = y + h,
            TintR = tint.R,
            TintG = tint.G,
            TintB = tint.B,
            TintA = tint.A,
            Radius = radius,
            Sigma = sigma,
            ClipIndex = clip,
            Opacity = opacity,
        });
    }

    private void Image(in Affine t, int handle, ComposeImage image, float x, float y, float w, float h, float opacity)
    {
        if (!(w > 0f) || !(h > 0f) || opacity <= 0f)
        {
            return;
        }
        // Local rect corner (x + u·w, y + v·h) → device; invert to map device → (u, v).
        var toDevice = t * new Affine(w, 0, 0, h, x, y);
        double det = toDevice.Determinant();
        if (Math.Abs(det) < 1e-12)
        {
            return;
        }
        var toUv = toDevice.Inverse();
        DeviceBounds(t, x, y, w, h, out float minX, out float minY, out float maxX, out float maxY);
        uint clip = CurrentClip();
        // Edges antialias over half a pixel either side.
        minX -= 1f;
        minY -= 1f;
        maxX += 1f;
        maxY += 1f;
        if (!ClipExtent(ref minX, ref minY, ref maxX, ref maxY, clip))
        {
            return;
        }
        list.AddImage(new ImageInstance
        {
            MinX = minX,
            MinY = minY,
            MaxX = maxX,
            MaxY = maxY,
            Ux = (float)toUv.M00,
            Uy = (float)toUv.M01,
            U0 = (float)toUv.M02,
            Vx = (float)toUv.M10,
            Vy = (float)toUv.M11,
            V0 = (float)toUv.M12,
            Opacity = opacity,
            ClipIndex = clip,
            Handle = handle,
            EdgeAntialiasing = IsAxisAligned(t) ? 0f : 1f,
        }, image);
    }

    private void Glyphs(in GlyphRunData run, float offsetX, float offsetY, float opacity)
    {
        if (opacity <= 0f || run.Color.A <= 0f)
        {
            return;
        }
        uint clip = CurrentClip();
        int monoStart = list.Glyphs.Count;
        int colorStart = list.ColorGlyphs.Count;
        CulledGlyphs += GlyphRunBuilder.Append(list, run, offsetX, offsetY, opacity, clip, monoAtlas, colorAtlas, placedGlyphs);
        list.PlaceGlyphs(DrawKind.Glyph, monoStart);
        list.PlaceGlyphs(DrawKind.ColorGlyph, colorStart);
    }

    // ── Masks ───────────────────────────────────────────────────────────

    private static float MaxScale(in Affine t)
    {
        double a = Math.Sqrt(t.M00 * t.M00 + t.M10 * t.M10);
        double b = Math.Sqrt(t.M01 * t.M01 + t.M11 * t.M11);
        return (float)Math.Max(a, b);
    }

    private ulong MaskKeyForShape(in Affine t, int kind, float a, float b, float c, float d, float e, float f, StrokeParameters stroke = default)
    {
        hasher.Reset();
        Hash(kind);
        HashLinear(t);
        Hash(a);
        Hash(b);
        Hash(c);
        Hash(d);
        Hash(e);
        Hash(f);
        HashStroke(stroke);
        return hasher.GetCurrentHashAsUInt64();
    }

    private void StrokeMask(FlatPath localContours, in StrokeParameters stroke, in Affine t, ulong key, ShapeInstance inst)
    {
        float local = FlattenTolerance / Math.Max(MaxScale(t), 1e-3f);
        stroker.Stroke(localContours, stroke, local, strokeOutput);
        strokeOutput.Transform(t);
        FillMask(strokeOutput, FillRule.NonZero, t, key, inst);
    }

    /// <summary>
    /// Rasterizes device-space contours into the mask atlas as tiles on a grid anchored at the
    /// contours' own integer-floored bounds, keyed by <paramref name="geometryKey"/> (the geometry
    /// under <paramref name="t"/>'s linear part) and <paramref name="t"/>'s translation relative to
    /// that anchor — so a whole-pixel move hits the same masks — and places a mask shape for each
    /// tile inside the clip rect.
    /// </summary>
    private void FillMask(FlatPath devicePath, FillRule rule, in Affine t, ulong geometryKey, ShapeInstance inst)
    {
        if (!devicePath.TryGetBounds(out float minX, out float minY, out float maxX, out float maxY))
        {
            return;
        }
        int ax = (int)MathF.Floor(minX);
        int ay = (int)MathF.Floor(minY);
        int bx = (int)MathF.Ceiling(maxX);
        int by = (int)MathF.Ceiling(maxY);
        uint clip = CurrentClip();
        if (!ClipExtent(ref minX, ref minY, ref maxX, ref maxY, clip))
        {
            return;
        }
        int x0 = (int)MathF.Floor(minX);
        int y0 = (int)MathF.Floor(minY);
        int x1 = (int)MathF.Ceiling(maxX);
        int y1 = (int)MathF.Ceiling(maxY);
        hasher.Reset();
        Span<long> head = [(long)geometryKey, (long)rule];
        hasher.Append(MemoryMarshal.AsBytes(head));
        HashAnchoredTranslation(t, ax, ay);
        ulong anchoredKey = hasher.GetCurrentHashAsUInt64();

        inst.Type = ShapeType.Mask;
        inst.SetIdentityFrame();
        inst.ClipIndex = clip;
        // Tiles of the anchored grid that intersect the clipped extent.
        int firstRow = ay + (y0 - ay) / MaskTileSize * MaskTileSize;
        int firstColumn = ax + (x0 - ax) / MaskTileSize * MaskTileSize;
        for (int ty = firstRow; ty < y1; ty += MaskTileSize)
        {
            for (int tx = firstColumn; tx < x1; tx += MaskTileSize)
            {
                int tw = Math.Min(MaskTileSize, bx - tx);
                int th = Math.Min(MaskTileSize, by - ty);
                if (tw <= 0 || th <= 0)
                {
                    continue;
                }
                ulong key = TileKey(anchoredKey, rule, tx - ax, ty - ay, tw, th);
                if (!uniformTiles.TryGetValue(key, out byte uniform) && !masks.TryLookup(key, out _))
                {
                    var tile = coverage.AsSpan(0, tw * th);
                    rasterizer.Reset(tw, th);
                    rasterizer.AddPath(devicePath, -tx, -ty);
                    rasterizer.Resolve(tile, rule);
                    if (IsUniform(tile, out uniform))
                    {
                        uniformTiles[key] = uniform;
                    }
                    else if (!masks.TryInsert(key, tile, tw, th, out _))
                    {
                        DroppedMasks++;
                        continue;
                    }
                }
                if (uniformTiles.TryGetValue(key, out uniform))
                {
                    if (uniform == 0)
                    {
                        continue;
                    }
                    if (uniform == 255)
                    {
                        // Fully covered: a hard rect over the tile is the same coverage (1 at every pixel centre).
                        var rect = inst;
                        rect.Type = ShapeType.Rect;
                        rect.P0 = tx;
                        rect.P1 = ty;
                        rect.P2 = tx + tw;
                        rect.P3 = ty + th;
                        if (TileExtent(ref rect, tx, ty, tw, th, clip))
                        {
                            list.AddShape(rect);
                        }
                        continue;
                    }
                    // A uniform partial tile is rare (a path of constant fractional coverage): store it.
                    var tile = coverage.AsSpan(0, tw * th);
                    tile.Fill(uniform);
                    uniformTiles.Remove(key);
                    if (!masks.TryInsert(key, tile, tw, th, out _))
                    {
                        DroppedMasks++;
                        continue;
                    }
                }
                masks.TryLookup(key, out var region);
                inst.P0 = tx;
                inst.P1 = ty;
                inst.P2 = region.U;
                inst.P3 = region.V;
                inst.Q0 = tw;
                inst.Q1 = th;
                inst.Q2 = region.Layer;
                if (TileExtent(ref inst, tx, ty, tw, th, clip))
                {
                    list.AddShape(inst);
                }
            }
        }
    }

    // The tile's raster extent, cut to the clip rect; false when nothing of it is inside.
    private bool TileExtent(ref ShapeInstance inst, int tx, int ty, int tw, int th, uint clip)
    {
        float minX = tx, minY = ty, maxX = tx + tw, maxY = ty + th;
        if (!ClipExtent(ref minX, ref minY, ref maxX, ref maxY, clip))
        {
            return false;
        }
        inst.MinX = minX;
        inst.MinY = minY;
        inst.MaxX = maxX;
        inst.MaxY = maxY;
        return true;
    }
    private ulong TileKey(ulong geometryKey, FillRule rule, int x, int y, int w, int h)
    {
        hasher.Reset();
        Span<long> values = [(long)geometryKey, (long)rule, x, y, w, h];
        hasher.Append(MemoryMarshal.AsBytes(values));
        return hasher.GetCurrentHashAsUInt64();
    }

    // ── Shape outlines (for masks) ──────────────────────────────────────

    private static void RoundedRectFlat(in Affine t, float x, float y, float w, float h, float radius, FlatPath output)
    {
        output.Clear();
        float r = Math.Max(0f, Math.Min(radius, Math.Min(w, h) * 0.5f));
        float tol = FlattenTolerance / Math.Max(MaxScale(t), 1e-3f);
        if (r <= 0f)
        {
            output.MoveTo(x, y);
            output.LineTo(x + w, y);
            output.LineTo(x + w, y + h);
            output.LineTo(x, y + h);
            output.Close();
        }
        else
        {
            output.MoveTo(x + r, y);
            output.LineTo(x + w - r, y);
            AppendArcPoints(output, x + w - r, y + r, r, -MathF.PI / 2f, MathF.PI / 2f, tol);
            output.LineTo(x + w, y + h - r);
            AppendArcPoints(output, x + w - r, y + h - r, r, 0f, MathF.PI / 2f, tol);
            output.LineTo(x + r, y + h);
            AppendArcPoints(output, x + r, y + h - r, r, MathF.PI / 2f, MathF.PI / 2f, tol);
            output.LineTo(x, y + r);
            AppendArcPoints(output, x + r, y + r, r, MathF.PI, MathF.PI / 2f, tol);
            output.Close();
        }
        output.Transform(t);
    }

    private static void EllipseFlat(in Affine t, float cx, float cy, float r, FlatPath output)
    {
        output.Clear();
        float tol = FlattenTolerance / Math.Max(MaxScale(t), 1e-3f);
        output.MoveTo(cx + r, cy);
        AppendArcPoints(output, cx, cy, r, 0f, 2f * MathF.PI, tol);
        output.Close();
        output.Transform(t);
    }

    private static void ArcFlat(float cx, float cy, float r, float start, float sweep, float tol, FlatPath output)
    {
        output.Clear();
        output.MoveTo(cx + r * MathF.Cos(start), cy + r * MathF.Sin(start));
        AppendArcPoints(output, cx, cy, r, start, sweep, tol);
        output.EndContour(closed: false);
    }

    private static void SectorFlat(float cx, float cy, float outer, float inner, float start, float sweep, FlatPath output)
    {
        output.Clear();
        float tol = FlattenTolerance;
        output.MoveTo(cx + outer * MathF.Cos(start), cy + outer * MathF.Sin(start));
        AppendArcPoints(output, cx, cy, outer, start, sweep, tol);
        if (inner > 0f)
        {
            output.LineTo(cx + inner * MathF.Cos(start + sweep), cy + inner * MathF.Sin(start + sweep));
            AppendArcPoints(output, cx, cy, inner, start + sweep, -sweep, tol);
        }
        else
        {
            output.LineTo(cx, cy);
        }
        output.Close();
    }

    private static void AppendArcPoints(FlatPath output, float cx, float cy, float r, float start, float sweep, float tol)
    {
        float step = r > tol ? 2f * MathF.Acos(1f - tol / r) : MathF.PI / 2f;
        int segments = Math.Clamp((int)MathF.Ceiling(MathF.Abs(sweep) / Math.Max(step, 1e-3f)), 1, 2048);
        for (int i = 1; i <= segments; i++)
        {
            float a = start + sweep * i / segments;
            output.LineTo(cx + r * MathF.Cos(a), cy + r * MathF.Sin(a));
        }
    }

    // Appends every contour of source to target with its point order reversed.
    private static void AppendReversed(FlatPath source, FlatPath target)
    {
        var xs = source.X;
        var ys = source.Y;
        foreach (var contour in source.Contours)
        {
            int last = contour.Start + contour.Count - 1;
            target.MoveTo(xs[last], ys[last]);
            for (int i = last - 1; i >= contour.Start; i--)
            {
                target.LineTo(xs[i], ys[i]);
            }
            target.Close();
        }
    }
}
