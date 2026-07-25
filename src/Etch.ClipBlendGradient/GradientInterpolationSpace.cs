namespace Etch.ClipBlendGradient;

public enum GradientInterpolationSpace : byte
{
    /// <summary>
    /// Linear-light interpolation (default). Colors are converted to linear space before
    /// interpolating, then converted back to the output space. Produces perceptually
    /// correct gradients especially for bright colors and long gradients.
    /// </summary>
    LinearLight = 0,

    /// <summary>
    /// sRGB interpolation. Matches legacy CSS 3 behaviour — colors are interpolated
    /// directly in sRGB space without linearization.
    /// </summary>
    /// <remarks>
    /// WARNING: sRGB interpolation produces muddy midtones in red-to-green and similar
    /// gradients where both channels are changing significantly. Prefer LinearLight for
    /// new work. This option is provided only for legacy authoring compatibility.
    /// </remarks>
    Srgb = 1,
}
