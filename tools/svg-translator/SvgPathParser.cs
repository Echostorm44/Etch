using System;
using Etch.Geometry;

namespace Etch.SvgTranslator;

/// <summary>
/// Parses SVG path data (the <c>d</c> attribute) into an Etch <see cref="BezPath"/>.
/// Supports: M/m, L/l, H/h, V/v, C/c, S/s, Q/q, T/t, Z/z.
/// Arcs (A/a) are not supported — they throw <see cref="NotSupportedException"/>.
/// </summary>
public static class SvgPathParser
{
    public static BezPath Parse(string d)
    {
        using var builder = BezPathBuilder.Begin();
        var tokenizer = new PathTokenizer(d);

        char currentCommand = '\0';
        Point currentPoint = new Point(0, 0);
        Point? lastCubicControl = null;
        Point? lastQuadControl = null;
        bool hasCurrent = false;

        while (tokenizer.TryPeek(out char ch))
        {
            if (IsCommand(ch))
            {
                currentCommand = tokenizer.ReadChar();
            }
            else if (currentCommand == '\0')
            {
                // Unexpected data before first command
                SkipToNextCommand(tokenizer);
                continue;
            }

            bool relative = char.IsLower(currentCommand);
            char cmd = char.ToUpper(currentCommand, System.Globalization.CultureInfo.InvariantCulture);

            switch (cmd)
            {
                case 'M':
                    currentPoint = ReadPoint(ref tokenizer, relative, currentPoint);
                    if (!hasCurrent)
                    {
                        builder.MoveTo(currentPoint);
                        hasCurrent = true;
                    }
                    else
                    {
                        builder.LineTo(currentPoint);
                    }
                    // Subsequent pairs are treated as lineto
                    currentCommand = relative ? 'l' : 'L';
                    while (tokenizer.TryPeekNumber())
                    {
                        currentPoint = ReadPoint(ref tokenizer, relative, currentPoint);
                        builder.LineTo(currentPoint);
                    }
                    lastCubicControl = null;
                    lastQuadControl = null;
                    break;

                case 'L':
                    currentPoint = ReadPoint(ref tokenizer, relative, currentPoint);
                    builder.LineTo(currentPoint);
                    lastCubicControl = null;
                    lastQuadControl = null;
                    break;

                case 'H':
                    double hx = ReadNumber(ref tokenizer);
                    if (relative) hx += currentPoint.X;
                    currentPoint = new Point(hx, currentPoint.Y);
                    builder.LineTo(currentPoint);
                    lastCubicControl = null;
                    lastQuadControl = null;
                    break;

                case 'V':
                    double vy = ReadNumber(ref tokenizer);
                    if (relative) vy += currentPoint.Y;
                    currentPoint = new Point(currentPoint.X, vy);
                    builder.LineTo(currentPoint);
                    lastCubicControl = null;
                    lastQuadControl = null;
                    break;

                case 'C':
                    {
                        Point c1 = ReadPoint(ref tokenizer, relative, currentPoint);
                        Point c2 = ReadPoint(ref tokenizer, relative, currentPoint);
                        Point end = ReadPoint(ref tokenizer, relative, currentPoint);
                        builder.CubicTo(c1, c2, end);
                        lastCubicControl = c2;
                        currentPoint = end;
                        lastQuadControl = null;
                    }
                    break;

                case 'S':
                    {
                        Point c1 = lastCubicControl.HasValue
                            ? Reflect(currentPoint, lastCubicControl.Value)
                            : currentPoint;
                        Point c2 = ReadPoint(ref tokenizer, relative, currentPoint);
                        Point end = ReadPoint(ref tokenizer, relative, currentPoint);
                        builder.CubicTo(c1, c2, end);
                        lastCubicControl = c2;
                        currentPoint = end;
                        lastQuadControl = null;
                    }
                    break;

                case 'Q':
                    {
                        Point ctrl = ReadPoint(ref tokenizer, relative, currentPoint);
                        Point end = ReadPoint(ref tokenizer, relative, currentPoint);
                        builder.QuadTo(ctrl, end);
                        lastQuadControl = ctrl;
                        currentPoint = end;
                        lastCubicControl = null;
                    }
                    break;

                case 'T':
                    {
                        Point ctrl = lastQuadControl.HasValue
                            ? Reflect(currentPoint, lastQuadControl.Value)
                            : currentPoint;
                        Point end = ReadPoint(ref tokenizer, relative, currentPoint);
                        builder.QuadTo(ctrl, end);
                        lastQuadControl = ctrl;
                        currentPoint = end;
                        lastCubicControl = null;
                    }
                    break;

                case 'Z':
                    if (hasCurrent)
                    {
                        // Explicit close via LineTo to work around
                        // CurveFlattener.BezPath not emitting Close edges.
                        builder.LineTo(currentPoint);
                    }
                    hasCurrent = false;
                    lastCubicControl = null;
                    lastQuadControl = null;
                    break;

                case 'A':
                    {
                        // Parse arc-to: rx ry x-axis-rotation large-arc sweep x y
                        float rx = tokenizer.ReadFloat();
                        float ry = tokenizer.ReadFloat();
                        float xAxisRotation = tokenizer.ReadFloat();
                        bool largeArc = tokenizer.ReadFloat() != 0;
                        bool sweep = tokenizer.ReadFloat() != 0;
                        float x = tokenizer.ReadFloat();
                        float y = tokenizer.ReadFloat();

                        if (rx <= 0 || ry <= 0)
                        {
                            // Degenerate arc → line
                            builder.LineTo(new Point(x, y));
                            currentPoint = new Point(x, y);
                            hasCurrent = true;
                            break;
                        }

                        var endPoint = new Point(x, y);
                        ApproximateArc(builder, currentPoint, endPoint, rx, ry, xAxisRotation, largeArc, sweep);
                        currentPoint = endPoint;
                        hasCurrent = true;
                        break;
                    }

                default:
                    // Unknown command — skip to next known command
                    SkipToNextCommand(tokenizer);
                    currentCommand = '\0';
                    break;
            }
        }

        return builder.Build();
    }

    private static Point Reflect(Point origin, Point control)
    {
        return new Point(
            origin.X + (origin.X - control.X),
            origin.Y + (origin.Y - control.Y));
    }

    private static Point ReadPoint(ref PathTokenizer tokenizer, bool relative, Point current)
    {
        double x = ReadNumber(ref tokenizer);
        double y = ReadNumber(ref tokenizer);
        if (relative)
        {
            x += current.X;
            y += current.Y;
        }
        return new Point(x, y);
    }

    private static double ReadNumber(ref PathTokenizer tokenizer)
    {
        tokenizer.SkipSeparators();
        return tokenizer.ReadNumber();
    }

    private static bool IsCommand(char ch)
    {
        return "MmLlHhVvCcSsQqTtAaZz".Contains(ch, StringComparison.Ordinal);
    }

    private static void SkipToNextCommand(PathTokenizer tokenizer)
    {
        while (tokenizer.TryPeek(out char ch) && !IsCommand(ch))
        {
            tokenizer.ReadChar();
        }
    }

    /// <summary>
    /// Simple tokenizer for SVG path data strings.
    /// </summary>
    private ref struct PathTokenizer
    {
        private readonly ReadOnlySpan<char> _source;
        private int _index;

        public PathTokenizer(string source)
        {
            _source = source.AsSpan();
            _index = 0;
        }

        public bool TryPeek(out char ch)
        {
            if (_index < _source.Length)
            {
                ch = _source[_index];
                return true;
            }
            ch = '\0';
            return false;
        }

        public char ReadChar()
        {
            return _source[_index++];
        }

        public bool TryPeekNumber()
        {
            SkipSeparators();
            if (_index >= _source.Length)
                return false;
            char ch = _source[_index];
            return ch == '+' || ch == '-' || ch == '.' || char.IsDigit(ch);
        }

        public void SkipSeparators()
        {
            while (_index < _source.Length)
            {
                char ch = _source[_index];
                if (char.IsWhiteSpace(ch) || ch == ',')
                {
                    _index++;
                }
                else
                {
                    break;
                }
            }
        }

        public double ReadNumber()
        {
            SkipSeparators();
            int start = _index;
            bool hasDigits = false;

            // Sign
            if (_index < _source.Length && (_source[_index] == '+' || _source[_index] == '-'))
            {
                _index++;
            }

            // Integer part
            while (_index < _source.Length && char.IsDigit(_source[_index]))
            {
                _index++;
                hasDigits = true;
            }

            // Fractional part
            if (_index < _source.Length && _source[_index] == '.')
            {
                _index++;
                while (_index < _source.Length && char.IsDigit(_source[_index]))
                {
                    _index++;
                    hasDigits = true;
                }
            }

            // Exponent
            if (_index < _source.Length && (_source[_index] == 'e' || _source[_index] == 'E'))
            {
                int expStart = _index;
                _index++;
                if (_index < _source.Length && (_source[_index] == '+' || _source[_index] == '-'))
                {
                    _index++;
                }
                bool hasExpDigits = false;
                while (_index < _source.Length && char.IsDigit(_source[_index]))
                {
                    _index++;
                    hasExpDigits = true;
                }
                if (!hasExpDigits)
                {
                    // Roll back exponent
                    _index = expStart;
                }
            }

            if (!hasDigits)
            {
                // No valid number found — return 0 and don't advance
                return 0.0;
            }

            var slice = _source[start.._index];
            if (double.TryParse(slice, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double result))
            {
                return result;
            }
            return 0.0;
        }
    }

    private static void ApproximateArc(BezPathBuilder builder, Point start, Point end,
        float rx, float ry, float xAxisRotation, bool largeArc, bool sweep)
    {
        // Convert SVG arc to cubic Beziers using the standard parameterization.
        double x1 = start.X, y1 = start.Y;
        double x2 = end.X, y2 = end.Y;

        double phi = xAxisRotation * Math.PI / 180.0;
        double cosPhi = Math.Cos(phi);
        double sinPhi = Math.Sin(phi);

        double dx = (x1 - x2) / 2.0;
        double dy = (y1 - y2) / 2.0;
        double x1p = cosPhi * dx + sinPhi * dy;
        double y1p = -sinPhi * dx + cosPhi * dy;

        double rxs = (double)rx * rx;
        double rys = (double)ry * ry;
        double x1ps = x1p * x1p;
        double y1ps = y1p * y1p;

        double lambda = x1ps / rxs + y1ps / rys;
        if (lambda > 1.0)
        {
            double scale = Math.Sqrt(lambda);
            rx = (float)(rx * scale);
            ry = (float)(ry * scale);
            rxs = rx * rx;
            rys = ry * ry;
        }

        double sign = largeArc == sweep ? -1.0 : 1.0;
        double num = rxs * rys - rxs * y1ps - rys * x1ps;
        double den = rxs * y1ps + rys * x1ps;
        double coef = sign * Math.Sqrt(Math.Max(0, num / den));
        double cxp = coef * ((double)rx * y1p) / ry;
        double cyp = coef * (-(double)ry * x1p) / rx;

        double cx = cosPhi * cxp - sinPhi * cyp + (x1 + x2) / 2.0;
        double cy = sinPhi * cxp + cosPhi * cyp + (y1 + y2) / 2.0;

        double theta1 = Math.Atan2((y1p - cyp) / ry, (x1p - cxp) / rx);
        double dtheta = Math.Atan2((-y1p - cyp) / ry, (-x1p - cxp) / rx) - theta1;

        if (!sweep && dtheta > 0) dtheta -= 2 * Math.PI;
        if (sweep && dtheta < 0) dtheta += 2 * Math.PI;

        int segments = (int)Math.Ceiling(Math.Abs(dtheta) / (Math.PI / 2.0));
        double delta = dtheta / segments;
        double t = theta1;

        for (int i = 0; i < segments; i++)
        {
            double t2 = t + delta;
            double a = Math.Sin(delta) * (Math.Sqrt(4.0 + 3.0 * Math.Pow(Math.Tan(delta / 2.0), 2.0)) - 1.0) / 3.0;

            double sinT = Math.Sin(t), cosT = Math.Cos(t);
            double sinT2 = Math.Sin(t2), cosT2 = Math.Cos(t2);

            double p1x = cx + rx * cosT * cosPhi - ry * sinT * sinPhi;
            double p1y = cy + rx * cosT * sinPhi + ry * sinT * cosPhi;
            double p4x = cx + rx * cosT2 * cosPhi - ry * sinT2 * sinPhi;
            double p4y = cy + rx * cosT2 * sinPhi + ry * sinT2 * cosPhi;

            double c1x = p1x - a * (rx * sinT * cosPhi + ry * cosT * sinPhi);
            double c1y = p1y - a * (rx * sinT * sinPhi - ry * cosT * cosPhi);
            double c2x = p4x + a * (rx * sinT2 * cosPhi + ry * cosT2 * sinPhi);
            double c2y = p4y + a * (rx * sinT2 * sinPhi - ry * cosT2 * cosPhi);

            builder.CubicTo(new Point(c1x, c1y), new Point(c2x, c2y), new Point(p4x, p4y));
            t = t2;
        }
    }
}
