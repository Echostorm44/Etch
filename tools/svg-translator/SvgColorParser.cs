using System;
using System.Collections.Generic;

namespace Etch.SvgTranslator;

/// <summary>
/// Parses SVG color values into 32-bit ARGB.
/// Supports: #RGB, #RRGGBB, #RGBA, #RRGGBBAA, rgb(), rgba(), and named colors.
/// </summary>
public static class SvgColorParser
{
    private static readonly Dictionary<string, uint> s_namedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = 0xFF000000,
        ["white"] = 0xFFFFFFFF,
        ["red"] = 0xFFFF0000,
        ["green"] = 0xFF008000,
        ["lime"] = 0xFF00FF00,
        ["blue"] = 0xFF0000FF,
        ["yellow"] = 0xFFFFFF00,
        ["cyan"] = 0xFF00FFFF,
        ["aqua"] = 0xFF00FFFF,
        ["magenta"] = 0xFFFF00FF,
        ["fuchsia"] = 0xFFFF00FF,
        ["silver"] = 0xFFC0C0C0,
        ["gray"] = 0xFF808080,
        ["grey"] = 0xFF808080,
        ["maroon"] = 0xFF800000,
        ["olive"] = 0xFF808000,
        ["navy"] = 0xFF000080,
        ["purple"] = 0xFF800080,
        ["teal"] = 0xFF008080,
        ["orange"] = 0xFFFFA500,
        ["transparent"] = 0x00000000,
    };

    public static uint Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0xFF000000; // default black

        value = value.Trim();

        if (value.StartsWith('#'))
        {
            return ParseHex(value);
        }

        if (value.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase))
        {
            return ParseRgb(value);
        }

        if (s_namedColors.TryGetValue(value, out uint named))
        {
            return named;
        }

        return 0xFF000000;
    }

    private static uint ParseHex(string value)
    {
        ReadOnlySpan<char> span = value.AsSpan(1);
        if (span.Length == 3)
        {
            uint r = HexDigit(span[0]) * 17u;
            uint g = HexDigit(span[1]) * 17u;
            uint b = HexDigit(span[2]) * 17u;
            return 0xFF000000u | (r << 16) | (g << 8) | b;
        }
        if (span.Length == 4)
        {
            uint r = HexDigit(span[0]) * 17u;
            uint g = HexDigit(span[1]) * 17u;
            uint b = HexDigit(span[2]) * 17u;
            uint a = HexDigit(span[3]) * 17u;
            return (a << 24) | (r << 16) | (g << 8) | b;
        }
        if (span.Length == 6)
        {
            uint r = (HexDigit(span[0]) << 4) | HexDigit(span[1]);
            uint g = (HexDigit(span[2]) << 4) | HexDigit(span[3]);
            uint b = (HexDigit(span[4]) << 4) | HexDigit(span[5]);
            return 0xFF000000u | (r << 16) | (g << 8) | b;
        }
        if (span.Length == 8)
        {
            uint r = (HexDigit(span[0]) << 4) | HexDigit(span[1]);
            uint g = (HexDigit(span[2]) << 4) | HexDigit(span[3]);
            uint b = (HexDigit(span[4]) << 4) | HexDigit(span[5]);
            uint a = (HexDigit(span[6]) << 4) | HexDigit(span[7]);
            return (a << 24) | (r << 16) | (g << 8) | b;
        }
        return 0xFF000000;
    }

    private static uint HexDigit(char c)
    {
        if (c >= '0' && c <= '9') return (uint)(c - '0');
        if (c >= 'a' && c <= 'f') return (uint)(c - 'a' + 10);
        if (c >= 'A' && c <= 'F') return (uint)(c - 'A' + 10);
        return 0;
    }

    private static uint ParseRgb(string value)
    {
        bool hasAlpha = value.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase);
        int start = hasAlpha ? 5 : 4;
        var inner = value.AsSpan(start);
        if (inner.Length > 0 && inner[^1] == ')')
            inner = inner[..^1];

        var parts = inner.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
            return 0xFF000000;

        uint r = ParseColorComponent(parts[0].Trim());
        uint g = ParseColorComponent(parts[1].Trim());
        uint b = ParseColorComponent(parts[2].Trim());
        uint a = 255;
        if (hasAlpha && parts.Length >= 4)
        {
            if (double.TryParse(parts[3].Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double af))
            {
                a = (uint)Math.Clamp(af * 255.0, 0.0, 255.0);
            }
        }

        return (a << 24) | (r << 16) | (g << 8) | b;
    }

    private static uint ParseColorComponent(string s)
    {
        if (s.EndsWith('%'))
        {
            if (double.TryParse(s.AsSpan(0, s.Length - 1), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double pct))
            {
                return (uint)Math.Clamp(pct * 2.55, 0.0, 255.0);
            }
        }
        if (int.TryParse(s, out int v))
        {
            return (uint)Math.Clamp(v, 0, 255);
        }
        return 0;
    }
}
