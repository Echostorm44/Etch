using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using Etch.Geometry;
using Etch.Scene;

namespace Etch.SvgTranslator;

/// <summary>
/// Translates a subset of SVG 1.1 into an Etch <see cref="SceneBuffer"/>.
/// </summary>
public static class SvgToSceneTranslator
{
    public static SceneBuffer Translate(string svgXml, int targetWidth, int targetHeight)
    {
        var doc = XDocument.Parse(svgXml);
        var svg = doc.Root;
        if (svg == null || svg.Name.LocalName != "svg")
            throw new ArgumentException("Root element is not <svg>");

        // Determine coordinate mapping
        var viewBox = ParseViewBox(svg.Attribute("viewBox")?.Value);
        double svgWidth = ParseLength(svg.Attribute("width")?.Value, viewBox.Width > 0 ? viewBox.Width : 100);
        double svgHeight = ParseLength(svg.Attribute("height")?.Value, viewBox.Height > 0 ? viewBox.Height : 100);

        if (viewBox.Width <= 0 || viewBox.Height <= 0)
        {
            viewBox = new Rect(0, 0, svgWidth, svgHeight);
        }

        double scaleX = targetWidth / viewBox.Width;
        double scaleY = targetHeight / viewBox.Height;
        double scale = Math.Min(scaleX, scaleY);

        // Compute translation to center content (if aspect ratios differ)
        double tx = (targetWidth - viewBox.Width * scale) * 0.5 - viewBox.MinX * scale;
        double ty = (targetHeight - viewBox.Height * scale) * 0.5 - viewBox.MinY * scale;

        var builder = SceneBuilder.Begin();

        var state = new TranslationState
        {
            Scale = scale,
            TranslateX = tx,
            TranslateY = ty,
        };

        // Add default transform that maps SVG space to output pixels
        int defaultTransformId = builder.AddTransform(
            Affine.Translate(tx, ty) * Affine.Scale(scale, scale));
        state.CurrentTransformId = defaultTransformId;

        builder.BeginFrame();

        // Parse defs (gradients, clips)
        var defs = svg.Element((XNamespace)"" + "defs");
        if (defs != null)
        {
            ParseDefs(defs, ref builder, state);
        }

        // Render elements
        foreach (var element in svg.Elements())
        {
            if (element.Name.LocalName == "defs")
                continue;
            RenderElement(element, ref builder, state, defaultTransformId);
        }

        builder.EndFrame();
        return builder.End();
    }

    private static void ParseDefs(XElement defs, ref SceneBuilder builder, TranslationState state)
    {
        foreach (var child in defs.Elements())
        {
            string name = child.Name.LocalName;
            if (name == "linearGradient" || name == "radialGradient")
            {
                string id = child.Attribute("id")?.Value ?? "";
                if (string.IsNullOrEmpty(id))
                    continue;

                var stops = new List<(float offset, uint argb)>();
                foreach (var stop in child.Elements())
                {
                    if (stop.Name.LocalName != "stop")
                        continue;
                    float offset = ParseStopOffset(stop.Attribute("offset")?.Value);
                    uint color = SvgColorParser.Parse(stop.Attribute("stop-color")?.Value);
                    float opacity = ParseOpacity(stop.Attribute("stop-opacity")?.Value);
                    color = (color & 0x00FFFFFF) | ((uint)(opacity * 255) << 24);
                    stops.Add((offset, color));
                }

                if (stops.Count >= 2)
                {
                    int gradientId = builder.AddGradientStops(GradientStops.Create(stops.ToArray()));
                    state.Gradients[id] = gradientId;
                }
            }
        }
    }

    private static void RenderElement(XElement element, ref SceneBuilder builder, TranslationState state, int parentTransformId)
    {
        string name = element.Name.LocalName;
        var transform = SvgTransformParser.Parse(element.Attribute("transform")?.Value);
        int elementTransformId = parentTransformId;

        if (!(transform == Affine.Identity))
        {
            elementTransformId = builder.AddTransform(transform);
        }

        var style = new SvgStyle(element);

        // Skip invisible elements
        if (style.Display == "none" || style.Opacity <= 0)
            return;

        // Group
        if (name == "g")
        {
            foreach (var child in element.Elements())
            {
                RenderElement(child, ref builder, state, elementTransformId);
            }
            return;
        }

        // Skip unsupported elements without crashing
        if (name is "text" or "tspan" or "image" or "use" or "switch" or "symbol" or "mask" or "filter")
        {
            state.UnsupportedElements.Add(name);
            return;
        }

        // Parse geometry
        BezPath? geometry = name switch
        {
            "rect" => ParseRect(element),
            "circle" => ParseCircle(element),
            "ellipse" => ParseEllipse(element),
            "line" => ParseLine(element),
            "polygon" => ParsePolygon(element, true),
            "polyline" => ParsePolygon(element, false),
            "path" => ParsePath(element),
            _ => null,
        };

        if (geometry == null)
            return;

        // For rectangles, use FillRect directly (avoids FillPath interior-tile
        // limitation in the current CPU renderer).
        if (name == "rect" && geometry.HasValue)
        {
            var rectAabb = geometry.Value.Aabb();
            if (style.HasFill && style.Fill != "none")
            {
                int paintId = ResolvePaint(style.Fill, style.FillOpacity, ref builder, state);
                if (paintId >= 0)
                {
                    builder.FillRect(rectAabb, paintId, elementTransformId);
                }
            }
            if (style.HasStroke && style.Stroke != "none" && style.StrokeWidth > 0)
            {
                int strokePaintId = ResolvePaint(style.Stroke, style.StrokeOpacity, ref builder, state);
                if (strokePaintId >= 0)
                {
                    int rectPathId = builder.AddPath(geometry.Value);
                    builder.StrokePath(rectPathId, strokePaintId, elementTransformId, (float)style.StrokeWidth, default);
                }
            }
            return;
        }

        int pathId = builder.AddPath(geometry.Value);

        // Fill
        if (style.HasFill && style.Fill != "none")
        {
            int paintId = ResolvePaint(style.Fill, style.FillOpacity, ref builder, state);
            if (paintId >= 0)
            {
                builder.FillPath(pathId, paintId, elementTransformId, style.FillRule);
            }
        }

        // Stroke
        if (style.HasStroke && style.Stroke != "none" && style.StrokeWidth > 0)
        {
            int strokePaintId = ResolvePaint(style.Stroke, style.StrokeOpacity, ref builder, state);
            if (strokePaintId >= 0)
            {
                builder.StrokePath(pathId, strokePaintId, elementTransformId, (float)style.StrokeWidth, default);
            }
        }
    }

    private static int ResolvePaint(string colorSpec, double opacity, ref SceneBuilder builder, TranslationState state)
    {
        if (string.IsNullOrWhiteSpace(colorSpec) || colorSpec == "none")
            return -1;

        // Check for url(#id) reference
        if (colorSpec.StartsWith("url(#", StringComparison.OrdinalIgnoreCase))
        {
            string id = colorSpec[5..];
            if (id.EndsWith(')'))
                id = id[..^1];
            if (state.Gradients.TryGetValue(id, out int gradientId))
            {
                // For now, always use LinearGradient paint kind for gradient references
                return builder.AddPaint(Paint.LinearGradient((uint)gradientId));
            }
            return -1;
        }

        uint color = SvgColorParser.Parse(colorSpec);
        uint a = (uint)Math.Clamp((color >> 24) * opacity, 0, 255);
        color = (color & 0x00FFFFFF) | (a << 24);
        return builder.AddPaint(Paint.Solid(color));
    }

    private static BezPath ParseRect(XElement e)
    {
        double x = ParseDouble(e.Attribute("x")?.Value);
        double y = ParseDouble(e.Attribute("y")?.Value);
        double w = ParseDouble(e.Attribute("width")?.Value);
        double h = ParseDouble(e.Attribute("height")?.Value);
        double rx = ParseDouble(e.Attribute("rx")?.Value);
        double ry = ParseDouble(e.Attribute("ry")?.Value);

        if (w <= 0 || h <= 0)
            return CreateEmptyPath();

        using var builder = BezPathBuilder.Begin();

        if (rx <= 0 && ry <= 0)
        {
            // Simple rectangle — explicit close via LineTo to work around
            // CurveFlattener.BezPath not emitting Close edges (COR-004).
            builder.MoveTo(new Point(x, y));
            builder.LineTo(new Point(x + w, y));
            builder.LineTo(new Point(x + w, y + h));
            builder.LineTo(new Point(x, y + h));
            builder.LineTo(new Point(x, y));
        }
        else
        {
            // Rounded rectangle — approximate with cubic beziers
            rx = rx > 0 ? rx : ry;
            ry = ry > 0 ? ry : rx;
            rx = Math.Min(rx, w * 0.5);
            ry = Math.Min(ry, h * 0.5);

            double k = 0.55228475;
            double kx = k * rx;
            double ky = k * ry;

            builder.MoveTo(new Point(x + rx, y));
            builder.LineTo(new Point(x + w - rx, y));
            builder.CubicTo(
                new Point(x + w - rx + kx, y),
                new Point(x + w, y + ry - ky),
                new Point(x + w, y + ry));
            builder.LineTo(new Point(x + w, y + h - ry));
            builder.CubicTo(
                new Point(x + w, y + h - ry + ky),
                new Point(x + w - rx + kx, y + h),
                new Point(x + w - rx, y + h));
            builder.LineTo(new Point(x + rx, y + h));
            builder.CubicTo(
                new Point(x + rx - kx, y + h),
                new Point(x, y + h - ry + ky),
                new Point(x, y + h - ry));
            builder.LineTo(new Point(x, y + ry));
            builder.CubicTo(
                new Point(x, y + ry - ky),
                new Point(x + rx - kx, y),
                new Point(x + rx, y));
            builder.LineTo(new Point(x + rx, y));
        }

        return builder.Build();
    }

    private static BezPath ParseCircle(XElement e)
    {
        double cx = ParseDouble(e.Attribute("cx")?.Value);
        double cy = ParseDouble(e.Attribute("cy")?.Value);
        double r = ParseDouble(e.Attribute("r")?.Value);
        if (r <= 0)
            return CreateEmptyPath();
        return CreateEllipsePath(cx, cy, r, r);
    }

    private static BezPath ParseEllipse(XElement e)
    {
        double cx = ParseDouble(e.Attribute("cx")?.Value);
        double cy = ParseDouble(e.Attribute("cy")?.Value);
        double rx = ParseDouble(e.Attribute("rx")?.Value);
        double ry = ParseDouble(e.Attribute("ry")?.Value);
        if (rx <= 0 || ry <= 0)
            return CreateEmptyPath();
        return CreateEllipsePath(cx, cy, rx, ry);
    }

    private static BezPath CreateEllipsePath(double cx, double cy, double rx, double ry)
    {
        double k = 0.55228475;
        double kx = k * rx;
        double ky = k * ry;

        using var builder = BezPathBuilder.Begin();
        builder.MoveTo(new Point(cx + rx, cy));
        builder.CubicTo(
            new Point(cx + rx, cy - ky),
            new Point(cx + kx, cy - ry),
            new Point(cx, cy - ry));
        builder.CubicTo(
            new Point(cx - kx, cy - ry),
            new Point(cx - rx, cy - ky),
            new Point(cx - rx, cy));
        builder.CubicTo(
            new Point(cx - rx, cy + ky),
            new Point(cx - kx, cy + ry),
            new Point(cx, cy + ry));
        builder.CubicTo(
            new Point(cx + kx, cy + ry),
            new Point(cx + rx, cy + ky),
            new Point(cx + rx, cy));
        builder.LineTo(new Point(cx + rx, cy));
        return builder.Build();
    }

    private static BezPath ParseLine(XElement e)
    {
        double x1 = ParseDouble(e.Attribute("x1")?.Value);
        double y1 = ParseDouble(e.Attribute("y1")?.Value);
        double x2 = ParseDouble(e.Attribute("x2")?.Value);
        double y2 = ParseDouble(e.Attribute("y2")?.Value);

        using var builder = BezPathBuilder.Begin();
        builder.MoveTo(new Point(x1, y1));
        builder.LineTo(new Point(x2, y2));
        return builder.Build();
    }

    private static BezPath ParsePolygon(XElement e, bool close)
    {
        string? pointsStr = e.Attribute("points")?.Value;
        if (string.IsNullOrWhiteSpace(pointsStr))
            return CreateEmptyPath();

        var points = ParsePoints(pointsStr);
        if (points.Count < 2)
            return CreateEmptyPath();

        using var builder = BezPathBuilder.Begin();
        builder.MoveTo(points[0]);
        for (int i = 1; i < points.Count; i++)
        {
            builder.LineTo(points[i]);
        }
        if (close)
        {
            // Explicit close via LineTo to work around CurveFlattener not
            // emitting Close edges.
            builder.LineTo(points[0]);
        }
        return builder.Build();
    }

    private static BezPath ParsePath(XElement e)
    {
        string? d = e.Attribute("d")?.Value;
        if (string.IsNullOrWhiteSpace(d))
            return CreateEmptyPath();
        return SvgPathParser.Parse(d);
    }

    private static readonly char[] s_pointSeparators = { ' ', '\t', '\n', '\r', ',' };
    private static readonly char[] s_viewBoxSeparators = { ' ', '\t', '\n', '\r', ',' };

    private static List<Point> ParsePoints(string value)
    {
        var result = new List<Point>();
        var parts = value.Split(s_pointSeparators, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i + 1 < parts.Length; i += 2)
        {
            if (double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double x) &&
                double.TryParse(parts[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
            {
                result.Add(new Point(x, y));
            }
        }
        return result;
    }

    private static Rect ParseViewBox(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Rect.Empty;

        var parts = value.Split(s_viewBoxSeparators, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4)
            return Rect.Empty;

        if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y) &&
            double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double w) &&
            double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double h))
        {
            if (w > 0 && h > 0)
                return Rect.FromMinSize(x, y, w, h);
        }

        return Rect.Empty;
    }

    private static double ParseLength(string? value, double fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        value = value.Trim();
        // Strip units (px, pt, em, %)
        int i = 0;
        while (i < value.Length && (char.IsDigit(value[i]) || value[i] == '.' || value[i] == '-' || value[i] == '+' || value[i] == 'e' || value[i] == 'E'))
            i++;
        var numStr = value[..i];
        if (double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
            return result;
        return fallback;
    }

    private static double ParseDouble(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0.0;
        if (double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
            return result;
        return 0.0;
    }

    private static float ParseStopOffset(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0f;
        value = value.Trim();
        if (value.EndsWith('%'))
        {
            if (double.TryParse(value.AsSpan(0, value.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double pct))
                return (float)(pct / 100.0);
        }
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
            return (float)d;
        return 0f;
    }

    private static float ParseOpacity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 1.0f;
        if (double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
            return (float)Math.Clamp(result, 0.0, 1.0);
        return 1.0f;
    }

    private static BezPath CreateEmptyPath()
    {
        using var builder = BezPathBuilder.Begin();
        builder.MoveTo(new Point(0, 0));
        return builder.Build();
    }

    private sealed class TranslationState
    {
        public required double Scale;
        public required double TranslateX;
        public required double TranslateY;
        public int CurrentTransformId;
        public readonly Dictionary<string, int> Gradients = new();
        public readonly HashSet<string> UnsupportedElements = new();
    }

    private readonly struct SvgStyle
    {
        public readonly bool HasFill;
        public readonly string Fill;
        public readonly double FillOpacity;
        public readonly FillRule FillRule;
        public readonly bool HasStroke;
        public readonly string Stroke;
        public readonly double StrokeWidth;
        public readonly double StrokeOpacity;
        public readonly string Display;
        public readonly double Opacity;

        public SvgStyle(XElement element)
        {
            string? styleAttr = element.Attribute("style")?.Value;
            var styleDict = ParseStyle(styleAttr);

            HasFill = true;
            Fill = GetValue(element, "fill", styleDict, "black");
            if (Fill == "none")
                HasFill = false;

            FillOpacity = ParseDouble(GetValue(element, "fill-opacity", styleDict, "1"));
            string rule = GetValue(element, "fill-rule", styleDict, "nonzero");
            FillRule = rule == "evenodd" ? FillRule.EvenOdd : FillRule.NonZero;

            HasStroke = false;
            Stroke = GetValue(element, "stroke", styleDict, "none");
            HasStroke = Stroke != "none" && !string.IsNullOrWhiteSpace(Stroke);
            StrokeWidth = ParseDouble(GetValue(element, "stroke-width", styleDict, "1"));
            StrokeOpacity = ParseDouble(GetValue(element, "stroke-opacity", styleDict, "1"));

            Display = GetValue(element, "display", styleDict, "");
            Opacity = ParseDouble(GetValue(element, "opacity", styleDict, "1"));
        }

        private static string GetValue(XElement element, string attrName, Dictionary<string, string> styleDict, string defaultValue)
        {
            if (styleDict.TryGetValue(attrName, out string? styleValue))
                return styleValue;
            return element.Attribute(attrName)?.Value ?? defaultValue;
        }

        private static Dictionary<string, string> ParseStyle(string? style)
        {
            var dict = new Dictionary<string, string>();
            if (string.IsNullOrWhiteSpace(style))
                return dict;

            var parts = style.Split(';', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var kv = part.Split(':', 2);
                if (kv.Length == 2)
                {
                    dict[kv[0].Trim().ToLowerInvariant()] = kv[1].Trim();
                }
            }
            return dict;
        }
    }
}
