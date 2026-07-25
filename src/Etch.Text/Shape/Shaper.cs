using System;
using Etch.Text.HarfBuzz;

namespace Etch.Text.Shape;

public enum BiDiLevel
{
    Neutral = 0,
    LeftToRight = 1,
    RightToLeft = 2
}

public readonly ref struct ShapeRequest
{
    public ReadOnlySpan<char> Text { get; }
    public FontFace Face { get; }
    public BiDiLevel BiDiLevel { get; }
    public string ScriptTag { get; }

    public ShapeRequest(ReadOnlySpan<char> text, FontFace face, BiDiLevel biDiLevel, string scriptTag)
    {
        Text = text;
        Face = face;
        BiDiLevel = biDiLevel;
        ScriptTag = scriptTag;
    }
}

public static class Shaper
{
    public static ShapedRun Shape(ShapeRequest request)
    {
        var text = request.Text;
        int textLength = text.Length;
        if (textLength == 0)
        {
            return new ShapedRun(Array.Empty<GlyphInfo>(), 0f, request.ScriptTag);
        }

        var font = request.Face.Handle;
        var buffer = request.Face.GetBuffer();

        buffer.ClearContents();
        buffer.Direction = request.BiDiLevel switch
        {
            BiDiLevel.RightToLeft => Direction.RightToLeft,
            _ => Direction.LeftToRight
        };

        if (request.ScriptTag == "Arab")
        {
            buffer.Script = Script.Arabic;
            buffer.Language = new Language("ar");
        }

        buffer.AddUtf16(text);

        Feature[]? features = null;
        if (request.ScriptTag == "Arab")
        {
            features =
            [
                new Feature(new Tag('i', 'n', 'i', 't'), 1, 0, uint.MaxValue),
                new Feature(new Tag('m', 'e', 'd', 'i'), 1, 0, uint.MaxValue),
                new Feature(new Tag('f', 'i', 'n', 'a'), 1, 0, uint.MaxValue),
                new Feature(new Tag('c', 'c', 'm', 'p'), 1, 0, uint.MaxValue),
                new Feature(new Tag('c', 'a', 'l', 't'), 1, 0, uint.MaxValue),
                new Feature(new Tag('l', 'i', 'g', 'a'), 1, 0, uint.MaxValue),
                new Feature(new Tag('r', 'l', 'i', 'g'), 1, 0, uint.MaxValue),
            ];
        }

        font.Shape(buffer, features);

        int glyphCount = buffer.Length;
        var glyphInfos = new GlyphInfo[glyphCount];
        float advance = 0f;

        var infos = buffer.GlyphInfos;
        var positions = buffer.GlyphPositions;

        for (int i = 0; i < glyphCount; i++)
        {
            var info = infos[i];
            var pos = positions[i];

            glyphInfos[i] = new GlyphInfo(
                (ushort)info.Codepoint,
                pos.XAdvance / 64f,
                pos.XOffset / 64f,
                pos.YOffset / 64f,
                (int)info.Cluster);

            advance += pos.XAdvance / 64f;
        }

        return new ShapedRun(glyphInfos, advance, request.ScriptTag);
    }
}