using System.Runtime.InteropServices;

namespace Etch.Compose;

/// <summary>
/// One frame's draws in paint order, grouped into batches by <see cref="PaintOrderBatcher"/>, with
/// the tables they index (clips, gradients, gradient stops, images). Built by
/// <see cref="DrawListBuilder"/>, then executed unchanged by the GPU or the CPU composer — which is
/// what makes the two backends agree by construction: they run the same items in the same batch
/// order and differ only in per-pixel arithmetic.
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
    private readonly List<ImageInstance> images = new();
    private readonly List<ImageInstance> orderedImages = new();
    private readonly List<DrawItem> imageItems = new();
    private readonly List<BlurInstance> blurs = new();
    private readonly List<BlurInstance> orderedBlurs = new();
    private readonly List<DrawItem> blurItems = new();

    private readonly List<ClipEntry> clips = new();
    private readonly List<GradientEntry> gradients = new();
    private readonly List<GradientStopEntry> stops = new();
    private readonly List<MaskTileEntry> maskTiles = new();
    private readonly Dictionary<int, ComposeImage> imageTable = new();

    private List<ShapeInstance> finishedShapes;
    private List<GlyphInstance> finishedGlyphs;
    private List<GlyphInstance> finishedColorGlyphs;
    private List<ImageInstance> finishedImages;
    private List<BlurInstance> finishedBlurs;

    /// <summary>Creates an empty draw list.</summary>
    public DrawList()
    {
        finishedShapes = shapes;
        finishedGlyphs = glyphs;
        finishedColorGlyphs = colorGlyphs;
        finishedImages = images;
        finishedBlurs = blurs;
        Reset(1, 1);
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

    /// <summary>Shape instances in paint order.</summary>
    public List<ShapeInstance> Shapes => shapes;

    /// <summary>Mono glyph instances in paint order; append a run, then call <see cref="PlaceGlyphs"/>.</summary>
    public List<GlyphInstance> Glyphs => glyphs;

    /// <summary>Colour glyph instances in paint order; append a run, then call <see cref="PlaceGlyphs"/>.</summary>
    public List<GlyphInstance> ColorGlyphs => colorGlyphs;

    /// <summary>Image instances in paint order.</summary>
    public List<ImageInstance> ImageInstances => images;

    /// <summary>Blur instances in paint order.</summary>
    public List<BlurInstance> Blurs => blurs;

    /// <summary>The clip table; entry 0 is the whole target.</summary>
    public ReadOnlySpan<ClipEntry> Clips => CollectionsMarshal.AsSpan(clips);

    /// <summary>The gradient table; entry 0 is unused (solid paint).</summary>
    public ReadOnlySpan<GradientEntry> Gradients => CollectionsMarshal.AsSpan(gradients);

    /// <summary>The gradient stops the gradient table indexes.</summary>
    public ReadOnlySpan<GradientStopEntry> GradientStops => CollectionsMarshal.AsSpan(stops);

    /// <summary>The clip-mask tiles the clip table's masks index.</summary>
    public ReadOnlySpan<MaskTileEntry> MaskTiles => CollectionsMarshal.AsSpan(maskTiles);

    /// <summary>The images the frame's quads reference, by handle.</summary>
    public IReadOnlyDictionary<int, ComposeImage> Images => imageTable;

    // The same table, for allocation-free enumeration inside the assembly (a foreach over the
    // interface boxes the enumerator).
    internal Dictionary<int, ComposeImage> ImageTable => imageTable;

    /// <summary>Shapes in batch order (valid after <see cref="Finish"/>).</summary>
    public List<ShapeInstance> OrderedShapes => finishedShapes;

    /// <summary>Mono glyphs in batch order (valid after <see cref="Finish"/>).</summary>
    public List<GlyphInstance> OrderedGlyphs => finishedGlyphs;

    /// <summary>Colour glyphs in batch order (valid after <see cref="Finish"/>).</summary>
    public List<GlyphInstance> OrderedColorGlyphs => finishedColorGlyphs;

    /// <summary>Images in batch order (valid after <see cref="Finish"/>).</summary>
    public List<ImageInstance> OrderedImages => finishedImages;

    /// <summary>Blurs in batch order (valid after <see cref="Finish"/>).</summary>
    public List<BlurInstance> OrderedBlurs => finishedBlurs;

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
        clips.Clear();
        gradients.Clear();
        stops.Clear();
        maskTiles.Clear();
        finishedShapes = shapes;
        finishedGlyphs = glyphs;
        finishedColorGlyphs = colorGlyphs;
        finishedImages = images;
        finishedBlurs = blurs;

        // Entry 0 of each table: the whole target, and "no gradient".
        clips.Add(new ClipEntry { MinX = 0, MinY = 0, MaxX = width, MaxY = height });
        gradients.Add(default);
    }

    /// <summary>
    /// Empties the list and frees its storage, including its references to the frame's images:
    /// the target is hidden, and the next frame starts from <see cref="Reset"/> as usual.
    /// </summary>
    public void Trim()
    {
        Reset(0, 0);
        batcher.Trim();
        orderedShapes.Clear();
        orderedGlyphs.Clear();
        orderedColorGlyphs.Clear();
        orderedImages.Clear();
        orderedBlurs.Clear();
        shapes.TrimExcess();
        orderedShapes.TrimExcess();
        shapeItems.TrimExcess();
        glyphs.TrimExcess();
        orderedGlyphs.TrimExcess();
        glyphItems.TrimExcess();
        colorGlyphs.TrimExcess();
        orderedColorGlyphs.TrimExcess();
        colorGlyphItems.TrimExcess();
        images.TrimExcess();
        orderedImages.TrimExcess();
        imageItems.TrimExcess();
        blurs.TrimExcess();
        orderedBlurs.TrimExcess();
        blurItems.TrimExcess();
        clips.TrimExcess();
        gradients.TrimExcess();
        stops.TrimExcess();
        maskTiles.TrimExcess();
        imageTable.TrimExcess();
    }
    /// <summary>Appends a clip entry and returns its index.</summary>
    public uint AddClip(in ClipEntry clip)
    {
        clips.Add(clip);
        return (uint)(clips.Count - 1);
    }

    /// <summary>Appends a clip mask's tiles (row-major) and returns the index of the first.</summary>
    public int AddMaskTiles(ReadOnlySpan<MaskTileEntry> tiles)
    {
        int start = maskTiles.Count;
        maskTiles.AddRange(tiles);
        return start;
    }

    /// <summary>Appends a gradient with its stops and returns its index.</summary>
    public uint AddGradient(GradientEntry gradient, ReadOnlySpan<GradientStopEntry> gradientStops)
    {
        gradient.StopStart = (uint)stops.Count;
        gradient.StopCount = (uint)gradientStops.Length;
        stops.AddRange(gradientStops);
        gradients.Add(gradient);
        return (uint)(gradients.Count - 1);
    }

    /// <summary>
    /// Takes a shape's place in paint order without drawing it: a fully transparent solid shape
    /// batches as it would when visible, so whether it shows changes only its own pixels, not the
    /// batches of everything drawn after it.
    /// </summary>
    public void HoldShapePlace(float minX, float minY, float maxX, float maxY)
    {
        batcher.Place(DrawKind.Shape, minX, minY, maxX, maxY);
    }

    /// <summary>Places one shape in paint order.</summary>
    public void AddShape(in ShapeInstance inst)
    {
        int batch = batcher.Place(DrawKind.Shape, inst.MinX, inst.MinY, inst.MaxX, inst.MaxY);
        batcher.Record(DrawKind.Shape, shapeItems, batch, shapes.Count, 1);
        shapes.Add(inst);
    }

    /// <summary>
    /// Places the glyph run appended to <see cref="Glyphs"/> (for <see cref="DrawKind.Glyph"/>) or
    /// <see cref="ColorGlyphs"/> (for <see cref="DrawKind.ColorGlyph"/>) since index
    /// <paramref name="start"/>, as one item with the union of its quads.
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

        var clipTable = CollectionsMarshal.AsSpan(clips);
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (ref readonly var g in CollectionsMarshal.AsSpan(list).Slice(start, count))
        {
            ref readonly var clip = ref clipTable[(int)g.ClipIndex];
            minX = Math.Min(minX, Math.Max(g.PosX, clip.MinX));
            minY = Math.Min(minY, Math.Max(g.PosY, clip.MinY));
            maxX = Math.Max(maxX, Math.Min(g.PosX + g.SizeX, clip.MaxX));
            maxY = Math.Max(maxY, Math.Min(g.PosY + g.SizeY, clip.MaxY));
        }

        int batch = batcher.Place(kind, minX, minY, maxX, maxY);
        batcher.Record(kind, items, batch, start, count);
    }

    /// <summary>Places one image in paint order; <paramref name="image"/> is the texture it samples.</summary>
    public void AddImage(in ImageInstance instance, ComposeImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        imageTable[instance.Handle] = image;
        int batch = batcher.Place(DrawKind.Image, instance.MinX, instance.MinY, instance.MaxX, instance.MaxY);
        batcher.Record(DrawKind.Image, imageItems, batch, images.Count, 1);
        images.Add(instance);
    }

    /// <summary>
    /// Places one backdrop blur. Its footprint includes the blur's sampling reach, so everything it
    /// samples is drawn before it.
    /// </summary>
    public void AddBlur(in BlurInstance blur)
    {
        float reach = BlurReach(blur.Sigma);
        int batch = batcher.Place(DrawKind.Blur, blur.MinX - reach, blur.MinY - reach, blur.MaxX + reach, blur.MaxY + reach);
        batcher.Record(DrawKind.Blur, blurItems, batch, blurs.Count, 1);
        blurs.Add(blur);
    }

    /// <summary>
    /// How far outside its rect a blur reads: ±3 taps of σ/2 (σ clamped to ≥ 0.5) plus one texel
    /// of bilinear filtering.
    /// </summary>
    public static float BlurReach(float sigma) => 1.5f * Math.Max(sigma, 0.5f) + 2f;

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
