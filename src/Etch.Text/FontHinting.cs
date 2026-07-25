namespace Etch.Text;

/// <summary>
/// Controls how aggressively a font is hinted during rasterization.
/// This affects the trade-off between sharpness (snapping to pixel grid)
/// and fidelity (preserving designed glyph shapes).
/// </summary>
public enum FontHinting : byte
{
    /// <summary>
    /// No hinting. Glyphs are rendered exactly as designed, which produces
    /// the most faithful shapes but can look blurry at small sizes.
    /// Equivalent to FT_LOAD_NO_HINTING.
    /// </summary>
    None = 0,

    /// <summary>
    /// Light hinting (Apple-style). Only subtle vertical snapping to improve
    /// consistency without distorting glyph shapes. Preferred for high-DPI
    /// displays and small text where full hinting looks too aggressive.
    /// Equivalent to FT_LOAD_TARGET_LIGHT.
    /// </summary>
    Slight = 1,

    /// <summary>
    /// Normal hinting (default). FreeType's autohinter snaps edges to the
    /// pixel grid for sharper text at the cost of some shape distortion.
    /// Equivalent to FT_LOAD_DEFAULT.
    /// </summary>
    Normal = 2,

    /// <summary>
    /// Full hinting (Windows-style). Uses the font's embedded bytecode
    /// instructions for maximum grid-fitting. Can look very sharp but may
    /// significantly distort shapes, especially at larger sizes.
    /// Equivalent to FT_LOAD_TARGET_NORMAL with bytecode interpreter.
    /// </summary>
    Full = 3,
}
