using System.Runtime.InteropServices;
using Etch.Compose.Coverage;
using Etch.Geometry;
using Etch.Scene;
using Etch.Text.Shape;

namespace Etch.Compose;

/// <summary>A glyph run as recorded: device-space pen positions in a face rasterized at a pixel size.</summary>
/// <param name="Face">The face, already sized for <paramref name="RasterSize"/>.</param>
/// <param name="FaceId">Stable id of the font (keys the glyph atlas).</param>
/// <param name="GlyphIds">Glyph ids.</param>
/// <param name="Positions">Pen positions, device pixels, interleaved x, y.</param>
/// <param name="RasterSize">Pixel size the glyphs are rasterized at.</param>
/// <param name="Color">Text colour, linear-light, straight alpha.</param>
/// <param name="Source">Caller data echoed into <see cref="PlacedGlyph"/> records (draw provenance), or null.</param>
public readonly record struct GlyphRunData(FontFace Face, int FaceId, ushort[] GlyphIds, float[] Positions, float RasterSize, ComposeColor Color, object? Source);

/// <summary>
/// A retained list of drawing commands in paint order — geometry in local space under recorded
/// transforms, clips, glyph runs, images, blurs and nested recordings — that
/// <see cref="DrawListBuilder"/> replays into a frame's <see cref="DrawList"/>, optionally offset
/// and faded. A retained layer (scrolled content) is recorded once and replayed every frame at its
/// scroll offset. Paths are copied in, so a recording owns everything it references except images,
/// faces and nested recordings. Storage is reused across <see cref="Clear"/>.
/// </summary>
public sealed class DrawRecording
{
    internal enum Op : byte
    {
        Transform,
        ClipRect,
        ClipRoundedRect,
        ClipPath,
        PopClip,
        FillRect,
        FillRoundedRect,
        Border,
        FillCircle,
        StrokeCircle,
        FillSector,
        StrokeLine,
        StrokeArc,
        FillPath,
        StrokePath,
        Shadow,
        BackdropBlur,
        Image,
        Glyphs,
        Layer,
    }

    [StructLayout(LayoutKind.Auto)]
    internal struct Command
    {
        public Op Op;
        public byte Flag;
        public int Ref;
        public int Ref2;
        public int Ref3;
        public float A, B, C, D, E, F, G;
    }

    internal readonly record struct PathRef(int VerbStart, int VerbCount, int CoordStart, int CoordCount);

    internal readonly record struct LayerRef(DrawRecording Recording, float OffsetX, float OffsetY, float Opacity);

    private readonly List<Command> commands = new();
    private readonly List<Affine> transforms = new();
    private readonly List<byte> verbs = new();
    private readonly List<double> coords = new();
    private readonly List<PathRef> paths = new();
    private readonly List<ComposePaint> paints = new();
    private readonly List<StrokeParameters> strokes = new();
    private readonly List<(int Handle, ComposeImage Image)> images = new();
    private readonly List<GlyphRunData> glyphRuns = new();
    private readonly List<LayerRef> layers = new();

    internal ReadOnlySpan<Command> Commands => CollectionsMarshal.AsSpan(commands);

    internal ReadOnlySpan<Affine> Transforms => CollectionsMarshal.AsSpan(transforms);

    internal ReadOnlySpan<byte> Verbs => CollectionsMarshal.AsSpan(verbs);

    internal ReadOnlySpan<double> Coords => CollectionsMarshal.AsSpan(coords);

    internal ReadOnlySpan<PathRef> Paths => CollectionsMarshal.AsSpan(paths);

    internal List<ComposePaint> Paints => paints;

    internal List<StrokeParameters> Strokes => strokes;

    internal List<(int Handle, ComposeImage Image)> Images => images;

    internal List<GlyphRunData> GlyphRuns => glyphRuns;

    internal List<LayerRef> Layers => layers;

    /// <summary>Number of recorded commands.</summary>
    public int Count => commands.Count;

    /// <summary>Empties the recording (storage is kept).</summary>
    public void Clear()
    {
        commands.Clear();
        transforms.Clear();
        verbs.Clear();
        coords.Clear();
        paths.Clear();
        paints.Clear();
        strokes.Clear();
        images.Clear();
        glyphRuns.Clear();
        layers.Clear();
    }

    /// <summary>Sets the local → device transform for the geometry of subsequent commands.</summary>
    public void SetTransform(in Affine transform)
    {
        transforms.Add(transform);
        commands.Add(new Command { Op = Op.Transform, Ref = transforms.Count - 1 });
    }

    /// <summary>Intersects the clip with a local rect.</summary>
    public void PushClipRect(float x, float y, float width, float height)
        => commands.Add(new Command { Op = Op.ClipRect, A = x, B = y, C = width, D = height });

    /// <summary>Intersects the clip with a local rounded rect.</summary>
    public void PushClipRoundedRect(float x, float y, float width, float height, float radius)
        => commands.Add(new Command { Op = Op.ClipRoundedRect, A = x, B = y, C = width, D = height, E = radius });

    /// <summary>Intersects the clip with a local path.</summary>
    public void PushClipPath(BezPath path, FillRule rule)
        => commands.Add(new Command { Op = Op.ClipPath, Ref = AddPath(path), Flag = (byte)rule });

    /// <summary>Restores the clip in force before the matching push.</summary>
    public void PopClip() => commands.Add(new Command { Op = Op.PopClip });

    /// <summary>Fills a rect. Axis-aligned rects keep hard, pixel-centre edges (UI pixel snapping).</summary>
    public void FillRect(float x, float y, float width, float height, in ComposePaint paint)
        => commands.Add(new Command { Op = Op.FillRect, A = x, B = y, C = width, D = height, Ref = AddPaint(paint) });

    /// <summary>Fills a rounded rect with antialiased edges.</summary>
    public void FillRoundedRect(float x, float y, float width, float height, float radius, in ComposePaint paint)
        => commands.Add(new Command { Op = Op.FillRoundedRect, A = x, B = y, C = width, D = height, E = radius, Ref = AddPaint(paint) });

    /// <summary>
    /// Draws a border of <paramref name="borderWidth"/> inside the (rounded) rect's edge, as CSS does:
    /// the outer edge is the rect, the inner edge is the rect inset by the width.
    /// </summary>
    public void Border(float x, float y, float width, float height, float radius, float borderWidth, ComposeColor color)
        => commands.Add(new Command { Op = Op.Border, A = x, B = y, C = width, D = height, E = radius, F = borderWidth, Ref = AddPaint(ComposePaint.Solid(color)) });

    /// <summary>Fills a circle.</summary>
    public void FillCircle(float cx, float cy, float radius, in ComposePaint paint)
        => commands.Add(new Command { Op = Op.FillCircle, A = cx, B = cy, C = radius, Ref = AddPaint(paint) });

    /// <summary>Strokes a circle, centred on its outline.</summary>
    public void StrokeCircle(float cx, float cy, float radius, float strokeWidth, ComposeColor color)
        => commands.Add(new Command { Op = Op.StrokeCircle, A = cx, B = cy, C = radius, D = strokeWidth, Ref = AddPaint(ComposePaint.Solid(color)) });

    /// <summary>Fills an annular sector (radians, positive sweep runs clockwise on screen).</summary>
    public void FillSector(float cx, float cy, float outerRadius, float innerRadius, float startAngle, float sweep, ComposeColor color)
        => commands.Add(new Command { Op = Op.FillSector, A = cx, B = cy, C = outerRadius, D = innerRadius, E = startAngle, F = sweep, Ref = AddPaint(ComposePaint.Solid(color)) });

    /// <summary>Strokes a segment.</summary>
    public void StrokeLine(float x0, float y0, float x1, float y1, in StrokeParameters stroke, ComposeColor color)
        => commands.Add(new Command { Op = Op.StrokeLine, A = x0, B = y0, C = x1, D = y1, Ref = AddPaint(ComposePaint.Solid(color)), Ref2 = AddStroke(stroke) });

    /// <summary>Strokes a circular arc (radians).</summary>
    public void StrokeArc(float cx, float cy, float radius, float startAngle, float sweep, in StrokeParameters stroke, ComposeColor color)
        => commands.Add(new Command { Op = Op.StrokeArc, A = cx, B = cy, C = radius, D = startAngle, E = sweep, Ref = AddPaint(ComposePaint.Solid(color)), Ref2 = AddStroke(stroke) });

    /// <summary>Fills a path.</summary>
    public void FillPath(BezPath path, FillRule rule, in ComposePaint paint)
        => commands.Add(new Command { Op = Op.FillPath, Ref = AddPath(path), Ref2 = AddPaint(paint), Flag = (byte)rule });

    /// <summary>Strokes a path, centred on its outline.</summary>
    public void StrokePath(BezPath path, in StrokeParameters stroke, in ComposePaint paint)
        => commands.Add(new Command { Op = Op.StrokePath, Ref = AddPath(path), Ref2 = AddPaint(paint), Ref3 = AddStroke(stroke) });

    /// <summary>Draws a Gaussian-blurred rounded rect (drop shadow); <paramref name="sigma"/> in local units.</summary>
    public void Shadow(float x, float y, float width, float height, float radius, float sigma, ComposeColor color)
        => commands.Add(new Command { Op = Op.Shadow, A = x, B = y, C = width, D = height, E = radius, F = sigma, Ref = AddPaint(ComposePaint.Solid(color)) });

    /// <summary>Fills a device-space rounded rect with a tinted blur of what is behind it.</summary>
    public void BackdropBlur(float deviceX, float deviceY, float width, float height, float radius, float sigma, ComposeColor tint)
        => commands.Add(new Command { Op = Op.BackdropBlur, A = deviceX, B = deviceY, C = width, D = height, E = radius, F = sigma, Ref = AddPaint(ComposePaint.Solid(tint)) });

    /// <summary>Draws an image into a local rect.</summary>
    public void Image(int handle, ComposeImage image, float x, float y, float width, float height, float opacity)
    {
        images.Add((handle, image));
        commands.Add(new Command { Op = Op.Image, A = x, B = y, C = width, D = height, E = opacity, Ref = images.Count - 1 });
    }

    /// <summary>Draws a glyph run (device-space positions; unaffected by the recorded transform).</summary>
    public void Glyphs(in GlyphRunData run)
    {
        glyphRuns.Add(run);
        commands.Add(new Command { Op = Op.Glyphs, Ref = glyphRuns.Count - 1 });
    }

    /// <summary>Replays <paramref name="layer"/> here, shifted by a device offset and faded by <paramref name="opacity"/>.</summary>
    public void Layer(DrawRecording layer, float offsetX, float offsetY, float opacity)
    {
        ArgumentNullException.ThrowIfNull(layer);
        layers.Add(new LayerRef(layer, offsetX, offsetY, opacity));
        commands.Add(new Command { Op = Op.Layer, Ref = layers.Count - 1 });
    }

    internal PathRef GetPath(int index) => paths[index];

    private int AddPaint(in ComposePaint paint)
    {
        paints.Add(paint);
        return paints.Count - 1;
    }

    private int AddStroke(in StrokeParameters stroke)
    {
        strokes.Add(stroke);
        return strokes.Count - 1;
    }

    private int AddPath(BezPath path)
    {
        int verbStart = verbs.Count;
        int coordStart = coords.Count;
        var it = path.Iterate();
        while (it.MoveNext())
        {
            var seg = it.Current;
            verbs.Add((byte)seg.Verb);
            switch (seg.Verb)
            {
                case PathVerb.MoveTo:
                case PathVerb.LineTo:
                    coords.Add(seg.End.X);
                    coords.Add(seg.End.Y);
                    break;
                case PathVerb.QuadTo:
                    coords.Add(seg.Control0.X);
                    coords.Add(seg.Control0.Y);
                    coords.Add(seg.End.X);
                    coords.Add(seg.End.Y);
                    break;
                case PathVerb.CubicTo:
                    coords.Add(seg.Control0.X);
                    coords.Add(seg.Control0.Y);
                    coords.Add(seg.Control1.X);
                    coords.Add(seg.Control1.Y);
                    coords.Add(seg.End.X);
                    coords.Add(seg.End.Y);
                    break;
            }
        }
        paths.Add(new PathRef(verbStart, verbs.Count - verbStart, coordStart, coords.Count - coordStart));
        return paths.Count - 1;
    }
}
