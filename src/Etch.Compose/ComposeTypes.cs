using System.Runtime.InteropServices;

namespace Etch.Compose;

/// <summary>The kinds of draw a <see cref="DrawList"/> holds; each executes with its own GPU pipeline.</summary>
public enum DrawKind : byte
{
    /// <summary>An analytic shape instance (<see cref="ShapeInstance"/>).</summary>
    Shape,

    /// <summary>A monochrome glyph quad sampling the coverage atlas (<see cref="GlyphInstance"/>).</summary>
    Glyph,

    /// <summary>A colour glyph quad sampling the RGBA atlas (<see cref="GlyphInstance"/>).</summary>
    ColorGlyph,

    /// <summary>A textured image quad (<see cref="ImageQuad"/>).</summary>
    Image,

    /// <summary>A frosted-glass backdrop blur (<see cref="BlurInstance"/>).</summary>
    Blur,
}

/// <summary>
/// One analytic shape: the quad <c>[Min, Max]</c> (grown by <see cref="Expand"/>) is rasterized and
/// every covered pixel evaluates the shape's coverage for <see cref="ShapeType"/>. The layout is the
/// WGSL <c>ShapeInstance</c> storage-buffer element (std430), 96 bytes.
/// </summary>
/// <remarks>
/// Shape types: 0 solid rect (pixel-centre rule, no AA), 1 circle, 2 two-stop gradient rect,
/// 3 ring, 4 line capsule, 5 rounded-rect fill, 6 rounded-rect stroke, 7 rounded-rect gradient,
/// 8 annular sector, 9 analytic drop shadow. Colours are linear-light, straight alpha.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Size = 96)]
public struct ShapeInstance
{
    /// <summary>Quad bounds, device pixels (offset 0).</summary>
    public float MinX, MinY;

    /// <summary>Quad bounds, device pixels (offset 8).</summary>
    public float MaxX, MaxY;

    /// <summary>Primary colour, linear straight alpha (offset 16).</summary>
    public float R0, G0, B0, A0;

    /// <summary>Second gradient colour, linear straight alpha (offset 32).</summary>
    public float R1, G1, B1, A1;

    /// <summary>Type-specific points: gradient endpoints, line endpoints, shadow rect, sector angles (offset 48).</summary>
    public float P0X, P0Y, P1X, P1Y;

    /// <summary>Which coverage function the shader evaluates (offset 64).</summary>
    public uint ShapeType;

    private uint pad0;

    /// <summary>Circle / ring / sector centre (offset 72).</summary>
    public float CenterX, CenterY;

    /// <summary>Corner, circle or outer radius (offset 80).</summary>
    public float Radius;

    /// <summary>Stroke width, sector inner radius or shadow sigma (offset 84).</summary>
    public float StrokeWidth;

    /// <summary>How far the rasterized quad extends past the bounds, for antialiasing (offset 88).</summary>
    public float Expand;

    private float pad1;

    /// <summary>
    /// Returns a copy shifted by (dx, dy): every positional field moves; colours, type, radius,
    /// stroke width and expand carry through unchanged via <c>with</c>, so a field added later
    /// cannot be dropped by a hand-written copy.
    /// </summary>
    public readonly ShapeInstance Translated(float dx, float dy) => this with
    {
        MinX = MinX + dx,
        MinY = MinY + dy,
        MaxX = MaxX + dx,
        MaxY = MaxY + dy,
        P0X = P0X + dx,
        P0Y = P0Y + dy,
        P1X = P1X + dx,
        P1Y = P1Y + dy,
        CenterX = CenterX + dx,
        CenterY = CenterY + dy,
    };
}

/// <summary>
/// One glyph quad: device position and size, atlas UVs (V0 at the quad's top edge, V1 at its bottom,
/// because atlas bitmaps are stored bottom-up), colour and an optional clip rect. Layout is the WGSL
/// <c>GlyphInstance</c> (std430), 48 bytes. A clip with <c>ClipMax &lt;= ClipMin</c> on both axes means
/// unclipped.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 48)]
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

    /// <summary>Text colour as the shader receives it.</summary>
    public float R, G, B, A;

    /// <summary>Clip rect minimum, device pixels.</summary>
    public float ClipMinX, ClipMinY;

    /// <summary>Clip rect maximum, device pixels.</summary>
    public float ClipMaxX, ClipMaxY;
}

/// <summary>One image quad: device rect plus the UV sub-rect that survives clipping.</summary>
public struct ImageQuad
{
    /// <summary>Image handle; keys <see cref="DrawList.Images"/> and the GPU texture cache.</summary>
    public int Handle;

    /// <summary>Device-pixel rect.</summary>
    public float L, T, R, B;

    /// <summary>Normalized texture coordinates of the rect corners.</summary>
    public float U0, V0, U1, V1;
}

/// <summary>
/// One frosted-glass backdrop blur: a device-space rounded rect filled with a Gaussian blur of what is
/// already drawn behind it, then tinted. Layout matches the WGSL <c>BlurInstance</c> (std430), 48 bytes.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 48)]
public struct BlurInstance
{
    /// <summary>Panel rect minimum, device pixels.</summary>
    public float MinX, MinY;

    /// <summary>Panel rect maximum, device pixels.</summary>
    public float MaxX, MaxY;

    /// <summary>Tint colour.</summary>
    public float TintR, TintG, TintB, TintA;

    /// <summary>Corner radius, device pixels.</summary>
    public float Radius;

    /// <summary>Gaussian standard deviation, device pixels.</summary>
    public float Sigma;

    private float pad0, pad1;
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
