using System.Runtime.InteropServices;

namespace Etch.Compose;

/// <summary>The kinds of draw a <see cref="DrawList"/> holds; each executes with its own GPU pipeline.</summary>
public enum DrawKind : byte
{
    /// <summary>An analytic or mask-coverage shape (<see cref="ShapeInstance"/>).</summary>
    Shape,

    /// <summary>A monochrome glyph quad sampling the coverage atlas (<see cref="GlyphInstance"/>).</summary>
    Glyph,

    /// <summary>A colour glyph quad sampling the RGBA atlas (<see cref="GlyphInstance"/>).</summary>
    ColorGlyph,

    /// <summary>A textured image quad (<see cref="ImageInstance"/>).</summary>
    Image,

    /// <summary>A frosted-glass backdrop blur (<see cref="BlurInstance"/>).</summary>
    Blur,
}

/// <summary>The coverage function a <see cref="ShapeInstance"/> evaluates.</summary>
public enum ShapeType : uint
{
    /// <summary>Axis-aligned rect, pixel-centre rule (hard edges); P = rect.</summary>
    Rect = 0,

    /// <summary>Filled circle; P = (cx, cy, r, 0).</summary>
    Circle = 1,

    /// <summary>Circle outline; P = (cx, cy, outer radius, width).</summary>
    Ring = 3,

    /// <summary>Stroked segment; P = (x0, y0, x1, y1), Q = (half width, cap, 0, 0).</summary>
    Line = 4,

    /// <summary>Filled rounded rect; P = rect, Q = (radius, 0, 0, 0).</summary>
    RoundedRect = 5,

    /// <summary>Inside border of a rounded rect; P = outer rect, Q = (outer radius, width, inner radius, 0).</summary>
    Border = 6,

    /// <summary>Annular sector; P = (cx, cy, outer, inner), Q = (start, sweep, round caps 0/1, 0).</summary>
    Sector = 8,

    /// <summary>Gaussian-blurred rounded rect; P = rect, Q = (corner, sigma, 0, 0).</summary>
    Shadow = 9,

    /// <summary>Coverage read from the mask atlas; P = (device origin x, y, atlas u, v), Q = (width, height, 0, 0).</summary>
    Mask = 10,
}

/// <summary>
/// One shape draw. The quad <c>[Min, Max]</c> is the device-pixel extent to rasterize (already
/// grown for antialiasing and intersected with the clip rect); every pixel whose centre lies in it
/// evaluates <see cref="Type"/>'s coverage at the pixel centre mapped into the shape's local frame,
/// multiplies the clip coverage and blends the paint. Layout is the WGSL <c>ShapeInstance</c>
/// (std430), 112 bytes.
/// </summary>
/// <remarks>
/// The local frame is <c>local = (A·x + B·y + Tx, C·x + D·y + Ty)</c>; distances measured there are
/// converted to device pixels by multiplying with <see cref="Scale"/> (frames are similarity
/// transforms — anything else is drawn as a <see cref="ShapeType.Mask"/>). Colours are linear-light
/// with straight alpha; for a gradient paint only <see cref="A0"/> is used, as an alpha multiplier.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Size = 112)]
public struct ShapeInstance
{
    /// <summary>Raster extent minimum, device pixels.</summary>
    public float MinX, MinY;

    /// <summary>Raster extent maximum, device pixels.</summary>
    public float MaxX, MaxY;

    /// <summary>Paint colour (linear, straight alpha).</summary>
    public float R0, G0, B0, A0;

    /// <summary>Device → local linear part.</summary>
    public float FrameA, FrameB, FrameC, FrameD;

    /// <summary>Device → local translation.</summary>
    public float FrameTx, FrameTy;

    /// <summary>Local → device distance factor.</summary>
    public float Scale;

    private float pad0;

    /// <summary>First type-specific parameter vector.</summary>
    public float P0, P1, P2, P3;

    /// <summary>Second type-specific parameter vector.</summary>
    public float Q0, Q1, Q2, Q3;

    /// <summary>Which coverage function to evaluate.</summary>
    public ShapeType Type;

    /// <summary>Index into the frame's clip table (0 = the whole target).</summary>
    public uint ClipIndex;

    /// <summary>Index into the frame's gradient table (0 = solid colour).</summary>
    public uint PaintIndex;

    private uint pad1;

    /// <summary>Sets the local frame to the identity (geometry already in device pixels).</summary>
    public void SetIdentityFrame()
    {
        FrameA = 1f;
        FrameB = 0f;
        FrameC = 0f;
        FrameD = 1f;
        FrameTx = 0f;
        FrameTy = 0f;
        Scale = 1f;
    }
}

/// <summary>
/// One glyph quad: integer-aligned device position and size, atlas UVs (V0 at the quad's top edge,
/// V1 at its bottom, because atlas bitmaps are stored bottom-up), colour, the foreground luminance
/// used by the adaptive text weight, and a clip index. Layout is the WGSL <c>GlyphInstance</c>
/// (std430), 64 bytes.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 64)]
public struct GlyphInstance
{
    /// <summary>Quad origin, device pixels.</summary>
    public float PosX, PosY;

    /// <summary>Quad size, device pixels.</summary>
    public float SizeX, SizeY;

    /// <summary>Atlas UV at the quad's top-left.</summary>
    public float AtlasU0, AtlasV0;

    /// <summary>Atlas UV at the quad's bottom-right.</summary>
    public float AtlasU1, AtlasV1;

    /// <summary>
    /// Text colour, linear-light with straight alpha. Colour glyphs carry (1, 1, 1, opacity): their
    /// atlas texels are the colour.
    /// </summary>
    public float R, G, B, A;

    /// <summary>Luminance of the sRGB-encoded text colour (Rec. 709 weights), for the adaptive weight.</summary>
    public float ForegroundLuminance;

    /// <summary>Index into the frame's clip table.</summary>
    public uint ClipIndex;

    private float pad0, pad1;
}

/// <summary>
/// One image draw: the device-pixel extent to rasterize and the affine map from device pixel
/// centres to normalized texture coordinates. Pixels whose coordinate falls outside [0, 1]² are
/// outside the image; edges are antialiased by their device-space distance. Layout is the WGSL
/// <c>ImageInstance</c> (std430), 64 bytes.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 64)]
public struct ImageInstance
{
    /// <summary>Raster extent minimum, device pixels.</summary>
    public float MinX, MinY;

    /// <summary>Raster extent maximum, device pixels.</summary>
    public float MaxX, MaxY;

    /// <summary><c>u = Ux·x + Uy·y + U0</c>.</summary>
    public float Ux, Uy, U0;

    private float pad0;

    /// <summary><c>v = Vx·x + Vy·y + V0</c>.</summary>
    public float Vx, Vy, V0;

    private float pad1;

    /// <summary>Opacity multiplier.</summary>
    public float Opacity;

    /// <summary>Index into the frame's clip table.</summary>
    public uint ClipIndex;

    /// <summary>Image handle; keys <see cref="DrawList.Images"/> and the GPU texture cache.</summary>
    public int Handle;

    /// <summary>
    /// 1 to antialias the image's edges (rotated or skewed images); 0 for the pixel-centre rule,
    /// which keeps axis-aligned UI images (icons) crisp, as axis-aligned rects are.
    /// </summary>
    public float EdgeAntialiasing;
}

/// <summary>
/// One frosted-glass backdrop blur: a device-space rounded rect filled with a Gaussian blur of what is
/// already drawn behind it, then tinted. Layout is the WGSL <c>BlurInstance</c> (std430), 48 bytes.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 48)]
public struct BlurInstance
{
    /// <summary>Panel rect minimum, device pixels.</summary>
    public float MinX, MinY;

    /// <summary>Panel rect maximum, device pixels.</summary>
    public float MaxX, MaxY;

    /// <summary>Tint colour, linear-light, straight alpha.</summary>
    public float TintR, TintG, TintB, TintA;

    /// <summary>Corner radius, device pixels.</summary>
    public float Radius;

    /// <summary>Gaussian standard deviation, device pixels.</summary>
    public float Sigma;

    /// <summary>Index into the frame's clip table.</summary>
    public uint ClipIndex;

    /// <summary>Opacity multiplier for the whole panel.</summary>
    public float Opacity;
}

/// <summary>
/// A clip as every draw kind evaluates it: a hard device rect (pixel-centre rule), optionally an
/// antialiased rounded rect and optionally a coverage mask from the mask atlas. Coverage is the
/// product. Layout is the WGSL <c>ClipEntry</c> (std430), 80 bytes.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 80)]
public struct ClipEntry
{
    /// <summary>Hard clip rect, device pixels (pixel centres in [Min, Max) pass).</summary>
    public float MinX, MinY, MaxX, MaxY;

    /// <summary>Rounded-rect clip bounds, device pixels.</summary>
    public float RoundMinX, RoundMinY, RoundMaxX, RoundMaxY;

    /// <summary>Rounded-rect clip corner radius.</summary>
    public float RoundRadius;

    /// <summary>1 when the rounded-rect clip applies.</summary>
    public uint HasRound;

    /// <summary>1 when the mask clip applies.</summary>
    public uint HasMask;

    private uint pad0;

    /// <summary>Device pixel of the mask's first texel.</summary>
    public int MaskOriginX, MaskOriginY;

    /// <summary>Atlas texel of the mask's first texel.</summary>
    public int MaskU, MaskV;

    /// <summary>Mask size in texels.</summary>
    public int MaskWidth, MaskHeight;

    private int pad1, pad2;
}

/// <summary>The geometry of a gradient paint.</summary>
public enum GradientKind : uint
{
    /// <summary>No gradient: the instance's solid colour.</summary>
    None = 0,

    /// <summary>t = x in gradient space.</summary>
    Linear = 1,

    /// <summary>t = |p| in gradient space.</summary>
    Radial = 2,

    /// <summary>t = angle of p / 2π, from the start angle.</summary>
    Sweep = 3,
}

/// <summary>
/// One gradient paint: the affine map from device pixel centres into gradient space, the kind,
/// and its stops in the frame's stop table. Layout is the WGSL <c>GradientEntry</c> (std430), 48 bytes.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 48)]
public struct GradientEntry
{
    /// <summary>Device → gradient-space linear part.</summary>
    public float A, B, C, D;

    /// <summary>Device → gradient-space translation.</summary>
    public float Tx, Ty;

    /// <summary>Sweep start angle (radians).</summary>
    public float StartAngle;

    private float pad0;

    /// <summary>Gradient kind.</summary>
    public GradientKind Kind;

    /// <summary>First stop in the stop table.</summary>
    public uint StopStart;

    /// <summary>Number of stops.</summary>
    public uint StopCount;

    private uint pad1;
}

/// <summary>A gradient stop: offset in [0, 1] and colour, linear-light, premultiplied. 32 bytes.</summary>
[StructLayout(LayoutKind.Sequential, Size = 32)]
public struct GradientStopEntry
{
    /// <summary>Offset along the gradient.</summary>
    public float Offset;

    private float pad0, pad1, pad2;

    /// <summary>Premultiplied linear colour.</summary>
    public float R, G, B, A;
}

/// <summary>A colour: linear-light channels with straight (unpremultiplied) alpha.</summary>
public readonly record struct ComposeColor(float R, float G, float B, float A)
{
    /// <summary>Transparent black.</summary>
    public static readonly ComposeColor Transparent = new(0f, 0f, 0f, 0f);

    /// <summary>The colour with its alpha multiplied by <paramref name="factor"/>.</summary>
    public ComposeColor WithOpacity(float factor) => this with { A = A * factor };
}

/// <summary>A gradient stop as authored: offset and straight linear colour.</summary>
public readonly record struct ComposeGradientStop(float Offset, ComposeColor Color);

/// <summary>
/// What a fill or stroke paints with: a solid colour, or a gradient defined in the draw's local
/// space. Linear: from (X0, Y0) to (X1, Y1). Radial: centre (X0, Y0), radius X1. Sweep: centre
/// (X0, Y0), start angle X1 (radians).
/// </summary>
public readonly struct ComposePaint
{
    private ComposePaint(ComposeColor color, GradientKind kind, float x0, float y0, float x1, float y1, ComposeGradientStop[]? stops)
    {
        Color = color;
        Kind = kind;
        X0 = x0;
        Y0 = y0;
        X1 = x1;
        Y1 = y1;
        Stops = stops;
    }

    /// <summary>Solid colour (or, for gradients, opaque white).</summary>
    public ComposeColor Color { get; }

    /// <summary>Gradient kind, or <see cref="GradientKind.None"/> for a solid colour.</summary>
    public GradientKind Kind { get; }

    /// <summary>First gradient parameter (see the type remarks).</summary>
    public float X0 { get; }

    /// <summary>Second gradient parameter.</summary>
    public float Y0 { get; }

    /// <summary>Third gradient parameter.</summary>
    public float X1 { get; }

    /// <summary>Fourth gradient parameter.</summary>
    public float Y1 { get; }

    /// <summary>Gradient stops (ascending offsets), or null for a solid colour.</summary>
    public ComposeGradientStop[]? Stops { get; }

    /// <summary>A solid-colour paint.</summary>
    public static ComposePaint Solid(ComposeColor color) => new(color, GradientKind.None, 0, 0, 0, 0, null);

    /// <summary>A linear gradient from (x0, y0) to (x1, y1) in the draw's local space.</summary>
    public static ComposePaint Linear(float x0, float y0, float x1, float y1, ComposeGradientStop[] stops)
        => new(new ComposeColor(1, 1, 1, 1), GradientKind.Linear, x0, y0, x1, y1, stops);

    /// <summary>A radial gradient about (cx, cy) with the given radius.</summary>
    public static ComposePaint Radial(float cx, float cy, float radius, ComposeGradientStop[] stops)
        => new(new ComposeColor(1, 1, 1, 1), GradientKind.Radial, cx, cy, radius, 0, stops);

    /// <summary>A sweep (conic) gradient about (cx, cy) starting at <paramref name="startAngle"/>.</summary>
    public static ComposePaint Sweep(float cx, float cy, float startAngle, ComposeGradientStop[] stops)
        => new(new ComposeColor(1, 1, 1, 1), GradientKind.Sweep, cx, cy, startAngle, 0, stops);

    /// <summary>True when the paint draws nothing (transparent solid, or a gradient without stops).</summary>
    public bool IsInvisible => Kind == GradientKind.None ? Color.A <= 0f : Stops is null || Stops.Length == 0;
}

/// <summary>Frame-wide shading parameters shared by every draw of a <see cref="DrawList"/>.</summary>
public struct ComposeParameters
{
    /// <summary>Glyph text-weight gamma; 0 selects the pure linear coverage blend.</summary>
    public float TextGamma;

    /// <summary>Adaptive light-on-dark text-weight strength (1 = full adaptive, 0 = symmetric).</summary>
    public float LightWeight;

    /// <summary>Per-frame pixel-dissolve threshold in [0, 1]; 0 disables it.</summary>
    public float Dissolve;
}

/// <summary>
/// The pixels of an image a <see cref="DrawList"/> draws: tightly packed RGBA8, sRGB-encoded, straight
/// alpha, as decoded from an 8-bit image file.
/// </summary>
public sealed class ComposeImage
{
    /// <summary>Creates an image over <paramref name="pixels"/> (not copied).</summary>
    public ComposeImage(byte[] pixels, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        Pixels = pixels;
        Width = width;
        Height = height;
    }

    /// <summary>RGBA8 pixels, row-major, stride <c>Width * 4</c>.</summary>
    public byte[] Pixels { get; }

    /// <summary>Width in texels.</summary>
    public int Width { get; }

    /// <summary>Height in texels.</summary>
    public int Height { get; }
}
