using System;
using Etch.Geometry;

namespace Etch.Scene;

/// <summary>
/// Geometry and falloff for <see cref="SceneOpcode.DrawShadow"/>. A Gaussian-blurred rounded
/// rectangle has a closed form, so renderers draw shadows analytically in one pass instead of
/// rendering and blurring a mask: the shadow's path is reduced to a rounded rectangle. A
/// rectangle, a rounded rectangle and a circle are recognised exactly; any other path is
/// shadowed by its bounding box.
/// </summary>
public static class ShadowShape
{
    /// <summary>
    /// Reduces <paramref name="path"/> to the rounded rectangle its shadow is drawn from.
    /// Returns false only for an empty path.
    /// </summary>
    public static bool TryResolve(BezPath path, out Rect rect, out double cornerRadius)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        int moves = 0, lines = 0, cubics = 0, quads = 0;
        double firstCubicExtent = 0;
        Point previous = default;

        foreach (var seg in path.Iterate())
        {
            switch (seg.Verb)
            {
                case PathVerb.MoveTo:
                    moves++;
                    Extend(seg.End);
                    break;
                case PathVerb.LineTo:
                    lines++;
                    Extend(seg.End);
                    break;
                case PathVerb.QuadTo:
                    quads++;
                    Extend(seg.Control0);
                    Extend(seg.End);
                    break;
                case PathVerb.CubicTo:
                    if (cubics == 0)
                    {
                        firstCubicExtent = Math.Min(Math.Abs(seg.End.X - previous.X), Math.Abs(seg.End.Y - previous.Y));
                    }
                    cubics++;
                    // For the circle/rounded-corner constructions the control points lie on the
                    // tangents at the extremes, so the control-point box is the curve's box.
                    Extend(seg.Control0);
                    Extend(seg.Control1);
                    Extend(seg.End);
                    break;
            }
            if (seg.Verb != PathVerb.Close)
            {
                previous = seg.End;
            }
        }

        if (moves == 0)
        {
            rect = default;
            cornerRadius = 0;
            return false;
        }

        rect = new Rect(minX, minY, maxX, maxY);
        double halfMin = Math.Min(rect.Width, rect.Height) * 0.5;
        if (moves == 1 && quads == 0 && cubics == 4 && lines == 0)
        {
            cornerRadius = halfMin; // circle (an ellipse becomes a stadium)
        }
        else if (moves == 1 && quads == 0 && cubics == 4 && lines == 4)
        {
            cornerRadius = Math.Min(firstCubicExtent, halfMin); // rounded rectangle
        }
        else
        {
            cornerRadius = 0; // rectangle, or any other path by its bounds
        }
        return true;

        void Extend(Point p)
        {
            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X);
            maxY = Math.Max(maxY, p.Y);
        }
    }

    /// <summary>
    /// Coverage in [0, 1] at (<paramref name="px"/>, <paramref name="py"/>) of the rounded
    /// rectangle blurred by a Gaussian of standard deviation <paramref name="sigma"/> (&gt; 0).
    /// Exact across x (via erf); integrated over y with four Gaussian-weighted samples
    /// (Evan Wallace, "Fast Rounded Rectangle Shadows"). GPU renderers must match this.
    /// </summary>
    public static double Coverage(Rect rect, double cornerRadius, double sigma, double px, double py)
    {
        double halfX = rect.Width * 0.5;
        double halfY = rect.Height * 0.5;
        double corner = Math.Min(cornerRadius, Math.Min(halfX, halfY));
        double x = px - (rect.MinX + rect.MaxX) * 0.5;
        double y = py - (rect.MinY + rect.MaxY) * 0.5;

        double low = y - halfY;
        double high = y + halfY;
        double start = Math.Clamp(-3.0 * sigma, low, high);
        double end = Math.Clamp(3.0 * sigma, low, high);
        double step = (end - start) / 4.0;
        double sample = start + step * 0.5;
        double value = 0;
        for (int i = 0; i < 4; i++)
        {
            value += CoverageX(x, y - sample, sigma, corner, halfX, halfY) * Gaussian(sample, sigma) * step;
            sample += step;
        }
        return Math.Clamp(value, 0.0, 1.0);
    }

    /// <summary>
    /// The σ to render with: a blur of 0 is a sharp offset copy, drawn as a sub-pixel blur so it keeps
    /// its rounded corners and an anti-aliased edge.
    /// </summary>
    public static double EffectiveSigma(double sigma) => Math.Max(sigma, MinSigma);

    /// <summary>Smallest σ rendered (device pixels); see <see cref="EffectiveSigma"/>.</summary>
    public const double MinSigma = 0.35;

    /// <summary>How far past the shape a shadow of <paramref name="sigma"/> is visible (3σ).</summary>
    public static double Extent(double sigma) => 3.0 * sigma;

    private static double CoverageX(double x, double y, double sigma, double corner, double halfX, double halfY)
    {
        double delta = Math.Min(halfY - corner - Math.Abs(y), 0.0);
        double curved = halfX - corner + Math.Sqrt(Math.Max(0.0, corner * corner - delta * delta));
        double scale = Math.Sqrt(0.5) / sigma;
        double lower = 0.5 + 0.5 * Erf((x - curved) * scale);
        double upper = 0.5 + 0.5 * Erf((x + curved) * scale);
        return upper - lower;
    }

    private static double Gaussian(double x, double sigma)
        => Math.Exp(-(x * x) / (2.0 * sigma * sigma)) / (Math.Sqrt(2.0 * Math.PI) * sigma);

    // Abramowitz–Stegun 7.1.27 (max error 5e-4), the same approximation the GPU shaders use.
    private static double Erf(double x)
    {
        double s = Math.Sign(x);
        double a = Math.Abs(x);
        double r = 1.0 + (0.278393 + (0.230389 + 0.078108 * (a * a)) * a) * a;
        r *= r;
        return s - s / (r * r);
    }
}
