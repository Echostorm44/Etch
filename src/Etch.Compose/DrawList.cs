using System.Runtime.InteropServices;

namespace Etch.Compose;

/// <summary>
/// One frame's draws in paint order, grouped into batches by <see cref="PaintOrderBatcher"/>. Built by
/// the host (one producer for every backend), then executed unchanged by the GPU or CPU composer —
/// which is what makes the two backends agree by construction: they run the same items in the same
/// batch order and differ only in per-pixel arithmetic.
/// </summary>
/// <remarks>
/// Every list is reused across frames; a steady-state frame allocates nothing once the lists have
/// grown to the frame's size.
/// </remarks>
public sealed class DrawList
{
    private readonly PaintOrderBatcher batcher = new();

    private readonly List<ShapeInstance> shapes = new();
    private readonly List<ShapeInstance> orderedShapes = new();
    private readonly List<DrawItem> shapeItems = new();
    private readonly List<GlyphInstance> glyphs = new();
    private readonly List<GlyphInstance> orderedGlyphs = new();
    private readonly List<DrawItem> glyphItems = new();
    private readonly List<GlyphInstance> colorGlyphs = new();
    private readonly List<GlyphInstance> orderedColorGlyphs = new();
    private readonly List<DrawItem> colorGlyphItems = new();
    private readonly List<ImageQuad> images = new();
    private readonly List<ImageQuad> orderedImages = new();
    private readonly List<DrawItem> imageItems = new();
    private readonly List<BlurInstance> blurs = new();
    private readonly List<BlurInstance> orderedBlurs = new();
    private readonly List<DrawItem> blurItems = new();
    private readonly Dictionary<int, ComposeImage> imageTable = new();

    private List<ShapeInstance> finishedShapes;
    private List<GlyphInstance> finishedGlyphs;
    private List<GlyphInstance> finishedColorGlyphs;
    private List<ImageQuad> finishedImages;
    private List<BlurInstance> finishedBlurs;

    /// <summary>Creates an empty draw list.</summary>
    public DrawList()
    {
        finishedShapes = shapes;
        finishedGlyphs = glyphs;
        finishedColorGlyphs = colorGlyphs;
        finishedImages = images;
        finishedBlurs = blurs;
    }

    /// <summary>Target width in device pixels.</summary>
    public uint Width { get; private set; }

    /// <summary>Target height in device pixels.</summary>
    public uint Height { get; private set; }

    /// <summary>Frame-wide shading parameters.</summary>
    public ComposeParameters Parameters;

    /// <summary>The batches in execution order (valid after <see cref="Finish"/>).</summary>
    public ReadOnlySpan<DrawBatch> Batches => batcher.Batches;

    /// <summary>Number of batches.</summary>
    public int BatchCount => batcher.Count;

    /// <summary>Shape instances in paint order (append glyphs, then call <see cref="PlaceGlyphs"/>).</summary>
    public List<ShapeInstance> Shapes => shapes;

    /// <summary>Mono glyph instances in paint order; append a run, then call <see cref="PlaceGlyphs"/>.</summary>
    public List<GlyphInstance> Glyphs => glyphs;

    /// <summary>Colour glyph instances in paint order; append a run, then call <see cref="PlaceGlyphs"/>.</summary>
    public List<GlyphInstance> ColorGlyphs => colorGlyphs;

    /// <summary>Image quads in paint order.</summary>
    public List<ImageQuad> ImageQuads => images;

    /// <summary>Blur instances in paint order.</summary>
    public List<BlurInstance> Blurs => blurs;

    /// <summary>The images the frame's quads reference, by handle.</summary>
    public IReadOnlyDictionary<int, ComposeImage> Images => imageTable;

    /// <summary>Shapes in batch order (valid after <see cref="Finish"/>).</summary>
    public List<ShapeInstance> OrderedShapes => finishedShapes;

    /// <summary>Mono glyphs in batch order (valid after <see cref="Finish"/>).</summary>
    public List<GlyphInstance> OrderedGlyphs => finishedGlyphs;

    /// <summary>Colour glyphs in batch order (valid after <see cref="Finish"/>).</summary>
    public List<GlyphInstance> OrderedColorGlyphs => finishedColorGlyphs;

    /// <summary>Image quads in batch order (valid after <see cref="Finish"/>).</summary>
    public List<ImageQuad> OrderedImages => finishedImages;

    /// <summary>Blurs in batch order (valid after <see cref="Finish"/>).</summary>
    public List<BlurInstance> OrderedBlurs => finishedBlurs;

    /// <summary>True when nothing but shapes has been placed so far.</summary>
    public bool OnlyShapes => batcher.OnlyShapes;

    /// <summary>Starts a new frame for a target of the given size.</summary>
    public void Reset(uint width, uint height)
    {
        Width = width;
        Height = height;
        batcher.Reset();
        shapes.Clear();
        shapeItems.Clear();
        glyphs.Clear();
        glyphItems.Clear();
        colorGlyphs.Clear();
        colorGlyphItems.Clear();
        images.Clear();
        imageItems.Clear();
        blurs.Clear();
        blurItems.Clear();
        imageTable.Clear();
        finishedShapes = shapes;
        finishedGlyphs = glyphs;
        finishedColorGlyphs = colorGlyphs;
        finishedImages = images;
        finishedBlurs = blurs;
    }

    /// <summary>
    /// Places shape instances in paint order. While nothing but shapes has been placed (the
    /// backgrounds at the start of any frame), they all join one batch without per-instance placement.
    /// </summary>
    public void AddShapes(ReadOnlySpan<ShapeInstance> span)
    {
        if (span.IsEmpty)
        {
            return;
        }

        if (batcher.OnlyShapes)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (ref readonly var inst in span)
            {
                minX = Math.Min(minX, inst.MinX - inst.Expand);
                minY = Math.Min(minY, inst.MinY - inst.Expand);
                maxX = Math.Max(maxX, inst.MaxX + inst.Expand);
                maxY = Math.Max(maxY, inst.MaxY + inst.Expand);
            }
            int batch = batcher.Place(DrawKind.Shape, minX, minY, maxX, maxY);
            batcher.Record(DrawKind.Shape, shapeItems, batch, shapes.Count, span.Length);
            shapes.AddRange(span);
            return;
        }

        foreach (ref readonly var inst in span)
        {
            AddShape(inst);
        }
    }

    /// <summary>Places one shape instance in paint order.</summary>
    public void AddShape(in ShapeInstance inst)
    {
        int batch = batcher.Place(DrawKind.Shape,
            inst.MinX - inst.Expand, inst.MinY - inst.Expand, inst.MaxX + inst.Expand, inst.MaxY + inst.Expand);
        batcher.Record(DrawKind.Shape, shapeItems, batch, shapes.Count, 1);
        shapes.Add(inst);
    }

    /// <summary>
    /// Places the glyph run appended to <see cref="Glyphs"/> (for <see cref="DrawKind.Glyph"/>) or
    /// <see cref="ColorGlyphs"/> (for <see cref="DrawKind.ColorGlyph"/>) since index
    /// <paramref name="start"/>, as one item with the union of its clipped quads.
    /// </summary>
    public void PlaceGlyphs(DrawKind kind, int start)
    {
        if (kind != DrawKind.Glyph && kind != DrawKind.ColorGlyph)
        {
            Panic.Invariant(PanicCodes.ArgumentOutOfRange, "PlaceGlyphs takes Glyph or ColorGlyph");
        }

        var list = kind == DrawKind.Glyph ? glyphs : colorGlyphs;
        var items = kind == DrawKind.Glyph ? glyphItems : colorGlyphItems;
        int count = list.Count - start;
        if (count <= 0)
        {
            return;
        }

        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (ref readonly var g in CollectionsMarshal.AsSpan(list).Slice(start, count))
        {
            float l = g.PosX, t = g.PosY, r = g.PosX + g.SizeX, b = g.PosY + g.SizeY;
            if (g.ClipMaxX > g.ClipMinX || g.ClipMaxY > g.ClipMinY)
            {
                l = Math.Max(l, g.ClipMinX);
                t = Math.Max(t, g.ClipMinY);
                r = Math.Min(r, g.ClipMaxX);
                b = Math.Min(b, g.ClipMaxY);
            }
            minX = Math.Min(minX, l);
            minY = Math.Min(minY, t);
            maxX = Math.Max(maxX, r);
            maxY = Math.Max(maxY, b);
        }

        int batch = batcher.Place(kind, minX, minY, maxX, maxY);
        batcher.Record(kind, items, batch, start, count);
    }

    /// <summary>
    /// Places one image quad, clamping it and its UVs to the clip rect when <paramref name="clipped"/>.
    /// A degenerate clip intersection draws nothing.
    /// </summary>
    public void AddImage(int handle, ComposeImage image, float left, float top, float right, float bottom,
        bool clipped, float clipLeft, float clipTop, float clipRight, float clipBottom)
    {
        ArgumentNullException.ThrowIfNull(image);
        float u0 = 0f, v0 = 0f, u1 = 1f, v1 = 1f;
        if (clipped)
        {
            if (clipRight <= clipLeft || clipBottom <= clipTop)
            {
                return;
            }

            float nl = Math.Max(left, clipLeft), nt = Math.Max(top, clipTop);
            float nr = Math.Min(right, clipRight), nb = Math.Min(bottom, clipBottom);
            if (nr <= nl || nb <= nt)
            {
                return;
            }

            float w = right - left, h = bottom - top;
            u0 = (nl - left) / w; u1 = (nr - left) / w;
            v0 = (nt - top) / h; v1 = (nb - top) / h;
            left = nl; top = nt; right = nr; bottom = nb;
        }

        imageTable[handle] = image;
        int batch = batcher.Place(DrawKind.Image, left, top, right, bottom);
        batcher.Record(DrawKind.Image, imageItems, batch, images.Count, 1);
        images.Add(new ImageQuad
        {
            Handle = handle,
            L = left,
            T = top,
            R = right,
            B = bottom,
            U0 = u0,
            V0 = v0,
            U1 = u1,
            V1 = v1,
        });
    }

    /// <summary>
    /// Places one backdrop blur. Its footprint includes the blur's sampling reach, so everything it
    /// samples is drawn before it.
    /// </summary>
    public void AddBlur(in BlurInstance blur)
    {
        // The shader taps ±3 steps of σ/2 around each pixel (σ clamped to ≥ 0.5), plus one texel
        // of bilinear filtering.
        float reach = 1.5f * Math.Max(blur.Sigma, 0.5f) + 2f;
        int batch = batcher.Place(DrawKind.Blur, blur.MinX - reach, blur.MinY - reach, blur.MaxX + reach, blur.MaxY + reach);
        batcher.Record(DrawKind.Blur, blurItems, batch, blurs.Count, 1);
        blurs.Add(blur);
    }

    /// <summary>Assigns each batch its range and orders every kind by batch. Call once, after the last add.</summary>
    public void Finish()
    {
        batcher.AssignStarts();
        finishedShapes = batcher.Order(DrawKind.Shape, shapes, shapeItems, orderedShapes);
        finishedGlyphs = batcher.Order(DrawKind.Glyph, glyphs, glyphItems, orderedGlyphs);
        finishedColorGlyphs = batcher.Order(DrawKind.ColorGlyph, colorGlyphs, colorGlyphItems, orderedColorGlyphs);
        finishedImages = batcher.Order(DrawKind.Image, images, imageItems, orderedImages);
        finishedBlurs = batcher.Order(DrawKind.Blur, blurs, blurItems, orderedBlurs);
    }
}
