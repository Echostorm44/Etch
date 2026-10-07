using System.Runtime.InteropServices;
using Etch.Gpu;
using Etch.Text.Atlas;

namespace Etch.Compose.Cpu;

/// <summary>
/// Executes a <see cref="DrawList"/> on the CPU into a <see cref="CpuFramebuffer"/>, producing the
/// image the <see cref="GpuComposer"/> produces: the same batches in the same order, the same
/// per-pixel arithmetic (<see cref="CpuShading"/> ports the shaders), the same sRGB target model.
/// </summary>
/// <remarks>
/// <para>
/// A frame renders region by region: every batch is applied to one region before the next region
/// starts, which is exact because a draw only reads the pixel it writes — except backdrop blurs,
/// which read neighbours and therefore split the frame into phases. Mono glyph batches read the
/// background as it stood before the batch, from a copy taken per region, as the GPU copies the
/// framebuffer before each glyph batch.
/// </para>
/// <para>
/// The composer owns memory-backed glyph and mask atlases; build the frame's draw list against them.
/// </para>
/// </remarks>
public sealed class CpuComposer : IDisposable
{
    /// <summary>Monochrome glyph atlas page size (one page; resets when full).</summary>
    public const int GlyphAtlasSize = GpuComposer.GlyphAtlasSize;

    /// <summary>Colour glyph atlas page size (one page; resets when full).</summary>
    public const int ColorGlyphAtlasSize = GpuComposer.ColorGlyphAtlasSize;

    private readonly GlyphAtlas monoAtlas;
    private readonly GlyphAtlas colorAtlas;
    private readonly MaskAtlas maskAtlas;
    private uint[] snapshot = Array.Empty<uint>();
    private bool disposed;

    /// <summary>Creates a CPU composer with empty atlases.</summary>
    public CpuComposer()
    {
        monoAtlas = new GlyphAtlas(GlyphAtlasSize, TextureFormat.R8Unorm, 128, 1);
        colorAtlas = new GlyphAtlas(ColorGlyphAtlasSize, TextureFormat.Rgba8UnormSrgb, 128, 1);
        maskAtlas = new MaskAtlas();
    }

    /// <summary>The monochrome glyph atlas glyph instances' UVs refer to.</summary>
    public GlyphAtlas MonoAtlas => monoAtlas;

    /// <summary>The colour glyph atlas colour-glyph instances' UVs refer to.</summary>
    public GlyphAtlas ColorAtlas => colorAtlas;

    /// <summary>The coverage-mask atlas mask shapes and mask clips refer to.</summary>
    public MaskAtlas Masks => maskAtlas;

    /// <summary>When set, glyph batches are skipped (bisect text vs geometry).</summary>
    public bool SkipGlyphs { get; set; }

    /// <summary>Clears every atlas that filled up last frame (or all, when <paramref name="force"/>). Call between frames.</summary>
    public void ResetAtlasesIfExhausted(bool force)
    {
        if (force || monoAtlas.WasExhausted)
        {
            monoAtlas.Reset();
        }
        if (force || colorAtlas.WasExhausted)
        {
            colorAtlas.Reset();
        }
        if (force || maskAtlas.WasExhausted)
        {
            maskAtlas.Reset();
        }
    }

    /// <summary>Renders the whole of <paramref name="list"/> (finished) into <paramref name="target"/>.</summary>
    public void Render(DrawList list, CpuFramebuffer target)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(target);
        target.Resize((int)list.Width, (int)list.Height);
        if (snapshot.Length < target.Width * target.Height)
        {
            snapshot = new uint[target.Width * target.Height];
        }

        var region = new Region(0, 0, target.Width, target.Height);
        target.Pixels.Fill(0xFF000000u);
        var batches = list.Batches;
        for (int b = 0; b < batches.Length; b++)
        {
            RenderBatch(list, target, batches[b], region, snapshot);
        }
    }

    /// <summary>A rectangle of target pixels [X0, X1) × [Y0, Y1).</summary>
    internal readonly record struct Region(int X0, int Y0, int X1, int Y1);

    private void RenderBatch(DrawList list, CpuFramebuffer target, in DrawBatch batch, Region region, uint[] background)
    {
        if (batch.Count == 0)
        {
            return;
        }
        switch (batch.Kind)
        {
            case DrawKind.Shape:
                {
                    var shapes = CollectionsMarshal.AsSpan(list.OrderedShapes).Slice(batch.Start, batch.Count);
                    foreach (ref readonly var shape in shapes)
                    {
                        DrawShape(list, target, shape, region);
                    }
                    break;
                }
            case DrawKind.Glyph:
                {
                    if (SkipGlyphs)
                    {
                        return;
                    }
                    CopyBackground(target, batch, region, background);
                    var glyphs = CollectionsMarshal.AsSpan(list.OrderedGlyphs).Slice(batch.Start, batch.Count);
                    foreach (ref readonly var glyph in glyphs)
                    {
                        DrawGlyph(list, target, glyph, region, background);
                    }
                    break;
                }
            case DrawKind.ColorGlyph:
                {
                    if (SkipGlyphs)
                    {
                        return;
                    }
                    var glyphs = CollectionsMarshal.AsSpan(list.OrderedColorGlyphs).Slice(batch.Start, batch.Count);
                    foreach (ref readonly var glyph in glyphs)
                    {
                        DrawColorGlyph(list, target, glyph, region);
                    }
                    break;
                }
            case DrawKind.Image:
                {
                    var images = CollectionsMarshal.AsSpan(list.OrderedImages).Slice(batch.Start, batch.Count);
                    foreach (ref readonly var image in images)
                    {
                        DrawImage(list, target, image, region);
                    }
                    break;
                }
            case DrawKind.Blur:
                {
                    CopyBackground(target, batch, region, background);
                    var blurs = CollectionsMarshal.AsSpan(list.OrderedBlurs).Slice(batch.Start, batch.Count);
                    foreach (ref readonly var blur in blurs)
                    {
                        DrawBlur(list, target, blur, region, background);
                    }
                    break;
                }
        }
    }

    // The GPU copies the batch's bounds (rounded out) before a glyph or blur batch; pixels outside
    // that rect keep their previous copy, which nothing in the batch reads.
    private static void CopyBackground(CpuFramebuffer target, in DrawBatch batch, Region region, uint[] background)
    {
        int x0 = Math.Max(region.X0, (int)Math.Clamp(MathF.Floor(batch.MinX), 0f, target.Width));
        int y0 = Math.Max(region.Y0, (int)Math.Clamp(MathF.Floor(batch.MinY), 0f, target.Height));
        int x1 = Math.Min(region.X1, (int)Math.Clamp(MathF.Ceiling(batch.MaxX), 0f, target.Width));
        int y1 = Math.Min(region.Y1, (int)Math.Clamp(MathF.Ceiling(batch.MaxY), 0f, target.Height));
        if (x1 <= x0 || y1 <= y0)
        {
            return;
        }
        var pixels = target.Pixels;
        int width = target.Width;
        for (int y = y0; y < y1; y++)
        {
            pixels.Slice(y * width + x0, x1 - x0).CopyTo(background.AsSpan(y * width + x0, x1 - x0));
        }
    }

    // Pixels whose centre lies in [min, max) — the GPU rasterization rule for a quad.
    private static bool PixelSpan(float min, float max, int regionMin, int regionMax, out int first, out int end)
    {
        first = Math.Max(regionMin, (int)MathF.Ceiling(min - 0.5f));
        end = Math.Min(regionMax, (int)MathF.Ceiling(max - 0.5f));
        return end > first;
    }

    private ReadOnlySpan<byte> MaskPage => maskAtlas.HasPage ? maskAtlas.PagePixels : ReadOnlySpan<byte>.Empty;

    private void DrawShape(DrawList list, CpuFramebuffer target, in ShapeInstance inst, Region region)
    {
        if (!PixelSpan(inst.MinX, inst.MaxX, region.X0, region.X1, out int x0, out int x1)
            || !PixelSpan(inst.MinY, inst.MaxY, region.Y0, region.Y1, out int y0, out int y1))
        {
            return;
        }
        var clip = list.Clips[(int)inst.ClipIndex];
        var gradients = list.Gradients;
        var stops = list.GradientStops;
        var mask = MaskPage;
        float dissolve = list.Parameters.Dissolve;
        var pixels = target.Pixels;
        int width = target.Width;
        for (int y = y0; y < y1; y++)
        {
            float py = y + 0.5f;
            int row = y * width;
            for (int x = x0; x < x1; x++)
            {
                float px = x + 0.5f;
                if (CpuShading.Dissolved(px, py, dissolve))
                {
                    continue;
                }
                float cov = CpuShading.ShapeCoverage(inst, px, py, mask) * CpuShading.ClipCoverage(px, py, clip, mask);
                if (cov <= 0f)
                {
                    continue;
                }
                var (r, g, b, a) = CpuShading.PaintColor(inst, px, py, gradients, stops);
                pixels[row + x] = CpuShading.BlendStraight(pixels[row + x], r, g, b, a * cov);
            }
        }
    }

    private void DrawGlyph(DrawList list, CpuFramebuffer target, in GlyphInstance g, Region region, uint[] background)
    {
        if (!PixelSpan(g.PosX, g.PosX + g.SizeX, region.X0, region.X1, out int x0, out int x1)
            || !PixelSpan(g.PosY, g.PosY + g.SizeY, region.Y0, region.Y1, out int y0, out int y1))
        {
            return;
        }
        var clip = list.Clips[(int)g.ClipIndex];
        var mask = MaskPage;
        var page = monoAtlas.GetPage(0).Pixels!;
        int dim = monoAtlas.Dimension;
        var p = list.Parameters;
        var pixels = target.Pixels;
        int width = target.Width;
        float du = (g.AtlasU1 - g.AtlasU0) / g.SizeX;
        float dv = (g.AtlasV1 - g.AtlasV0) / g.SizeY;
        for (int y = y0; y < y1; y++)
        {
            float py = y + 0.5f;
            int row = y * width;
            float v = g.AtlasV0 + (py - g.PosY) * dv;
            int ty = Math.Clamp((int)MathF.Floor(v * dim), 0, dim - 1);
            for (int x = x0; x < x1; x++)
            {
                float px = x + 0.5f;
                if (CpuShading.Dissolved(px, py, p.Dissolve))
                {
                    continue;
                }
                float clipCov = CpuShading.ClipCoverage(px, py, clip, mask);
                if (clipCov <= 0f)
                {
                    continue;
                }
                float u = g.AtlasU0 + (px - g.PosX) * du;
                int tx = Math.Clamp((int)MathF.Floor(u * dim), 0, dim - 1);
                float cov = page[ty * dim + tx] * (1f / 255f);
                float bgLum = CpuShading.PixelLuminance(background[row + x]);
                float alpha = g.A * CpuShading.WeightedCoverage(cov, g.ForegroundLuminance, bgLum, p.TextGamma, p.LightWeight) * clipCov;
                pixels[row + x] = CpuShading.BlendPremultiplied(pixels[row + x], g.R * alpha, g.G * alpha, g.B * alpha, alpha);
            }
        }
    }

    private void DrawColorGlyph(DrawList list, CpuFramebuffer target, in GlyphInstance g, Region region)
    {
        if (!PixelSpan(g.PosX, g.PosX + g.SizeX, region.X0, region.X1, out int x0, out int x1)
            || !PixelSpan(g.PosY, g.PosY + g.SizeY, region.Y0, region.Y1, out int y0, out int y1))
        {
            return;
        }
        var clip = list.Clips[(int)g.ClipIndex];
        var mask = MaskPage;
        var page = colorAtlas.GetPage(0).Pixels!;
        int dim = colorAtlas.Dimension;
        float dissolve = list.Parameters.Dissolve;
        var pixels = target.Pixels;
        int width = target.Width;
        float du = (g.AtlasU1 - g.AtlasU0) / g.SizeX;
        float dv = (g.AtlasV1 - g.AtlasV0) / g.SizeY;
        for (int y = y0; y < y1; y++)
        {
            float py = y + 0.5f;
            int row = y * width;
            float v = g.AtlasV0 + (py - g.PosY) * dv;
            for (int x = x0; x < x1; x++)
            {
                float px = x + 0.5f;
                if (CpuShading.Dissolved(px, py, dissolve))
                {
                    continue;
                }
                float clipCov = CpuShading.ClipCoverage(px, py, clip, mask);
                if (clipCov <= 0f)
                {
                    continue;
                }
                float u = g.AtlasU0 + (px - g.PosX) * du;
                var (r, gr, b, a) = CpuShading.SampleBilinearSrgb(page, dim, 0, 0, dim, dim, u, v);
                float alpha = a * g.A * clipCov;
                pixels[row + x] = CpuShading.BlendPremultiplied(pixels[row + x], r * alpha, gr * alpha, b * alpha, alpha);
            }
        }
    }

    private void DrawImage(DrawList list, CpuFramebuffer target, in ImageInstance inst, Region region)
    {
        if (!PixelSpan(inst.MinX, inst.MaxX, region.X0, region.X1, out int x0, out int x1)
            || !PixelSpan(inst.MinY, inst.MaxY, region.Y0, region.Y1, out int y0, out int y1))
        {
            return;
        }
        if (!list.Images.TryGetValue(inst.Handle, out var image))
        {
            return;
        }
        var clip = list.Clips[(int)inst.ClipIndex];
        var mask = MaskPage;
        var pixels = target.Pixels;
        int width = target.Width;
        float gu = MathF.Sqrt(inst.Ux * inst.Ux + inst.Uy * inst.Uy);
        float gv = MathF.Sqrt(inst.Vx * inst.Vx + inst.Vy * inst.Vy);
        for (int y = y0; y < y1; y++)
        {
            float py = y + 0.5f;
            int row = y * width;
            for (int x = x0; x < x1; x++)
            {
                float px = x + 0.5f;
                float u = inst.Ux * px + inst.Uy * py + inst.U0;
                float v = inst.Vx * px + inst.Vy * py + inst.V0;
                float edge;
                if (inst.EdgeAntialiasing != 0f)
                {
                    float du = MathF.Min(u, 1f - u) / gu;
                    float dv = MathF.Min(v, 1f - v) / gv;
                    edge = Math.Clamp(du + 0.5f, 0f, 1f) * Math.Clamp(dv + 0.5f, 0f, 1f);
                }
                else
                {
                    edge = u >= 0f && u < 1f && v >= 0f && v < 1f ? 1f : 0f;
                }
                float cov = edge * CpuShading.ClipCoverage(px, py, clip, mask) * inst.Opacity;
                if (cov <= 0f)
                {
                    continue;
                }
                var (r, g, b, a) = CpuShading.SampleBilinearSrgb(image.Pixels, image.Width, 0, 0, image.Width, image.Height, u, v);
                pixels[row + x] = CpuShading.BlendStraight(pixels[row + x], r, g, b, a * cov);
            }
        }
    }

    private void DrawBlur(DrawList list, CpuFramebuffer target, in BlurInstance inst, Region region, uint[] background)
    {
        if (!PixelSpan(inst.MinX, inst.MaxX, region.X0, region.X1, out int x0, out int x1)
            || !PixelSpan(inst.MinY, inst.MaxY, region.Y0, region.Y1, out int y0, out int y1))
        {
            return;
        }
        var clip = list.Clips[(int)inst.ClipIndex];
        var mask = MaskPage;
        var pixels = target.Pixels;
        int width = target.Width;
        int height = target.Height;
        float sigma = MathF.Max(inst.Sigma, 0.5f);
        float step = sigma * 0.5f;
        float invW = 1f / width;
        float invH = 1f / height;
        Span<float> weights = stackalloc float[49];
        float wsum = 0f;
        for (int j = -3; j <= 3; j++)
        {
            for (int i = -3; i <= 3; i++)
            {
                float ox = i * step, oy = j * step;
                float w = MathF.Exp(-(ox * ox + oy * oy) / (2f * sigma * sigma));
                weights[(j + 3) * 7 + i + 3] = w;
                wsum += w;
            }
        }
        float norm = 1f / MathF.Max(wsum, 0.0001f);

        for (int y = y0; y < y1; y++)
        {
            float py = y + 0.5f;
            int row = y * width;
            for (int x = x0; x < x1; x++)
            {
                float px = x + 0.5f;
                float dist = CpuShading.SdfRoundRect(px, py, inst.MinX, inst.MinY, inst.MaxX, inst.MaxY, inst.Radius);
                if (dist > 1f)
                {
                    continue;
                }
                float coverage = (1f - CpuShading.SmoothStep(-1f, 1f, dist)) * CpuShading.ClipCoverage(px, py, clip, mask) * inst.Opacity;
                if (coverage <= 0f)
                {
                    continue;
                }
                float ar = 0f, ag = 0f, ab = 0f;
                for (int j = -3; j <= 3; j++)
                {
                    for (int i = -3; i <= 3; i++)
                    {
                        float w = weights[(j + 3) * 7 + i + 3];
                        var (sr, sg, sb) = CpuShading.SampleBilinearTarget(background, width, height, (px + i * step) * invW, (py + j * step) * invH);
                        ar += sr * w;
                        ag += sg * w;
                        ab += sb * w;
                    }
                }
                float br = ar * norm, bg = ag * norm, bb = ab * norm;
                float or = br + (inst.TintR - br) * inst.TintA;
                float og = bg + (inst.TintG - bg) * inst.TintA;
                float ob = bb + (inst.TintB - bb) * inst.TintA;
                pixels[row + x] = CpuShading.BlendPremultiplied(pixels[row + x], or * coverage, og * coverage, ob * coverage, coverage);
            }
        }
    }

    /// <summary>Releases nothing unmanaged; present for symmetry with the GPU composer.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        monoAtlas.Dispose();
        colorAtlas.Dispose();
        maskAtlas.Dispose();
    }
}
