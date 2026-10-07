using Etch.Compose;
using Etch.Geometry;
using Etch.Scene;
using Etch.Text;
using Etch.Text.Shape;

namespace Etch.Bench.Compose;

/// <summary>
/// A representative application frame for the composer benchmarks and the large parity case: a
/// sidebar, a header with a search field, a grid of cards (shadow, rounded fill, border, title,
/// body text, thumbnail, progress bar, pill), icons as paths and a status bar. At 1920×1080 it
/// draws about 3,500 glyphs and 400 shapes — the "HelloCascade-class" frame of the perf budget.
/// </summary>
public static class UiFrameScene
{
    private static readonly ComposeColor Window = Rgb(246, 246, 248);
    private static readonly ComposeColor Sidebar = Rgb(236, 236, 240);
    private static readonly ComposeColor Card = Rgb(255, 255, 255);
    private static readonly ComposeColor Hairline = Rgb(0, 0, 0, 0.1f);
    private static readonly ComposeColor Ink = Rgb(28, 28, 30);
    private static readonly ComposeColor Secondary = Rgb(108, 108, 112);
    private static readonly ComposeColor Accent = Rgb(10, 132, 255);
    private static readonly ComposeColor Track = Rgb(229, 229, 234);
    private static readonly ComposeColor Shadow = Rgb(0, 0, 0, 0.18f);
    private static readonly ComposeColor White = Rgb(255, 255, 255);

    private static readonly Lazy<byte[]> FontBytes = new(LoadFont);
    private static readonly Dictionary<float, FontFace> Faces = new();

    private const string Body = "The quick brown fox jumps over the lazy dog while the composer keeps every pixel honest.";

    /// <summary>
    /// Builds the frame at <paramref name="scale"/> (logical 1920/scale × 1080/scale). With
    /// <paramref name="caret"/> a text caret shows in the search field (a blink toggles it);
    /// <paramref name="titleVariant"/> changes the first card's title (an edit).
    /// </summary>
    public static DrawRecording Build(int width, int height, float scale, bool caret = false, int titleVariant = 0)
    {
        var rec = new DrawRecording();
        var t = Affine.Scale(scale);
        float w = width / scale;
        float h = height / scale;
        rec.SetTransform(t);
        rec.FillRect(0, 0, w, h, ComposePaint.Solid(Window));

        // Sidebar with navigation rows.
        rec.FillRect(0, 0, 260, h, ComposePaint.Solid(Sidebar));
        rec.FillRect(259, 0, 1, h, ComposePaint.Solid(Hairline));
        for (int i = 0; i < 18; i++)
        {
            float y = 72 + i * 36;
            if (i == 3)
            {
                rec.FillRoundedRect(12, y, 236, 30, 8, ComposePaint.Solid(Accent.WithOpacity(0.14f)));
            }
            rec.FillPath(Icon(28, y + 7, 16), FillRule.NonZero, ComposePaint.Solid(i == 3 ? Accent : Secondary));
            rec.Glyphs(Run($"Navigation item {i + 1}", 54 * scale, (y + 20) * scale, i == 3 ? Accent : Ink, scale, 14));
        }

        // Header: title, search field, buttons.
        rec.FillRect(260, 0, w - 260, 56, ComposePaint.Solid(Card));
        rec.FillRect(260, 55, w - 260, 1, ComposePaint.Solid(Hairline));
        rec.Glyphs(Run("Dashboard", 284 * scale, 36 * scale, Ink, scale, 20));
        rec.FillRoundedRect(w - 620, 12, 320, 32, 8, ComposePaint.Solid(Window));
        rec.Border(w - 620, 12, 320, 32, 8, 1, Hairline);
        rec.Glyphs(Run("Type to filter entries…", (w - 596) * scale, 33 * scale, Secondary, scale, 14));
        if (caret)
        {
            rec.FillRect(w - 600, 19, 1.5f, 18, ComposePaint.Solid(Accent));
        }
        for (int i = 0; i < 3; i++)
        {
            float x = w - 280 + i * 92;
            rec.FillRoundedRect(x, 12, 84, 32, 8, ComposePaint.Solid(i == 2 ? Accent : Window));
            rec.Glyphs(Run(i == 2 ? "Publish" : i == 1 ? "Share" : "Export", (x + 16) * scale, 33 * scale, i == 2 ? White : Ink, scale, 14));
        }

        // Card grid.
        float gridX = 284;
        float gridY = 80;
        float cardW = (w - gridX - 24 - 2 * 20) / 3f;
        float cardH = (h - gridY - 48 - 3 * 20) / 4f;
        for (int row = 0; row < 4; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                float x = gridX + col * (cardW + 20);
                float y = gridY + row * (cardH + 20);
                CardAt(rec, x, y, cardW, cardH, row * 3 + col, scale, row == 0 && col == 0 ? titleVariant : 0);
            }
        }

        // Status bar.
        rec.FillRect(260, h - 28, w - 260, 28, ComposePaint.Solid(Sidebar));
        rec.Glyphs(Run("Ready · 12 items · last sync 2 minutes ago", 284 * scale, (h - 9) * scale, Secondary, scale, 12));
        return rec;
    }

    private static void CardAt(DrawRecording rec, float x, float y, float w, float h, int index, float scale, int titleVariant)
    {
        rec.Shadow(x, y + 4, w, h, 12, 10, Shadow);
        rec.FillRoundedRect(x, y, w, h, 12, ComposePaint.Solid(Card));
        rec.Border(x, y, w, h, 12, 1, Hairline);
        rec.PushClipRoundedRect(x, y, w, h, 12);
        rec.Image(1, Thumbnail.Value, x + 16, y + 16, 56, 56, 1f);
        rec.Glyphs(Run(titleVariant == 0 ? $"Card {index + 1}: weekly summary" : $"Card {index + 1}: edited {titleVariant}", (x + 88) * scale, (y + 34) * scale, Ink, scale, 16));
        rec.Glyphs(Run("Updated today", (x + 88) * scale, (y + 56) * scale, Secondary, scale, 12));
        float lineY = y + 98;
        for (int line = 0; line < 4 && lineY < y + h - 40; line++)
        {
            rec.Glyphs(Run(Body, (x + 16) * scale, lineY * scale, Ink, scale, 13));
            lineY += 20;
        }
        float barY = y + h - 28;
        rec.FillRoundedRect(x + 16, barY, w - 120, 8, 4, ComposePaint.Solid(Track));
        rec.FillRoundedRect(x + 16, barY, (w - 120) * (0.2f + 0.06f * index), 8, 4, ComposePaint.Solid(Accent));
        rec.FillRoundedRect(x + w - 92, barY - 8, 76, 24, 12, ComposePaint.Solid(Accent.WithOpacity(0.12f)));
        rec.Glyphs(Run("Active", (x + w - 76) * scale, (barY + 9) * scale, Accent, scale, 12));
        rec.PopClip();
    }

    private static readonly Lazy<ComposeImage> Thumbnail = new(() =>
    {
        const int size = 64;
        byte[] rgba = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = (y * size + x) * 4;
                rgba[i] = (byte)(40 + x * 3);
                rgba[i + 1] = (byte)(90 + y * 2);
                rgba[i + 2] = (byte)(200 - x);
                rgba[i + 3] = 255;
            }
        }
        return new ComposeImage(rgba, size, size);
    });

    // A rounded "app" glyph: two overlapping squircle-ish contours, exercising path masks.
    private static BezPath Icon(float x, float y, float size)
    {
        var b = BezPathBuilder.Begin(16);
        float r = size * 0.3f;
        b.MoveTo(new Point(x + r, y));
        b.LineTo(new Point(x + size - r, y));
        b.QuadTo(new Point(x + size, y), new Point(x + size, y + r));
        b.LineTo(new Point(x + size, y + size - r));
        b.QuadTo(new Point(x + size, y + size), new Point(x + size - r, y + size));
        b.LineTo(new Point(x + r, y + size));
        b.QuadTo(new Point(x, y + size), new Point(x, y + size - r));
        b.LineTo(new Point(x, y + r));
        b.QuadTo(new Point(x, y), new Point(x + r, y));
        b.Close();
        return b.Build();
    }

    /// <summary>Shapes <paramref name="text"/> into a glyph run at device position (x, baseline).</summary>
    public static GlyphRunData Run(string text, float x, float baseline, ComposeColor color, float scale, float size)
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

    // Roboto from Google Fonts (cached on disk), as Etch.Text.Tests uses; the system UI font offline.
    private static byte[] LoadFont() => Etch.Testing.TestFontCache.RobotoRegular();

    private static ComposeColor Rgb(byte r, byte g, byte b, float a = 1f)
        => new(Srgb.Decode(r / 255f), Srgb.Decode(g / 255f), Srgb.Decode(b / 255f), a);
}
