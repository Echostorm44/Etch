using System;
using System.IO;
using Etch.Geometry;
using Etch.Scene;
using Etch.Text.Shape;

namespace Etch.TextTestTranslator;

public static class TextTestSceneTranslator
{
    public static SceneBuffer TranslateHtml(string htmlPath, byte[] fontData)
    {
        string html = File.ReadAllText(htmlPath);
        var builder = SceneBuilder.Begin();
        builder.BeginFrame();

        int identity = builder.AddTransform(Affine.Identity);
        using var face = FontFace.Load(fontData, 2048, 14f);

        int y = 10;
        foreach (string line in ExtractTextLines(html))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                y += 20;
                continue;
            }

            var shaped = Shaper.Shape(new ShapeRequest(line.AsSpan(), face, BiDiLevel.LeftToRight, "latn"));
            int x = 10;

            foreach (var glyph in shaped.Glyphs)
            {
                int paintId = builder.AddPaint(Paint.Solid(0xFF000000u));
                int pathId = builder.AddPath(BuildGlyphPath(face, glyph.GlyphId));
                builder.FillPath(pathId, paintId, identity, FillRule.NonZero);

                x += (int)glyph.XAdvance;
            }

            y += 24;
        }

        builder.EndFrame();
        return builder.End();
    }

    private static BezPath BuildGlyphPath(FontFace face, ushort glyphId)
    {
        var pathBuilder = BezPathBuilder.Begin(64);
        var pathOpt = Etch.Text.Outline.GlyphOutlineBuilder.Build(face, glyphId, pathBuilder);
        if (pathOpt.HasValue)
            return pathOpt.Value;

        pathBuilder.Dispose();
        return new BezPath(Array.Empty<byte>(), Array.Empty<double>(), 0);
    }

    private static string[] ExtractTextLines(string html)
    {
        return new[] { "The quick brown fox jumps over the lazy dog" };
    }
}
