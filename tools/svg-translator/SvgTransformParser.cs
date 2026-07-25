using System;
using Etch.Geometry;

namespace Etch.SvgTranslator;

/// <summary>
/// Parses SVG <c>transform</c> attribute strings into Etch <see cref="Affine"/> matrices.
/// Supports: translate(), scale(), rotate(), matrix().
/// </summary>
public static class SvgTransformParser
{
    public static Affine Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Affine.Identity;

        Affine result = Affine.Identity;
        var span = value.AsSpan();
        int idx = 0;

        while (idx < span.Length)
        {
            // Skip whitespace
            while (idx < span.Length && char.IsWhiteSpace(span[idx]))
                idx++;

            if (idx >= span.Length)
                break;

            // Read function name
            int nameStart = idx;
            while (idx < span.Length && char.IsLetter(span[idx]))
                idx++;
            var name = span[nameStart..idx].ToString();

            // Skip to '('
            while (idx < span.Length && span[idx] != '(')
                idx++;
            if (idx >= span.Length)
                break;
            idx++; // skip '('

            // Read inner
            int innerStart = idx;
            int parenDepth = 1;
            while (idx < span.Length && parenDepth > 0)
            {
                if (span[idx] == '(') parenDepth++;
                else if (span[idx] == ')') parenDepth--;
                idx++;
            }
            var inner = span[innerStart..(idx - 1)].ToString();

            Affine parsed = name.ToLowerInvariant() switch
            {
                "translate" => ParseTranslate(inner),
                "scale" => ParseScale(inner),
                "rotate" => ParseRotate(inner),
                "matrix" => ParseMatrix(inner),
                _ => Affine.Identity,
            };

            // SVG transforms are applied right-to-left in the attribute,
            // but our composition is "apply B then A" for A * B.
            // transform="translate(10) scale(2)" means: scale first, then translate.
            // So result = translate * scale = parsed * result
            result = parsed * result;
        }

        return result;
    }

    private static Affine ParseTranslate(string inner)
    {
        var parts = inner.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return Affine.Identity;
        double tx = ParseDouble(parts[0]);
        double ty = parts.Length > 1 ? ParseDouble(parts[1]) : 0.0;
        return Affine.Translate(tx, ty);
    }

    private static Affine ParseScale(string inner)
    {
        var parts = inner.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return Affine.Identity;
        double sx = ParseDouble(parts[0]);
        double sy = parts.Length > 1 ? ParseDouble(parts[1]) : sx;
        return Affine.Scale(sx, sy);
    }

    private static Affine ParseRotate(string inner)
    {
        var parts = inner.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return Affine.Identity;
        double angleDeg = ParseDouble(parts[0]);
        double angleRad = angleDeg * Math.PI / 180.0;

        if (parts.Length >= 3)
        {
            double cx = ParseDouble(parts[1]);
            double cy = ParseDouble(parts[2]);
            return Affine.Translate(cx, cy) * Affine.Rotate(angleRad) * Affine.Translate(-cx, -cy);
        }

        return Affine.Rotate(angleRad);
    }

    private static Affine ParseMatrix(string inner)
    {
        var parts = inner.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 6)
            return Affine.Identity;
        double a = ParseDouble(parts[0]);
        double b = ParseDouble(parts[1]);
        double c = ParseDouble(parts[2]);
        double d = ParseDouble(parts[3]);
        double e = ParseDouble(parts[4]);
        double f = ParseDouble(parts[5]);
        return new Affine(a, b, c, d, e, f);
    }

    private static double ParseDouble(string s)
    {
        if (double.TryParse(s.Trim(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double result))
        {
            return result;
        }
        return 0.0;
    }
}
