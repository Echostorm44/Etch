using Etch.Compose.Coverage;
using Etch.Geometry;
using Etch.Scene;
using Etch.Text;
using Etch.Text.Shape;

namespace Etch.Compose.Tests;

/// <summary>
/// Recordings exercising every primitive, used to compare the GPU and CPU composers. Each scene
/// draws at a device scale so fractional geometry and AA edges are covered.
/// </summary>
internal static class ParityScenes
{
    public const int Width = 320;
    public const int Height = 240;

    private static readonly ComposeColor White = Rgb(255, 255, 255);
    private static readonly ComposeColor Paper = Rgb(244, 241, 234);
    private static readonly ComposeColor Ink = Rgb(28, 28, 30);
    private static readonly ComposeColor Blue = Rgb(10, 132, 255);
    private static readonly ComposeColor Orange = Rgb(255, 159, 10);
    private static readonly ComposeColor Green = Rgb(48, 209, 88);
    private static readonly ComposeColor Red = Rgb(255, 55, 95);
    private static readonly ComposeColor Purple = Rgb(191, 90, 242);
    private static readonly ComposeColor Yellow = Rgb(255, 214, 10);
    private static readonly ComposeColor Slate = Rgb(44, 44, 46);

    // Roboto through Etch.TestFonts.TestFontCache (downloaded once, cached on disk; the system UI font
    // offline). Any font works: parity compares two renders of the same glyphs.
    private static readonly Lazy<byte[]> FontBytes = new(LoadFont);

    private static readonly Dictionary<float, FontFace> Faces = new();

    public static readonly string[] Names = ["shapes", "strokes", "paths", "gradients", "clips", "images", "text", "emoji", "blur", "layers", "composite"];

    public static ComposeColor Rgb(byte r, byte g, byte b, float a = 1f)
        => new(Srgb.Decode(r / 255f), Srgb.Decode(g / 255f), Srgb.Decode(b / 255f), a);

    public static DrawRecording Build(string name, float scale)
    {
        var rec = new DrawRecording();
        var t = Affine.Scale(scale);
        rec.SetTransform(Affine.Identity);
        rec.FillRect(0, 0, Width * scale, Height * scale, ComposePaint.Solid(name == "text" ? White : Paper));
        rec.SetTransform(t);
        switch (name)
        {
            case "shapes":
                Shapes(rec, t);
                break;
            case "strokes":
                Strokes(rec, t);
                break;
            case "paths":
                Paths(rec, t);
                break;
            case "gradients":
                Gradients(rec, t);
                break;
            case "clips":
                Clips(rec, t, scale);
                break;
            case "composite":
                Composite(rec, t, scale);
                break;
            case "emoji":
                Emoji(rec, scale);
                break;
            case "images":
                Images(rec, t);
                break;
            case "text":
                Text(rec, scale);
                break;
            case "blur":
                Blur(rec, t, scale);
                break;
            case "layers":
                Layers(rec, t, scale);
                break;
        }
        return rec;
    }

    private static void Shapes(DrawRecording rec, Affine t)
    {
        rec.FillRect(5, 5, 30, 20, ComposePaint.Solid(Blue));
        rec.FillRect(40.3f, 5.6f, 30.4f, 20.7f, ComposePaint.Solid(Orange));
        rec.FillRoundedRect(75, 5, 40, 22, 6, ComposePaint.Solid(Green));
        rec.FillRoundedRect(120.4f, 5.3f, 40.2f, 22.1f, 11, ComposePaint.Solid(Red));
        rec.Border(165, 5, 40, 22, 0, 2, Ink);
        rec.Border(210, 5, 40, 22, 7, 2.5f, Purple);
        rec.FillCircle(20, 50, 14, ComposePaint.Solid(Blue));
        rec.FillCircle(55.5f, 50.3f, 9.7f, ComposePaint.Solid(Orange));
        rec.StrokeCircle(90, 50, 12, 3, Red);
        rec.StrokeLine(115, 40, 165, 40, StrokeParameters.Solid(5, StrokeCap.Butt), Ink);
        rec.StrokeLine(115, 52, 165, 52, StrokeParameters.Solid(5, StrokeCap.Round), Ink);
        rec.StrokeLine(115, 64, 165, 64, StrokeParameters.Solid(5, StrokeCap.Square), Ink);
        rec.StrokeLine(170, 38, 210, 66, StrokeParameters.Solid(3, StrokeCap.Round), Blue);
        rec.FillSector(240, 50, 22, 0, -1.5f, 2.2f, Green);
        rec.FillSector(240, 50, 22, 12, 0.9f, 2.1f, Red);
        rec.StrokeArc(285, 50, 16, 0.3f, 4.2f, StrokeParameters.Solid(5, StrokeCap.Round), Purple);
        rec.Shadow(10, 85, 60, 40, 8, 6, Rgb(0, 0, 0, 0.5f));
        rec.FillRoundedRect(10, 85, 60, 40, 8, ComposePaint.Solid(White));
        rec.Shadow(90, 85, 50, 40, 0, 3, Rgb(20, 40, 160, 0.6f));
        rec.FillRect(150, 85, 60, 40, ComposePaint.Solid(Rgb(10, 132, 255, 0.5f)));
        rec.FillRoundedRect(175, 100, 60, 40, 10, ComposePaint.Solid(Rgb(255, 55, 95, 0.6f)));
        rec.FillCircle(200, 120, 18, ComposePaint.Solid(Rgb(255, 214, 10, 0.5f)));
        rec.SetTransform(t * Affine.Translate(270, 120) * Affine.Rotate(0.35));
        rec.FillRoundedRect(-30, -15, 60, 30, 8, ComposePaint.Solid(Purple));
        rec.FillRect(-12, -6, 24, 12, ComposePaint.Solid(White));
        rec.Shadow(-20, 25, 40, 12, 4, 3, Rgb(0, 0, 0, 0.4f));
        rec.SetTransform(t);
    }

    private static void Strokes(DrawRecording rec, Affine t)
    {
        StrokeJoin[] joins = [StrokeJoin.Miter, StrokeJoin.Round, StrokeJoin.Bevel];
        for (int i = 0; i < 3; i++)
        {
            float x = 10 + i * 100;
            rec.StrokePath(Polyline((x, 60), (x + 20, 10), (x + 40, 60), (x + 60, 20), (x + 80, 35)),
                StrokeParameters.Solid(7, StrokeCap.Butt, joins[i]), ComposePaint.Solid(Rgb(191, 90, 242, 0.7f)));
            rec.StrokePath(Polyline((x + 5, 90), (x + 25, 110), (x + 70, 75)),
                StrokeParameters.Solid(9, StrokeCap.Round, joins[i]), ComposePaint.Solid(Rgb(10, 132, 255, 0.55f)));
        }
        var wave = BezPathBuilder.Begin(8);
        wave.MoveTo(new Point(10, 150));
        wave.CubicTo(new Point(60, 110), new Point(110, 190), new Point(160, 150));
        wave.QuadTo(new Point(200, 120), new Point(240, 160));
        rec.StrokePath(wave.Build(), StrokeParameters.Solid(3, StrokeCap.Round, StrokeJoin.Round), ComposePaint.Solid(Ink));
        var box = BezPathBuilder.Begin(8);
        box.MoveTo(new Point(20, 180));
        box.LineTo(new Point(300, 180));
        box.LineTo(new Point(300, 225));
        box.LineTo(new Point(20, 225));
        box.Close();
        rec.StrokePath(box.Build(), new StrokeParameters(2, StrokeCap.Butt, StrokeJoin.Miter, 4, 7, 4, 1.5f), ComposePaint.Solid(Ink));
        rec.StrokeArc(270, 120, 25, -2.5f, 3.6f, StrokeParameters.Solid(4, StrokeCap.Square), Orange);
    }

    private static void Paths(DrawRecording rec, Affine t)
    {
        rec.FillPath(Star(50, 50, 40, 17, 5), FillRule.NonZero, ComposePaint.Solid(Orange));
        rec.FillPath(Star(140, 50, 40, 28, 7), FillRule.EvenOdd, ComposePaint.Solid(Blue));
        var chevron = Polygon((200, 20), (230, 50), (200, 80), (190, 70), (210, 50), (190, 30));
        rec.FillPath(chevron, FillRule.NonZero, ComposePaint.Solid(Ink));
        var blob = BezPathBuilder.Begin(8);
        blob.MoveTo(new Point(250, 60));
        blob.CubicTo(new Point(250, 10), new Point(310, 10), new Point(310, 60));
        blob.CubicTo(new Point(310, 100), new Point(270, 80), new Point(265, 100));
        blob.QuadTo(new Point(240, 110), new Point(250, 60));
        blob.Close();
        rec.FillPath(blob.Build(), FillRule.NonZero, ComposePaint.Solid(Green));
        rec.SetTransform(t * new Affine(1.8, 0.4, 0, 0.8, 70, 160));
        rec.FillCircle(0, 0, 25, ComposePaint.Solid(Red));
        rec.FillRect(30, -20, 40, 30, ComposePaint.Solid(Purple));
        rec.SetTransform(t);
        rec.FillPath(Polygon((180, 120), (300, 200), (300, 120), (180, 200)), FillRule.NonZero, ComposePaint.Solid(Yellow));
    }

    private static void Gradients(DrawRecording rec, Affine t)
    {
        var rainbow = new[]
        {
            new ComposeGradientStop(0f, Red),
            new ComposeGradientStop(0.3f, Yellow),
            new ComposeGradientStop(0.55f, Green),
            new ComposeGradientStop(0.8f, Blue),
            new ComposeGradientStop(1f, Purple),
        };
        rec.FillRect(10, 10, 140, 40, ComposePaint.Linear(10, 10, 150, 10, rainbow));
        rec.FillRoundedRect(160, 10, 150, 40, 10, ComposePaint.Linear(160, 10, 220, 60, rainbow));
        rec.FillRect(10, 60, 140, 70, ComposePaint.Radial(80, 95, 60, [new(0f, White), new(0.5f, Blue), new(1f, Slate)]));
        rec.FillRoundedRect(160, 60, 70, 70, 35, ComposePaint.Sweep(195, 95, -1.57f, rainbow));
        rec.FillRect(240, 60, 70, 70, ComposePaint.Linear(240, 60, 310, 60, [new(0f, Red), new(1f, Red.WithOpacity(0f))]));
        rec.FillPath(Star(80, 185, 45, 20, 5), FillRule.NonZero, ComposePaint.Radial(80, 185, 45, [new(0f, Yellow), new(1f, Red)]));
        rec.SetTransform(t * Affine.Translate(230, 185) * Affine.Rotate(-0.4));
        rec.FillRoundedRect(-60, -25, 120, 50, 12, ComposePaint.Linear(-60, 0, 60, 0, rainbow));
        rec.SetTransform(t);
    }

    private static void Clips(DrawRecording rec, Affine t, float scale)
    {
        rec.PushClipRect(10, 10, 90, 60);
        rec.FillRoundedRect(25, 25, 110, 80, 18, ComposePaint.Solid(Blue));
        rec.Border(-10, 45, 60, 40, 10, 3, Ink);
        rec.PopClip();
        rec.PushClipRoundedRect(110, 10, 90, 70, 20);
        rec.FillRect(100, 0, 120, 100, ComposePaint.Solid(Orange));
        rec.Image(1, CheckerImage(), 110, 30, 90, 30, 1f);
        rec.PopClip();
        rec.PushClipPath(Circle(255, 45, 38), FillRule.NonZero);
        for (int i = 0; i < 8; i++)
        {
            rec.FillRect(215 + i * 10, 0, 5, 100, ComposePaint.Solid(i % 2 == 0 ? Red : Yellow));
        }
        rec.PopClip();
        rec.PushClipRect(10, 90, 150, 70);
        rec.PushClipRoundedRect(30, 100, 150, 50, 22);
        rec.PushClipRoundedRect(20, 95, 100, 70, 14);
        rec.FillRect(0, 80, 200, 100, ComposePaint.Solid(Purple));
        rec.FillCircle(50, 125, 30, ComposePaint.Solid(Green));
        rec.PopClip();
        rec.PopClip();
        rec.PopClip();
        rec.SetTransform(t * Affine.Translate(240, 160) * Affine.Rotate(0.5));
        rec.PushClipRect(-40, -25, 80, 50);
        rec.FillRect(-80, -80, 160, 160, ComposePaint.Solid(Orange));
        rec.FillCircle(0, 0, 30, ComposePaint.Solid(Blue));
        rec.PopClip();
        rec.SetTransform(t);
        rec.PushClipRect(10, 175, 140, 35);
        rec.Glyphs(Run("Clipped text runs past", 15, 205 * scale, Ink, scale));
        rec.PopClip();
    }

    private static void Images(DrawRecording rec, Affine t)
    {
        var checker = CheckerImage();
        var ramp = RampImage();
        rec.Image(1, checker, 10, 10, 64, 64, 1f);
        rec.Image(1, checker, 80.3f, 10.6f, 37.4f, 37.2f, 1f);
        rec.FillRect(130, 10, 100, 30, ComposePaint.Solid(Ink));
        rec.Image(2, ramp, 130, 10, 100, 60, 1f);
        rec.Image(1, checker, 240, 10, 60, 60, 0.4f);
        rec.SetTransform(t * Affine.Translate(90, 150) * Affine.Rotate(-0.3));
        rec.Image(1, checker, -50, -35, 100, 70, 1f);
        rec.SetTransform(t * Affine.Translate(230, 160) * Affine.Scale(1.3, 0.9));
        rec.Image(2, ramp, -50, -30, 100, 60, 0.8f);
        rec.SetTransform(t);
    }

    private static void Text(DrawRecording rec, float scale)
    {
        float y = 14 * scale;
        foreach (float size in new[] { 9f, 11f, 13f, 16f, 22f })
        {
            rec.Glyphs(Run($"Sphinx of black quartz {size}", 6 * scale, y, Ink, scale, size));
            y += (size * 1.3f + 3) * scale;
        }
        rec.SetTransform(Affine.Scale(scale));
        rec.FillRect(0, 120, Width, 40, ComposePaint.Solid(Slate));
        rec.FillRect(0, 160, Width, 40, ComposePaint.Solid(Blue));
        rec.FillRect(0, 200, Width, 40, ComposePaint.Solid(Yellow));
        rec.Glyphs(Run("Light on dark 14px", 6 * scale, 145 * scale, White, scale, 14));
        rec.Glyphs(Run("Gray 50% Aa", 170 * scale, 145 * scale, Rgb(142, 142, 147), scale, 14));
        rec.Glyphs(Run("On blue, translucent", 6 * scale, 185 * scale, White.WithOpacity(0.6f), scale, 14));
        rec.Glyphs(Run("Ink 40%", 200 * scale, 185 * scale, Ink.WithOpacity(0.4f), scale, 16));
        rec.Glyphs(Run("Dark on yellow", 6 * scale, 225 * scale, Ink, scale, 18));
        rec.Glyphs(Run("Overlap", 180 * scale, 225 * scale, Red, scale, 20));
        rec.Glyphs(Run("Overlap", 183 * scale, 227 * scale, Blue.WithOpacity(0.7f), scale, 20));
    }

    // Colour (COLR) glyphs among text, on light and dark grounds, translucent, clipped, overlapping.
    private static void Emoji(DrawRecording rec, float scale)
    {
        string emoji = "\U0001F600\U0001F680\U0001F308\u2764\uFE0F\U0001F44D\U0001F3FD\U0001F984";
        float y = 30 * scale;
        foreach (float size in new[] { 12f, 16f, 24f, 40f })
        {
            rec.Glyphs(Run("Mixed", 6 * scale, y, Ink, scale, 14));
            rec.Glyphs(EmojiRun(emoji, 60 * scale, y, 1f, scale, size));
            y += (size * 1.3f + 4) * scale;
        }
        rec.SetTransform(Affine.Scale(scale));
        rec.FillRect(0, 170, Width, 70, ComposePaint.Solid(Slate));
        rec.Glyphs(EmojiRun(emoji, 6 * scale, 205 * scale, 0.5f, scale, 28));
        rec.PushClipRoundedRect(180, 175, 120, 50, 18);
        rec.Glyphs(EmojiRun(emoji + emoji, 150 * scale, 215 * scale, 1f, scale, 32));
        rec.PopClip();
        rec.Glyphs(EmojiRun("\U0001F600\U0001F600", 240 * scale, 60 * scale, 1f, scale, 48));
        rec.Glyphs(EmojiRun("\U0001F680", 262 * scale, 70 * scale, 0.8f, scale, 48));
    }

    private static readonly Lazy<byte[]> EmojiBytes = new(Etch.TestFonts.TestFontCache.ColorEmoji);
    private static readonly Dictionary<float, FontFace> EmojiFaces = new();

    public static GlyphRunData EmojiRun(string text, float x, float baseline, float opacity, float scale, float size)
    {
        float rasterSize = size * scale;
        FontFace face;
        lock (EmojiFaces)
        {
            if (!EmojiFaces.TryGetValue(rasterSize, out face!))
            {
                face = FontFace.Load(EmojiBytes.Value, UnitsPerEm(EmojiBytes.Value), rasterSize);
                EmojiFaces[rasterSize] = face;
            }
        }
        var shaped = Shaper.Shape(new ShapeRequest(text, face, BiDiLevel.LeftToRight, "Zyyy"));
        var ids = new ushort[shaped.GlyphCount];
        var positions = new float[shaped.GlyphCount * 2];
        float pen = x;
        for (int i = 0; i < shaped.GlyphCount; i++)
        {
            var g = shaped.Glyphs[i];
            ids[i] = g.GlyphId;
            positions[i * 2] = pen + g.XOffset;
            positions[i * 2 + 1] = baseline - g.YOffset;
            pen += g.XAdvance;
        }
        return new GlyphRunData(face, 2, ids, positions, rasterSize, new ComposeColor(1f, 1f, 1f, opacity), null);
    }

    // unitsPerEm from the font's 'head' table.
    private static int UnitsPerEm(byte[] font)
    {
        int tables = font[4] << 8 | font[5];
        for (int i = 0; i < tables; i++)
        {
            int record = 12 + i * 16;
            if (font[record] == 'h' && font[record + 1] == 'e' && font[record + 2] == 'a' && font[record + 3] == 'd')
            {
                int offset = font[record + 8] << 24 | font[record + 9] << 16 | font[record + 10] << 8 | font[record + 11];
                return font[offset + 18] << 8 | font[offset + 19];
            }
        }
        return 2048;
    }

    // Images, blurs and shadows inside translucent and nested layers, rotated images in a layer,
    // a blur clipped by a rounded clip inside a layer, a large blur radius.
    private static void Composite(DrawRecording rec, Affine t, float scale)
    {
        var checker = CheckerImage();
        var ramp = RampImage();
        ComposeColor[] stripes = [Red, Orange, Yellow, Green, Blue, Purple];
        for (int i = 0; i < 27; i++)
        {
            rec.FillRect(i * 12, 0, 6, Height, ComposePaint.Solid(stripes[i % stripes.Length]));
        }

        var inner = new DrawRecording();
        inner.SetTransform(t);
        inner.Image(1, checker, 10, 10, 50, 50, 0.7f);
        inner.SetTransform(t * Affine.Translate(100, 40) * Affine.Rotate(0.35));
        inner.Image(2, ramp, -30, -20, 60, 40, 1f);
        inner.SetTransform(t);
        inner.Shadow(20, 80, 90, 40, 10, 6, Rgb(0, 0, 0, 0.4f));
        inner.FillRoundedRect(20, 80, 90, 40, 10, ComposePaint.Solid(Paper));
        inner.BackdropBlur(30 * scale, 85 * scale, 70 * scale, 30 * scale, 6 * scale, 3 * scale, White.WithOpacity(0.2f));

        var outer = new DrawRecording();
        outer.SetTransform(t);
        outer.FillRoundedRect(5, 5, 150, 140, 14, ComposePaint.Solid(White.WithOpacity(0.5f)));
        outer.Layer(inner, 10 * scale, 5 * scale, 0.8f);
        outer.PushClipRoundedRect(20, 150, 130, 70, 20);
        outer.BackdropBlur(10 * scale, 140 * scale, 150 * scale, 90 * scale, 0, 12 * scale, Blue.WithOpacity(0.15f));
        outer.PopClip();

        rec.Layer(outer, 0, 0, 1f);
        rec.Layer(outer, 160 * scale, 10 * scale, 0.6f);
        rec.Glyphs(Run("Composite", 175 * scale, 230 * scale, Ink, scale, 16));
    }

    private static void Blur(DrawRecording rec, Affine t, float scale)
    {
        ComposeColor[] stripes = [Red, Orange, Yellow, Green, Blue, Purple];
        for (int i = 0; i < 27; i++)
        {
            rec.FillRect(i * 12, 0, 6, Height, ComposePaint.Solid(stripes[i % stripes.Length]));
        }
        rec.Glyphs(Run("BACKDROP", 20 * scale, 60 * scale, Ink, scale, 36));
        rec.BackdropBlur(15 * scale, 15 * scale, 140 * scale, 80 * scale, 16 * scale, 6 * scale, White.WithOpacity(0.35f));
        rec.BackdropBlur(170 * scale, 20 * scale, 130 * scale, 70 * scale, 0, 2 * scale, ComposeColor.Transparent);
        rec.PushClipRect(20, 120, 120, 60);
        rec.BackdropBlur(10 * scale, 110 * scale, 160 * scale, 90 * scale, 10 * scale, 4 * scale, Blue.WithOpacity(0.25f));
        rec.PopClip();
        rec.Glyphs(Run("Frosted", 30 * scale, 80 * scale, Ink, scale, 18));
    }

    private static void Layers(DrawRecording rec, Affine t, float scale)
    {
        var layer = new DrawRecording();
        layer.SetTransform(t);
        for (int i = 0; i < 6; i++)
        {
            float y = 10 + i * 40;
            layer.Shadow(10, y + 2, 130, 32, 8, 3, Rgb(0, 0, 0, 0.3f));
            layer.FillRoundedRect(10, y, 130, 32, 8, ComposePaint.Solid(i % 2 == 0 ? White : Paper));
            layer.Glyphs(Run($"Row {i}", 20 * scale, (y + 21) * scale, Ink, scale, 13));
            layer.PushClipRect(100, y + 4, 30, 24);
            layer.FillCircle(115, y + 16, 14, ComposePaint.Solid(i % 3 == 0 ? Red : Blue));
            layer.PopClip();
        }
        rec.PushClipRect(10, 10, 150, 200);
        rec.Layer(layer, 0, -47.5f * scale, 1f);
        rec.PopClip();
        rec.PushClipRoundedRect(165, 10, 150, 200, 16);
        rec.Layer(layer, 157 * scale, -95 * scale, 0.6f);
        rec.PopClip();
        rec.FillRoundedRect(120, 150, 90, 50, 10, ComposePaint.Solid(Orange));
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static byte[] LoadFont() => Etch.TestFonts.TestFontCache.RobotoOrSystemUiFont();

    /// <summary>
    /// Clips and fills far larger than an atlas page: a full-width rounded clip, a nested rounded
    /// clip pair and a large star path clip (tiled clip masks), a fine stripe path clip whose
    /// tiles are all partial (forcing more than one atlas page), and a large path fill.
    /// </summary>
    public static DrawRecording BuildLarge(int width, int height)
    {
        var rec = new DrawRecording();
        rec.SetTransform(Affine.Identity);
        rec.FillRect(0, 0, width, height, ComposePaint.Solid(Paper));

        rec.PushClipRoundedRect(20.5f, 20.25f, width - 41, height * 0.3f, 60);
        rec.FillRect(0, 0, width, height, ComposePaint.Linear(0, 0, width, 0, [new(0f, Blue), new(1f, Purple)]));
        rec.PopClip();

        rec.PushClipRoundedRect(40, height * 0.35f, width * 0.6f, height * 0.3f, 90);
        rec.PushClipRoundedRect(width * 0.2f, height * 0.33f, width * 0.6f, height * 0.25f, 40);
        rec.FillRect(0, 0, width, height, ComposePaint.Solid(Green));
        rec.PopClip();
        rec.PopClip();

        rec.PushClipPath(Star(width * 0.8, height * 0.55, height * 0.4, height * 0.18, 7), FillRule.NonZero);
        rec.FillRect(0, 0, width, height, ComposePaint.Radial(width * 0.8f, height * 0.55f, height * 0.4f, [new(0f, Yellow), new(1f, Red)]));
        rec.PopClip();

        var stripes = BezPathBuilder.Begin(4 * 400 + 8);
        for (int i = 0; i < 400; i++)
        {
            double x = 10 + i * (width - 20) / 400.0;
            stripes.MoveTo(new Point(x, height * 0.7));
            stripes.LineTo(new Point(x + 1.3, height * 0.7));
            stripes.LineTo(new Point(x + 1.3 + height * 0.05, height * 0.98));
            stripes.LineTo(new Point(x + height * 0.05, height * 0.98));
            stripes.Close();
        }
        rec.PushClipPath(stripes.Build(), FillRule.NonZero);
        rec.FillRect(0, 0, width, height, ComposePaint.Solid(Ink));
        rec.PopClip();

        rec.FillPath(Circle(width * 0.35, height * 0.5, height * 0.22), FillRule.NonZero, ComposePaint.Solid(Orange.WithOpacity(0.7f)));
        return rec;
    }

    private static FontFace Face(float rasterSize)
    {
        lock (Faces)
        {
            if (!Faces.TryGetValue(rasterSize, out var face))
            {
                face = FontFace.Load(FontBytes.Value, 2048, rasterSize);
                face.Hinting = FontHinting.Slight;
                Faces[rasterSize] = face;
            }
            return face;
        }
    }

    public static GlyphRunData Run(string text, float x, float baseline, ComposeColor color, float scale, float size = 14f)
    {
        float rasterSize = size * scale;
        var face = Face(rasterSize);
        var shaped = Shaper.Shape(new ShapeRequest(text, face, BiDiLevel.LeftToRight, "Latn"));
        var ids = new ushort[shaped.GlyphCount];
        var positions = new float[shaped.GlyphCount * 2];
        float pen = x;
        for (int i = 0; i < shaped.GlyphCount; i++)
        {
            var g = shaped.Glyphs[i];
            ids[i] = g.GlyphId;
            positions[i * 2] = pen + g.XOffset;
            positions[i * 2 + 1] = baseline - g.YOffset;
            pen += g.XAdvance;
        }
        return new GlyphRunData(face, 1, ids, positions, rasterSize, color, null);
    }

    private static ComposeImage CheckerImage()
    {
        const int size = 8;
        byte[] rgba = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = (y * size + x) * 4;
                bool dark = ((x + y) & 1) == 0;
                rgba[i] = dark ? (byte)30 : (byte)240;
                rgba[i + 1] = dark ? (byte)60 : (byte)200;
                rgba[i + 2] = dark ? (byte)140 : (byte)40;
                rgba[i + 3] = 255;
            }
        }
        return new ComposeImage(rgba, size, size);
    }

    private static ComposeImage RampImage()
    {
        const int width = 32;
        const int height = 16;
        byte[] rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                rgba[i] = 255;
                rgba[i + 1] = (byte)(y * 16);
                rgba[i + 2] = 0;
                rgba[i + 3] = (byte)(x * 255 / (width - 1));
            }
        }
        return new ComposeImage(rgba, width, height);
    }

    private static BezPath Polyline(params (double X, double Y)[] points)
    {
        var b = BezPathBuilder.Begin(points.Length + 1);
        b.MoveTo(new Point(points[0].X, points[0].Y));
        for (int i = 1; i < points.Length; i++)
        {
            b.LineTo(new Point(points[i].X, points[i].Y));
        }
        return b.Build();
    }

    private static BezPath Polygon(params (double X, double Y)[] points)
    {
        var b = BezPathBuilder.Begin(points.Length + 2);
        b.MoveTo(new Point(points[0].X, points[0].Y));
        for (int i = 1; i < points.Length; i++)
        {
            b.LineTo(new Point(points[i].X, points[i].Y));
        }
        b.Close();
        return b.Build();
    }

    private static BezPath Star(double cx, double cy, double outer, double inner, int points)
    {
        var b = BezPathBuilder.Begin(points * 2 + 2);
        for (int i = 0; i < points * 2; i++)
        {
            double r = i % 2 == 0 ? outer : inner;
            double a = -Math.PI / 2 + i * Math.PI / points;
            var p = new Point(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
            if (i == 0)
            {
                b.MoveTo(p);
            }
            else
            {
                b.LineTo(p);
            }
        }
        b.Close();
        return b.Build();
    }

    private static BezPath Circle(double cx, double cy, double r)
    {
        const double k = 0.5522847498;
        var b = BezPathBuilder.Begin(8);
        b.MoveTo(new Point(cx + r, cy));
        b.CubicTo(new Point(cx + r, cy + k * r), new Point(cx + k * r, cy + r), new Point(cx, cy + r));
        b.CubicTo(new Point(cx - k * r, cy + r), new Point(cx - r, cy + k * r), new Point(cx - r, cy));
        b.CubicTo(new Point(cx - r, cy - k * r), new Point(cx - k * r, cy - r), new Point(cx, cy - r));
        b.CubicTo(new Point(cx + k * r, cy - r), new Point(cx + r, cy - k * r), new Point(cx + r, cy));
        b.Close();
        return b.Build();
    }
}
