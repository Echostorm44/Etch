using System;
using System.Runtime.CompilerServices;
using Etch.Geometry;

namespace Etch.Text;

/// <summary>
/// LCD subpixel orientation for a text rendering surface.
/// </summary>
public enum SubpixelOrientation : byte
{
    /// <summary>
    /// Standard grayscale anti-aliasing (default).
    /// </summary>
    Grayscale = 0,

    /// <summary>
    /// Horizontal RGB stripe order (standard Windows LCD).
    /// </summary>
    RgbHorizontal = 1,
}

/// <summary>
/// Surface configuration for text rendering.
/// </summary>
public readonly struct SurfaceInfo
{
    /// <summary>
    /// Subpixel orientation for this surface. Defaults to <see cref="SubpixelOrientation.Grayscale"/>.
    /// </summary>
    public readonly SubpixelOrientation SubpixelOrientation;

    public SurfaceInfo(SubpixelOrientation orientation)
    {
        SubpixelOrientation = orientation;
    }

    /// <summary>
    /// Returns true if the given transform is axis-aligned (no rotation or skew)
    /// and the surface is configured for subpixel rendering.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ShouldUseSubpixel(Affine transform)
    {
        if (SubpixelOrientation == SubpixelOrientation.Grayscale)
        {
            return false;
        }

        // Axis-aligned: M01 and M10 must be zero (allowing epsilon).
        const double Epsilon = 1e-10;
        return Math.Abs(transform.M01) < Epsilon && Math.Abs(transform.M10) < Epsilon;
    }
}
