using System;

namespace Etch.Text.Shape;

public readonly struct GlyphInfo
{
    public ushort GlyphId { get; }
    public float XAdvance { get; }
    public float XOffset { get; }
    public float YOffset { get; }
    public int Cluster { get; }

    public GlyphInfo(ushort glyphId, float xAdvance, float xOffset, float yOffset, int cluster)
    {
        GlyphId = glyphId;
        XAdvance = xAdvance;
        XOffset = xOffset;
        YOffset = yOffset;
        Cluster = cluster;
    }
}

public readonly struct ShapedRun
{
    public GlyphInfo[] Glyphs { get; }
    public float Advance { get; }
    public string ScriptTag { get; }

    public ShapedRun(GlyphInfo[] glyphs, float advance, string scriptTag)
    {
        Glyphs = glyphs;
        Advance = advance;
        ScriptTag = scriptTag;
    }

    public int GlyphCount => Glyphs.Length;
}