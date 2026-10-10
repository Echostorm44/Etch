using System.Runtime.CompilerServices;

namespace Etch.Compose.Cpu;

/// <summary>
/// Scalar ports of the composer shaders (<c>ComposeShaders</c>): every coverage, clip, paint and
/// text-weight function, with the same arithmetic in the same order, so the CPU composer computes
/// the same values the GPU fragment shaders do (to float rounding).
/// </summary>
internal static class CpuShading
{
    private const float TwoPi = 6.28318530718f;

    // ── sRGB transfer ──────────────────────────────────────────────────

    /// <summary>sRGB byte → linear.</summary>
    public static readonly float[] Decode = BuildDecode();

    // Linear values at the midpoints between consecutive sRGB codes: code k covers [T[k-1], T[k]).
    private static readonly float[] EncodeThresholds = BuildThresholds();

    // For a linear value v, EncodeLookup[(int)(v * 4095)] is the code at the start of its bucket;
    // at most a few threshold steps refine it.
    private static readonly byte[] EncodeLookup = BuildEncodeLookup();

    private static float[] BuildDecode()
    {
        var table = new float[256];
        for (int i = 0; i < 256; i++)
        {
            double c = i / 255.0;
            table[i] = (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4));
        }
        return table;
    }

    private static float[] BuildThresholds()
    {
        var table = new float[256];
        for (int i = 0; i < 255; i++)
        {
            double c = (i + 0.5) / 255.0;
            table[i] = (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4));
        }
        table[255] = float.MaxValue;
        return table;
    }

    private static byte[] BuildEncodeLookup()
    {
        var table = new byte[4096];
        int code = 0;
        for (int i = 0; i < 4096; i++)
        {
            float v = i / 4095f;
            while (code < 255 && v >= EncodeThresholds[code])
            {
                code++;
            }
            table[i] = (byte)code;
        }
        return table;
    }

    /// <summary>Linear → nearest sRGB byte (exact rounding in sRGB space).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Encode(float linear)
    {
        if (!(linear > 0f))
        {
            return 0;
        }
        if (linear >= 1f)
        {
            return 255;
        }
        int code = EncodeLookup[(int)(linear * 4095f)];
        while (linear >= EncodeThresholds[code])
        {
            code++;
        }
        return (uint)code;
    }

    /// <summary>Linear alpha → UNORM8 (round to nearest).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint EncodeAlpha(float alpha) => (uint)(Math.Clamp(alpha, 0f, 1f) * 255f + 0.5f);

    /// <summary>sRGB float → linear, as the WGSL <c>srgb_to_lin</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SrgbToLinear(float c) => c > 0.04045f ? MathF.Pow((c + 0.055f) / 1.055f, 2.4f) : c / 12.92f;

    // ── Blending (sRGB target: linear-light blend, 8-bit re-quantization) ─────────

    /// <summary>
    /// Straight-alpha source over the pixel: colour <c>src·a + dst·(1 − a)</c>, alpha
    /// <c>a + dstA·(1 − a)</c> (the shape and image pipelines' blend state).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint BlendStraight(uint dst, float r, float g, float b, float a)
    {
        float inv = 1f - a;
        float dr = Decode[(dst >> 16) & 0xFF];
        float dg = Decode[(dst >> 8) & 0xFF];
        float db = Decode[dst & 0xFF];
        float da = (dst >> 24) * (1f / 255f);
        return Pack(r * a + dr * inv, g * a + dg * inv, b * a + db * inv, a + da * inv);
    }

    /// <summary>Premultiplied source over the pixel (the glyph and blur pipelines' blend state).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint BlendPremultiplied(uint dst, float r, float g, float b, float a)
    {
        float inv = 1f - a;
        float dr = Decode[(dst >> 16) & 0xFF];
        float dg = Decode[(dst >> 8) & 0xFF];
        float db = Decode[dst & 0xFF];
        float da = (dst >> 24) * (1f / 255f);
        return Pack(r + dr * inv, g + dg * inv, b + db * inv, a + da * inv);
    }

    /// <summary>Packs linear colour + alpha to 0xAARRGGBB, sRGB-encoding the colour.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Pack(float r, float g, float b, float a)
        => (EncodeAlpha(a) << 24) | (Encode(r) << 16) | (Encode(g) << 8) | Encode(b);

    // ── Shared shader helpers ───────────────────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float EdgeCoverage(float dist) => Math.Clamp(0.5f - dist, 0f, 1f);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SmoothStep(float e0, float e1, float x)
    {
        float t = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    public static float SdfRoundRect(float px, float py, float loX, float loY, float hiX, float hiY, float r)
    {
        float cx = (loX + hiX) * 0.5f;
        float cy = (loY + hiY) * 0.5f;
        float hx = (hiX - loX) * 0.5f;
        float hy = (hiY - loY) * 0.5f;
        float dx = MathF.Abs(px - cx) - hx + r;
        float dy = MathF.Abs(py - cy) - hy + r;
        float mx = MathF.Max(dx, 0f);
        float my = MathF.Max(dy, 0f);
        return MathF.Sqrt(mx * mx + my * my) + MathF.Min(MathF.Max(dx, dy), 0f) - r;
    }

    /// <summary>The WGSL dissolve hash: true when the pixel is dissolved away this frame.</summary>
    public static bool Dissolved(float px, float py, float dissolve)
    {
        if (dissolve <= 0f)
        {
            return false;
        }
        float v = MathF.Sin(MathF.Floor(px) * 12.9898f + MathF.Floor(py) * 78.233f) * 43758.5453f;
        float dn = v - MathF.Floor(v);
        return dn < dissolve;
    }

    public static float MaskTexel(float px, float py, int originX, int originY, int u, int v, int width, int height, ReadOnlySpan<byte> page, int stride)
    {
        int tx = (int)MathF.Floor(px) - originX;
        int ty = (int)MathF.Floor(py) - originY;
        if (tx < 0 || ty < 0 || tx >= width || ty >= height || page.IsEmpty)
        {
            return 0f;
        }
        return page[(ty + v) * stride + tx + u] * (1f / 255f);
    }

    /// <summary>The WGSL <c>clip_mask</c>: a tiled clip mask's coverage at the pixel.</summary>
    public static float ClipMask(float px, float py, in ClipEntry c, in MaskSource masks)
    {
        int tx = (int)MathF.Floor(px) - c.MaskOriginX;
        int ty = (int)MathF.Floor(py) - c.MaskOriginY;
        if (tx < 0 || ty < 0 || tx >= c.MaskWidth || ty >= c.MaskHeight)
        {
            return 0f;
        }
        const int shift = 8;
        const int low = MaskAtlas.ClipMaskTile - 1;
        ref readonly var tile = ref masks.Tiles[c.MaskTileStart + (ty >> shift) * c.MaskTileColumns + (tx >> shift)];
        if (tile.Value >= 0)
        {
            return tile.Value / 255f;
        }
        var page = masks.Page(tile.Layer);
        return page[(tile.V + (ty & low)) * masks.Stride + tile.U + (tx & low)] * (1f / 255f);
    }

    public static float ClipCoverage(float px, float py, in ClipEntry c, in MaskSource masks)
    {
        if (px < c.MinX || px >= c.MaxX || py < c.MinY || py >= c.MaxY)
        {
            return 0f;
        }
        float cov = 1f;
        if (c.HasRound != 0)
        {
            cov = EdgeCoverage(SdfRoundRect(px, py, c.RoundMinX, c.RoundMinY, c.RoundMaxX, c.RoundMaxY, c.RoundRadius));
        }
        if (c.HasMask != 0)
        {
            cov *= ClipMask(px, py, c, masks);
        }
        return cov;
    }

    // ── Shapes ──────────────────────────────────────────────────────────

    private static float SectorAngularDistance(float dx, float dy, float dist, float startAngle, float sweepAngle)
    {
        float angle = MathF.Atan2(dy, dx);
        float endAngle = startAngle + sweepAngle;
        float test = angle;
        if (sweepAngle > 0f)
        {
            for (int turns = 0; turns < 4 && test < startAngle; turns++)
            {
                test += TwoPi;
            }
            for (int turns = 0; turns < 4 && test > startAngle + TwoPi; turns++)
            {
                test -= TwoPi;
            }
        }
        else
        {
            for (int turns = 0; turns < 4 && test > startAngle; turns++)
            {
                test -= TwoPi;
            }
            for (int turns = 0; turns < 4 && test < startAngle - TwoPi; turns++)
            {
                test += TwoPi;
            }
        }
        float signedAngular;
        if (sweepAngle > 0f)
        {
            signedAngular = test <= endAngle
                ? -MathF.Min(test - startAngle, endAngle - test)
                : MathF.Min(test - endAngle, startAngle + TwoPi - test);
        }
        else
        {
            signedAngular = test >= endAngle
                ? -MathF.Min(startAngle - test, test - endAngle)
                : MathF.Min(endAngle - test, test - (startAngle - TwoPi));
        }
        return signedAngular * dist;
    }

    private static (float X, float Y) ShadowErf(float x0, float x1)
    {
        return (Erf(x0), Erf(x1));

        static float Erf(float x)
        {
            // A select, not MathF.Sign (which throws on NaN): NaN gives 0, as the vector paths do.
            float s = x > 0f ? 1f : x < 0f ? -1f : 0f;
            float a = MathF.Abs(x);
            float r = 1f + (0.278393f + (0.230389f + 0.078108f * (a * a)) * a) * a;
            r *= r;
            return s - s / (r * r);
        }
    }

    private static float ShadowX(float x, float y, float sigma, float corner, float halfX, float halfY)
    {
        float delta = MathF.Min(halfY - corner - MathF.Abs(y), 0f);
        float curved = halfX - corner + MathF.Sqrt(MathF.Max(0f, corner * corner - delta * delta));
        float k = 0.70710678f / sigma;
        var (e0, e1) = ShadowErf((x - curved) * k, (x + curved) * k);
        return (0.5f + 0.5f * e1) - (0.5f + 0.5f * e0);
    }

    private static float RoundedBoxShadow(float loX, float loY, float hiX, float hiY, float px, float py, float sigma, float cornerIn)
    {
        float cx = (loX + hiX) * 0.5f;
        float cy = (loY + hiY) * 0.5f;
        float halfX = (hiX - loX) * 0.5f;
        float halfY = (hiY - loY) * 0.5f;
        float corner = MathF.Min(cornerIn, MathF.Min(halfX, halfY));
        float x = px - cx;
        float y = py - cy;
        float low = y - halfY;
        float high = y + halfY;
        float start = Math.Clamp(-3f * sigma, low, high);
        float end = Math.Clamp(3f * sigma, low, high);
        float step = (end - start) / 4f;
        float t = start + step * 0.5f;
        float value = 0f;
        for (int i = 0; i < 4; i++)
        {
            float g = MathF.Exp(-(t * t) / (2f * sigma * sigma)) / (2.50662827f * sigma);
            value += ShadowX(x, y - t, sigma, corner, halfX, halfY) * g * step;
            t += step;
        }
        return Math.Clamp(value, 0f, 1f);
    }

    /// <summary>The WGSL <c>shape_coverage</c> at pixel centre (px, py).</summary>
    public static float ShapeCoverage(in ShapeInstance inst, float px, float py, in MaskSource masks)
    {
        float lx = inst.FrameA * px + inst.FrameB * py + inst.FrameTx;
        float ly = inst.FrameC * px + inst.FrameD * py + inst.FrameTy;
        float s = inst.Scale;
        switch (inst.Type)
        {
            case ShapeType.Rect:
                return lx >= inst.P0 && lx < inst.P2 && ly >= inst.P1 && ly < inst.P3 ? 1f : 0f;

            case ShapeType.Circle:
                {
                    float dx = lx - inst.P0, dy = ly - inst.P1;
                    return EdgeCoverage((MathF.Sqrt(dx * dx + dy * dy) - inst.P2) * s);
                }

            case ShapeType.Ring:
                {
                    float dx = lx - inst.P0, dy = ly - inst.P1;
                    float d = MathF.Sqrt(dx * dx + dy * dy);
                    float outer = EdgeCoverage((d - inst.P2) * s);
                    float inner = EdgeCoverage(((inst.P2 - inst.P3) - d) * s);
                    return MathF.Min(outer, inner);
                }

            case ShapeType.Line:
                {
                    float ax = inst.P0, ay = inst.P1;
                    float bax = inst.P2 - ax, bay = inst.P3 - ay;
                    float len = MathF.Sqrt(bax * bax + bay * bay);
                    if (!(len > 0f))
                    {
                        return 0f;
                    }
                    float dirX = bax / len, dirY = bay / len;
                    float rx = lx - ax, ry = ly - ay;
                    float hw = inst.Q0;
                    float dist;
                    if (inst.Q1 == 1f)
                    {
                        float h = Math.Clamp((rx * bax + ry * bay) / (len * len), 0f, 1f);
                        float ex = rx - bax * h, ey = ry - bay * h;
                        dist = MathF.Sqrt(ex * ex + ey * ey) - hw;
                    }
                    else
                    {
                        float ext = inst.Q1 == 2f ? hw : 0f;
                        float along = rx * dirX + ry * dirY;
                        float perp = rx * -dirY + ry * dirX;
                        float cx = MathF.Abs(along - len * 0.5f) - (len * 0.5f + ext);
                        float cy = MathF.Abs(perp) - hw;
                        float mx = MathF.Max(cx, 0f), my = MathF.Max(cy, 0f);
                        dist = MathF.Sqrt(mx * mx + my * my) + MathF.Min(MathF.Max(cx, cy), 0f);
                    }
                    return EdgeCoverage(dist * s);
                }

            case ShapeType.RoundedRect:
                return EdgeCoverage(SdfRoundRect(lx, ly, inst.P0, inst.P1, inst.P2, inst.P3, inst.Q0) * s);

            case ShapeType.Border:
                {
                    float outer = EdgeCoverage(SdfRoundRect(lx, ly, inst.P0, inst.P1, inst.P2, inst.P3, inst.Q0) * s);
                    float w = inst.Q1;
                    float inner = EdgeCoverage(-SdfRoundRect(lx, ly, inst.P0 + w, inst.P1 + w, inst.P2 - w, inst.P3 - w, inst.Q2) * s);
                    return MathF.Min(outer, inner);
                }

            case ShapeType.Sector:
                {
                    float dx = lx - inst.P0, dy = ly - inst.P1;
                    float dist = MathF.Sqrt(dx * dx + dy * dy);
                    float cov = EdgeCoverage((dist - inst.P2) * s);
                    if (inst.P3 > 0f)
                    {
                        cov = MathF.Min(cov, EdgeCoverage((inst.P3 - dist) * s));
                    }
                    cov = MathF.Min(cov, EdgeCoverage(SectorAngularDistance(dx, dy, dist, inst.Q0, inst.Q1) * s));
                    if (inst.Q2 == 1f)
                    {
                        float mid = (inst.P2 + inst.P3) * 0.5f;
                        float capR = (inst.P2 - inst.P3) * 0.5f;
                        float end = inst.Q0 + inst.Q1;
                        float csx = inst.P0 + mid * MathF.Cos(inst.Q0), csy = inst.P1 + mid * MathF.Sin(inst.Q0);
                        float cex = inst.P0 + mid * MathF.Cos(end), cey = inst.P1 + mid * MathF.Sin(end);
                        float ds = MathF.Sqrt((lx - csx) * (lx - csx) + (ly - csy) * (ly - csy));
                        float de = MathF.Sqrt((lx - cex) * (lx - cex) + (ly - cey) * (ly - cey));
                        cov = MathF.Max(cov, EdgeCoverage((ds - capR) * s));
                        cov = MathF.Max(cov, EdgeCoverage((de - capR) * s));
                    }
                    return cov;
                }

            case ShapeType.Shadow:
                return RoundedBoxShadow(inst.P0, inst.P1, inst.P2, inst.P3, lx, ly, inst.Q1, inst.Q0);

            case ShapeType.Mask:
                return MaskTexel(px, py, (int)inst.P0, (int)inst.P1, (int)inst.P2, (int)inst.P3, (int)inst.Q0, (int)inst.Q1, masks.Page((int)inst.Q2), masks.Stride);

            default:
                return 0f;
        }
    }

    // ── Paint ───────────────────────────────────────────────────────────

    /// <summary>The WGSL <c>paint_color</c>: straight linear colour and alpha at the pixel.</summary>
    public static (float R, float G, float B, float A) PaintColor(in ShapeInstance inst, float px, float py,
        ReadOnlySpan<GradientEntry> gradients, ReadOnlySpan<GradientStopEntry> stops)
    {
        if (inst.PaintIndex == 0)
        {
            return (inst.R0, inst.G0, inst.B0, inst.A0);
        }
        ref readonly var g = ref gradients[(int)inst.PaintIndex];
        float gx = g.A * px + g.B * py + g.Tx;
        float gy = g.C * px + g.D * py + g.Ty;
        float t = gx;
        if (g.Kind == GradientKind.Radial)
        {
            t = MathF.Sqrt(gx * gx + gy * gy);
        }
        else if (g.Kind == GradientKind.Sweep)
        {
            // The centre has no angle: pin it (within a thousandth of a pixel) to the start.
            float a = (MathF.Atan2(gy, gx) - g.StartAngle) / TwoPi;
            t = gx * gx + gy * gy < 1e-6f ? 0f : a - MathF.Floor(a);
        }
        var (r, gr, b, al) = GradientColor(g, t, stops);
        if (al > 0f)
        {
            return (r / al, gr / al, b / al, al * inst.A0);
        }
        return (0f, 0f, 0f, 0f);
    }

    private static (float R, float G, float B, float A) GradientColor(in GradientEntry g, float tIn, ReadOnlySpan<GradientStopEntry> stops)
    {
        float t = Math.Clamp(tIn, 0f, 1f);
        int start = (int)g.StopStart;
        ref readonly var prev = ref stops[start];
        if (g.StopCount == 1 || t <= prev.Offset)
        {
            return (prev.R, prev.G, prev.B, prev.A);
        }
        for (int i = 1; i < (int)g.StopCount; i++)
        {
            ref readonly var cur = ref stops[start + i];
            if (t < cur.Offset)
            {
                float span = cur.Offset - prev.Offset;
                float f = span > 0f ? (t - prev.Offset) / span : 1f;
                return (prev.R + (cur.R - prev.R) * f, prev.G + (cur.G - prev.G) * f,
                    prev.B + (cur.B - prev.B) * f, prev.A + (cur.A - prev.A) * f);
            }
            prev = ref cur;
        }
        return (prev.R, prev.G, prev.B, prev.A);
    }

    // ── Text ────────────────────────────────────────────────────────────

    /// <summary>The WGSL <c>weighted_coverage</c> (adaptive text weight).</summary>
    public static float WeightedCoverage(float cov, float fgLum, float bgLum, float textGamma, float lightWeight)
    {
        if (textGamma <= 0f)
        {
            return cov;
        }
        float pc = 1f - MathF.Pow(MathF.Max(1f - cov, 0f), textGamma);
        float rel = Math.Clamp(fgLum - bgLum, 0f, 1f);
        float wLight = cov + (pc - cov) * (1f - lightWeight * rel);
        float aDark = 1f - SrgbToLinear(1f - pc);
        float aLight = SrgbToLinear(wLight);
        float polarity = SmoothStep(-0.1f, 0.1f, fgLum - bgLum);
        return aDark + (aLight - aDark) * polarity;
    }

    /// <summary>sRGB-encoded luminance of a target pixel (as the glyph shader reads the background).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float PixelLuminance(uint pixel)
        => (0.2126f * ((pixel >> 16) & 0xFF) + 0.7152f * ((pixel >> 8) & 0xFF) + 0.0722f * (pixel & 0xFF)) * (1f / 255f);

    // ── Sampling ────────────────────────────────────────────────────────

    /// <summary>
    /// Bilinear sample of an sRGB RGBA8 texture (stride <paramref name="stride"/> texels) at
    /// normalized (u, v) with clamp-to-edge, filtering in linear light as GPU samplers do for sRGB
    /// formats. Texture origin (tx, ty), size (w, h) select a sub-rect of the texture (atlas page).
    /// </summary>
    public static (float R, float G, float B, float A) SampleBilinearSrgb(ReadOnlySpan<byte> rgba, int stride,
        int originX, int originY, int pageWidth, int pageHeight, float u, float v)
    {
        // Normalized coordinates are over the whole page; texel centres at +0.5. The coordinate is
        // snapped to the nearest 1/256 of a texel (ComposeShaders.ImageWgsl does the same), so one
        // a rounding error from a texel centre filters as the centre on both backends.
        float x = SnapTexel(u * pageWidth - 0.5f);
        float y = SnapTexel(v * pageHeight - 0.5f);
        float fx = x - MathF.Floor(x);
        float fy = y - MathF.Floor(y);
        int x0 = (int)MathF.Floor(x);
        int y0 = (int)MathF.Floor(y);
        int x1 = Math.Clamp(x0 + 1, 0, pageWidth - 1);
        int y1 = Math.Clamp(y0 + 1, 0, pageHeight - 1);
        x0 = Math.Clamp(x0, 0, pageWidth - 1);
        y0 = Math.Clamp(y0, 0, pageHeight - 1);

        var t00 = Texel(rgba, stride, originX + x0, originY + y0);
        var t10 = Texel(rgba, stride, originX + x1, originY + y0);
        var t01 = Texel(rgba, stride, originX + x0, originY + y1);
        var t11 = Texel(rgba, stride, originX + x1, originY + y1);
        float w00 = (1 - fx) * (1 - fy), w10 = fx * (1 - fy), w01 = (1 - fx) * fy, w11 = fx * fy;
        return (
            t00.R * w00 + t10.R * w10 + t01.R * w01 + t11.R * w11,
            t00.G * w00 + t10.G * w10 + t01.G * w01 + t11.G * w11,
            t00.B * w00 + t10.B * w10 + t01.B * w01 + t11.B * w11,
            t00.A * w00 + t10.A * w10 + t01.A * w01 + t11.A * w11);

        static (float R, float G, float B, float A) Texel(ReadOnlySpan<byte> data, int stride, int tx, int ty)
        {
            int i = (ty * stride + tx) * 4;
            return (Decode[data[i]], Decode[data[i + 1]], Decode[data[i + 2]], data[i + 3] * (1f / 255f));
        }
    }

    // A texel coordinate on the 1/256-texel grid, to the nearest step (ties to even, as WGSL's round).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float SnapTexel(float x) => MathF.Round(x * 256f) * (1f / 256f);

    // Filter weights carry 8 bits of subtexel precision, truncated, as the reference rasterizer
    // (WARP) filters with a sampler; hardware may keep more, which the parity tests calibrate against.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float SubTexel(float f) => MathF.Floor(f * 256f) * (1f / 256f);

    /// <summary>Bilinear sample of the BGRA target copy (linear light, clamp-to-edge), at normalized (u, v).</summary>
    public static (float R, float G, float B) SampleBilinearTarget(ReadOnlySpan<uint> pixels, int width, int height, float u, float v)
    {
        float x = u * width - 0.5f;
        float y = v * height - 0.5f;
        float fx = SubTexel(x - MathF.Floor(x));
        float fy = SubTexel(y - MathF.Floor(y));
        int x0 = (int)MathF.Floor(x);
        int y0 = (int)MathF.Floor(y);
        int x1 = Math.Clamp(x0 + 1, 0, width - 1);
        int y1 = Math.Clamp(y0 + 1, 0, height - 1);
        x0 = Math.Clamp(x0, 0, width - 1);
        y0 = Math.Clamp(y0, 0, height - 1);
        uint p00 = pixels[y0 * width + x0], p10 = pixels[y0 * width + x1];
        uint p01 = pixels[y1 * width + x0], p11 = pixels[y1 * width + x1];
        float w00 = (1 - fx) * (1 - fy), w10 = fx * (1 - fy), w01 = (1 - fx) * fy, w11 = fx * fy;
        return (
            Decode[(p00 >> 16) & 0xFF] * w00 + Decode[(p10 >> 16) & 0xFF] * w10 + Decode[(p01 >> 16) & 0xFF] * w01 + Decode[(p11 >> 16) & 0xFF] * w11,
            Decode[(p00 >> 8) & 0xFF] * w00 + Decode[(p10 >> 8) & 0xFF] * w10 + Decode[(p01 >> 8) & 0xFF] * w01 + Decode[(p11 >> 8) & 0xFF] * w11,
            Decode[p00 & 0xFF] * w00 + Decode[p10 & 0xFF] * w10 + Decode[p01 & 0xFF] * w01 + Decode[p11 & 0xFF] * w11);
    }
}

/// <summary>The mask atlas pages and the frame's clip-mask tiles, as the per-pixel functions read them.</summary>
internal readonly ref struct MaskSource
{
    private readonly IReadOnlyList<byte[]>? pages;

    public MaskSource(IReadOnlyList<byte[]>? pages, ReadOnlySpan<MaskTileEntry> tiles, int stride)
    {
        this.pages = pages;
        Tiles = tiles;
        Stride = stride;
    }

    /// <summary>No masks (tests of analytic shapes).</summary>
    public static MaskSource None => default;

    public ReadOnlySpan<MaskTileEntry> Tiles { get; }

    /// <summary>Row stride of every page, texels (the atlas's current page dimension).</summary>
    public int Stride { get; }

    public ReadOnlySpan<byte> Page(int layer)
        => pages is not null && (uint)layer < (uint)pages.Count ? pages[layer] : ReadOnlySpan<byte>.Empty;
}
