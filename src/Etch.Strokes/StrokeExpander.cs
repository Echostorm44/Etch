using System;
using System.Buffers;
using Etch.Geometry;
using Etch.Geometry.Flatten;

namespace Etch.Strokes;

public static class StrokeExpander
{
    public static BezPath Expand(BezPath input, StrokeStyle style)
    {
        if (input.IsEmpty || style.Width <= 0f || style.Width < 0.001f)
        {
            return input;
        }

        Span<Point> scratch = stackalloc Point[2048];
        var flatSink = new FlattenSink(scratch);
        CurveFlattener.BezPath(input, 0.25, ref flatSink);

        ReadOnlySpan<Point> points = flatSink.Written;
        int count = points.Length;
        if (count < 2)
        {
            return input;
        }

        float halfWidth = style.Width * 0.5f;
        var stroker = new PolylineStroker(points, style, halfWidth);
        return stroker.Stroke();
    }

    private ref struct PolylineStroker
    {
        private readonly ReadOnlySpan<Point> _points;
        private readonly StrokeStyle _style;
        private readonly float _halfWidth;
        private readonly float _miterLimit;

        public PolylineStroker(ReadOnlySpan<Point> points, StrokeStyle style, float halfWidth)
        {
            _points = points;
            _style = style;
            _halfWidth = halfWidth;
            _miterLimit = style.MiterLimit * halfWidth;
        }

        public BezPath Stroke()
        {
            int n = _points.Length;
            if (n < 2) return EmptyPath();

            int approxVerts = n * 4 + 10;
            var builder = BezPathBuilder.Begin(approxVerts);

            Point startTangent = ComputeTangent(0, 1);
            Vec2 startNormal = new Vec2(-startTangent.Y, startTangent.X);

            builder.MoveTo(_points[0] + startNormal * _halfWidth);
            builder.LineTo(_points[0] - startNormal * _halfWidth);

            switch (_style.Cap)
            {
                case CapStyle.Butt:
                    EmitButtCapStart(builder, _points[0], startTangent);
                    break;
                case CapStyle.Round:
                    EmitRoundCapStart(builder, _points[0], startNormal);
                    break;
                case CapStyle.Square:
                    EmitSquareCapStart(builder, _points[0], startTangent, startNormal);
                    break;
            }

            for (int i = 1; i < n - 1; i++)
            {
                Point prev = _points[i - 1];
                Point curr = _points[i];
                Point next = _points[i + 1];

                EmitJoin(builder, prev, curr, next);
            }

            for (int i = n - 2; i >= 1; i--)
            {
                Point curr = _points[i];
                Point next = _points[i + 1];
                Point tangent = ComputeTangent(i, i + 1);
                Vec2 normal = new Vec2(-tangent.Y, tangent.X);
                builder.LineTo(curr - normal * _halfWidth);
            }

            Point endTangent = ComputeTangent(n - 2, n - 1);
            Vec2 endNormal = new Vec2(-endTangent.Y, endTangent.X);

            builder.LineTo(_points[n - 1] + endNormal * _halfWidth);

            switch (_style.Cap)
            {
                case CapStyle.Butt:
                    EmitButtCapEnd(builder, _points[n - 1], endTangent);
                    break;
                case CapStyle.Round:
                    EmitRoundCapEnd(builder, _points[n - 1], endNormal);
                    break;
                case CapStyle.Square:
                    EmitSquareCapEnd(builder, _points[n - 1], endTangent, endNormal);
                    break;
            }

            builder.LineTo(_points[n - 1] - endNormal * _halfWidth);

            builder.Close();
            return builder.Build();
        }

        private void EmitJoin(BezPathBuilder b, Point prev, Point curr, Point next)
        {
            double dx1 = curr.X - prev.X;
            double dy1 = curr.Y - prev.Y;
            double dx2 = next.X - curr.X;
            double dy2 = next.Y - curr.Y;

            double len1Sq = dx1 * dx1 + dy1 * dy1;
            double len2Sq = dx2 * dx2 + dy2 * dy2;

            if (len1Sq < 1e-20 || len2Sq < 1e-20)
            {
                Vec2 n = Normalize(new Vec2(dx1, dy1)).Perpendicular();
                b.LineTo(curr - n * _halfWidth);
                return;
            }

            double len1 = Math.Sqrt(len1Sq);
            double len2 = Math.Sqrt(len2Sq);

            double nx1 = -dy1 / len1;
            double ny1 = dx1 / len1;
            double nx2 = -dy2 / len2;
            double ny2 = dx2 / len2;

            double cross = dx1 * dy2 - dy1 * dx2;

            switch (_style.Join)
            {
                case JoinStyle.Miter:
                    {
                        double divisor = nx1 * ny2 - ny1 * nx2;
                        if (Math.Abs(divisor) < 1e-10) goto case JoinStyle.Bevel;

                        double t = ((curr.X - next.X) * ny2 - (curr.Y - next.Y) * nx2) / divisor;
                        double mx = next.X + nx2 * t;
                        double my = next.Y + ny2 * t;

                        double miterLen = Math.Sqrt((mx - curr.X) * (mx - curr.X) + (my - curr.Y) * (my - curr.Y));
                        if (miterLen > _miterLimit) goto case JoinStyle.Bevel;

                        double sign = cross >= 0 ? 1.0 : -1.0;
                        b.LineTo(curr + new Vec2((mx - curr.X) * sign, (my - curr.Y) * sign) * (_halfWidth / miterLen));
                    }
                    break;

                case JoinStyle.Round:
                    {
                        double sign = cross >= 0 ? 1.0 : -1.0;
                        b.LineTo(curr + new Vec2(nx1, ny1) * _halfWidth);
                        EmitArcCorner(b, curr, sign);
                        b.LineTo(curr + new Vec2(nx2, ny2) * _halfWidth);
                    }
                    break;

                case JoinStyle.Bevel:
                default:
                    b.LineTo(curr + new Vec2(nx1, ny1) * _halfWidth);
                    b.LineTo(curr + new Vec2(nx2, ny2) * _halfWidth);
                    break;
            }
        }

        private void EmitArcCorner(BezPathBuilder b, Point center, double sign)
        {
            const int segments = 6;
            double angleStep = Math.PI / segments * sign;

            double startAngle = Math.Atan2(-sign, 0);
            for (int i = 1; i <= segments; i++)
            {
                double angle = startAngle + angleStep * i;
                double x = center.X + Math.Cos(angle) * _halfWidth;
                double y = center.Y + Math.Sin(angle) * _halfWidth;
                b.LineTo(new Point(x, y));
            }
        }

        private void EmitButtCapStart(BezPathBuilder b, Point p, Point tangent)
        {
            Vec2 n = new Vec2(-tangent.Y, tangent.X) * _halfWidth;
            Vec2 offset = new Vec2(tangent.X, tangent.Y) * _halfWidth;
            b.LineTo(p - n);
            b.LineTo(p - n - offset);
            b.LineTo(p + n - offset);
            b.LineTo(p + n);
        }

        private void EmitButtCapEnd(BezPathBuilder b, Point p, Point tangent)
        {
            Vec2 n = new Vec2(-tangent.Y, tangent.X) * _halfWidth;
            Vec2 offset = new Vec2(tangent.X, tangent.Y) * _halfWidth;
            b.LineTo(p + n);
            b.LineTo(p + n + offset);
            b.LineTo(p - n + offset);
            b.LineTo(p - n);
        }

        private void EmitRoundCapStart(BezPathBuilder b, Point p, Vec2 startNormal)
        {
            b.LineTo(p - startNormal * _halfWidth);

            const int segments = 8;
            double startAngle = Math.Atan2(-startNormal.Y, -startNormal.X);
            double angleStep = Math.PI / segments;
            for (int i = 1; i <= segments; i++)
            {
                double angle = startAngle + angleStep * i;
                double x = p.X + Math.Cos(angle) * _halfWidth;
                double y = p.Y + Math.Sin(angle) * _halfWidth;
                b.LineTo(new Point(x, y));
            }

            b.LineTo(p + startNormal * _halfWidth);
        }

        private void EmitRoundCapEnd(BezPathBuilder b, Point p, Vec2 endNormal)
        {
            b.LineTo(p + endNormal * _halfWidth);

            const int segments = 8;
            double startAngle = Math.Atan2(endNormal.Y, endNormal.X);
            double angleStep = Math.PI / segments;
            for (int i = 1; i <= segments; i++)
            {
                double angle = startAngle + angleStep * i;
                double x = p.X + Math.Cos(angle) * _halfWidth;
                double y = p.Y + Math.Sin(angle) * _halfWidth;
                b.LineTo(new Point(x, y));
            }

            b.LineTo(p - endNormal * _halfWidth);
        }

        private void EmitSquareCapStart(BezPathBuilder b, Point p, Point tangent, Vec2 normal)
        {
            Vec2 offset = new Vec2(tangent.X, tangent.Y) * _halfWidth;
            b.LineTo(p - normal * _halfWidth + offset);
            b.LineTo(p + normal * _halfWidth + offset);
        }

        private void EmitSquareCapEnd(BezPathBuilder b, Point p, Point tangent, Vec2 normal)
        {
            Vec2 offset = new Vec2(tangent.X, tangent.Y) * _halfWidth;
            b.LineTo(p + normal * _halfWidth - offset);
            b.LineTo(p - normal * _halfWidth - offset);
        }

        private Point ComputeTangent(int fromIdx, int toIdx)
        {
            Point from = _points[fromIdx];
            Point to = _points[toIdx];
            double dx = to.X - from.X;
            double dy = to.Y - from.Y;
            double lenSq = dx * dx + dy * dy;
            if (lenSq < 1e-20) return new Point(1, 0);
            double invLen = 1.0 / Math.Sqrt(lenSq);
            return new Point(dx * invLen, dy * invLen);
        }

        private static Vec2 Normalize(Vec2 v)
        {
            double lenSq = v.X * v.X + v.Y * v.Y;
            if (lenSq < 1e-20) return new Vec2(1, 0);
            double invLen = 1.0 / Math.Sqrt(lenSq);
            return new Vec2(v.X * invLen, v.Y * invLen);
        }

        private static BezPath EmptyPath()
        {
            return new BezPath(Array.Empty<byte>(), Array.Empty<double>(), 0);
        }
    }
}