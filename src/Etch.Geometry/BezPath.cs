using System;
using System.Buffers;
using System.Runtime.InteropServices;

namespace Etch.Geometry;

public enum PathVerb : byte
{
    MoveTo = 0,
    LineTo = 1,
    QuadTo = 2,
    CubicTo = 3,
    Close = 4,
}

public static class PathVerbData
{
    public static int CoordCount(PathVerb verb) => verb switch
    {
        PathVerb.MoveTo => 2,
        PathVerb.LineTo => 2,
        PathVerb.QuadTo => 4,
        PathVerb.CubicTo => 6,
        PathVerb.Close => 0,
        _ => 0,
    };
}

public readonly struct BezPath
{
    // Legacy mode: separate arrays
    private readonly byte[]? _verbs;
    private readonly double[]? _coords;

    // Arena mode: single byte array containing verbs followed by coords
    private readonly byte[]? _arena;
    private readonly int _verbOffset;
    private readonly int _coordOffset;
    private readonly int _coordCount;

    public int VerbCount { get; }

    public BezPath(byte[] verbs, double[] coords, int verbCount)
    {
        _verbs = verbs;
        _coords = coords;
        _arena = null;
        _verbOffset = 0;
        _coordOffset = 0;
        _coordCount = coords?.Length ?? 0;
        VerbCount = verbCount;
    }

    public BezPath(byte[] arena, int verbOffset, int verbCount, int coordOffset, int coordCount)
    {
        _verbs = null;
        _coords = null;
        _arena = arena;
        _verbOffset = verbOffset;
        _coordOffset = coordOffset;
        _coordCount = coordCount;
        VerbCount = verbCount;
    }

    public bool IsEmpty => VerbCount == 0;

    private ReadOnlySpan<byte> GetVerbSpan()
    {
        if (_arena != null)
        {
            return _arena.AsSpan(_verbOffset, VerbCount);
        }
        return _verbs.AsSpan(0, VerbCount);
    }

    private ReadOnlySpan<double> GetCoordSpan()
    {
        if (_arena != null)
        {
            return MemoryMarshal.Cast<byte, double>(_arena.AsSpan(_coordOffset, _coordCount * 8));
        }
        return _coords.AsSpan();
    }

    public BezPathEnumerator Iterate() => new BezPathEnumerator(GetVerbSpan(), GetCoordSpan(), VerbCount);

    public Rect Aabb()
    {
        if (IsEmpty) return new Rect(0, 0, 0, 0);

        ReadOnlySpan<byte> verbs = GetVerbSpan();
        ReadOnlySpan<double> coords = GetCoordSpan();

        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double maxY = double.NegativeInfinity;

        int coordIdx = 0;
        Point current = new Point(0, 0);
        Point pathStart = current;

        for (int i = 0; i < VerbCount; i++)
        {
            PathVerb verb = (PathVerb)verbs[i];
            switch (verb)
            {
                case PathVerb.MoveTo:
                    current = new Point(coords[coordIdx], coords[coordIdx + 1]);
                    pathStart = current;
                    coordIdx += 2;
                    minX = Math.Min(minX, current.X);
                    minY = Math.Min(minY, current.Y);
                    maxX = Math.Max(maxX, current.X);
                    maxY = Math.Max(maxY, current.Y);
                    break;
                case PathVerb.LineTo:
                    {
                        Point end = new Point(coords[coordIdx], coords[coordIdx + 1]);
                        coordIdx += 2;
                        Rect segAabb = Rect.FromPoints(current, end);
                        minX = Math.Min(minX, segAabb.MinX);
                        minY = Math.Min(minY, segAabb.MinY);
                        maxX = Math.Max(maxX, segAabb.MaxX);
                        maxY = Math.Max(maxY, segAabb.MaxY);
                        current = end;
                        break;
                    }
                case PathVerb.QuadTo:
                    {
                        Point ctrl = new Point(coords[coordIdx], coords[coordIdx + 1]);
                        Point end = new Point(coords[coordIdx + 2], coords[coordIdx + 3]);
                        coordIdx += 4;
                        QuadBez quad = new QuadBez(current, ctrl, end);
                        Rect qAabb = quad.Aabb();
                        minX = Math.Min(minX, qAabb.MinX);
                        minY = Math.Min(minY, qAabb.MinY);
                        maxX = Math.Max(maxX, qAabb.MaxX);
                        maxY = Math.Max(maxY, qAabb.MaxY);
                        current = end;
                        break;
                    }
                case PathVerb.CubicTo:
                    {
                        Point c1 = new Point(coords[coordIdx], coords[coordIdx + 1]);
                        Point c2 = new Point(coords[coordIdx + 2], coords[coordIdx + 3]);
                        Point end = new Point(coords[coordIdx + 4], coords[coordIdx + 5]);
                        coordIdx += 6;
                        CubicBez cubic = new CubicBez(current, c1, c2, end);
                        Rect cAabb = cubic.Aabb();
                        minX = Math.Min(minX, cAabb.MinX);
                        minY = Math.Min(minY, cAabb.MinY);
                        maxX = Math.Max(maxX, cAabb.MaxX);
                        maxY = Math.Max(maxY, cAabb.MaxY);
                        current = end;
                        break;
                    }
                case PathVerb.Close:
                    current = pathStart;
                    break;
            }
        }

        if (minX == double.PositiveInfinity)
            return new Rect(0, 0, 0, 0);
        return new Rect(minX, minY, maxX, maxY);
    }

    public BezPath TransformedBy(Affine a)
    {
        if (IsEmpty) return this;

        ReadOnlySpan<byte> verbs = GetVerbSpan();
        ReadOnlySpan<double> coords = GetCoordSpan();

        byte[] newVerbs = new byte[VerbCount];
        double[] newCoords = new double[coords.Length];
        verbs.Slice(0, VerbCount).CopyTo(newVerbs);

        int coordIdx = 0;
        Point current = new Point(0, 0);

        for (int i = 0; i < VerbCount; i++)
        {
            PathVerb verb = (PathVerb)verbs[i];
            switch (verb)
            {
                case PathVerb.MoveTo:
                    {
                        Point p = new Point(coords[coordIdx], coords[coordIdx + 1]);
                        Point tp = a * p;
                        newCoords[coordIdx] = tp.X;
                        newCoords[coordIdx + 1] = tp.Y;
                        coordIdx += 2;
                        current = tp;
                        break;
                    }
                case PathVerb.LineTo:
                    {
                        Point end = new Point(coords[coordIdx], coords[coordIdx + 1]);
                        Point tend = a * end;
                        newCoords[coordIdx] = tend.X;
                        newCoords[coordIdx + 1] = tend.Y;
                        coordIdx += 2;
                        current = tend;
                        break;
                    }
                case PathVerb.QuadTo:
                    {
                        Point ctrl = new Point(coords[coordIdx], coords[coordIdx + 1]);
                        Point end = new Point(coords[coordIdx + 2], coords[coordIdx + 3]);
                        Point tctrl = a * ctrl;
                        Point tend = a * end;
                        newCoords[coordIdx] = tctrl.X; newCoords[coordIdx + 1] = tctrl.Y;
                        newCoords[coordIdx + 2] = tend.X; newCoords[coordIdx + 3] = tend.Y;
                        coordIdx += 4;
                        current = tend;
                        break;
                    }
                case PathVerb.CubicTo:
                    {
                        Point c1 = new Point(coords[coordIdx], coords[coordIdx + 1]);
                        Point c2 = new Point(coords[coordIdx + 2], coords[coordIdx + 3]);
                        Point end = new Point(coords[coordIdx + 4], coords[coordIdx + 5]);
                        Point tc1 = a * c1;
                        Point tc2 = a * c2;
                        Point tend = a * end;
                        newCoords[coordIdx] = tc1.X; newCoords[coordIdx + 1] = tc1.Y;
                        newCoords[coordIdx + 2] = tc2.X; newCoords[coordIdx + 3] = tc2.Y;
                        newCoords[coordIdx + 4] = tend.X; newCoords[coordIdx + 5] = tend.Y;
                        coordIdx += 6;
                        current = tend;
                        break;
                    }
                case PathVerb.Close:
                    break;
            }
        }

        return new BezPath(newVerbs, newCoords, VerbCount);
    }
}

public ref struct BezPathBuilder
{
    private byte[] _verbs;
    private double[] _coords;
    private int _verbCount;
    private int _coordCount;
    private bool _built;
    private bool _hasMoveTo;
    private Point _current;

    public static BezPathBuilder Begin(int estimatedVerbs = 64)
    {
        return new BezPathBuilder
        {
            _verbs = ArrayPool<byte>.Shared.Rent(estimatedVerbs),
            _coords = ArrayPool<double>.Shared.Rent(estimatedVerbs * 6),
            _verbCount = 0,
            _coordCount = 0,
            _built = false,
            _hasMoveTo = false,
            _current = new Point(0, 0),
        };
    }

    public Point Current => _current;

    public int VerbCount => _verbCount;

    public void MoveTo(Point p)
    {
        EnsureNotBuilt();
        EnsureCapacity(2);
        _verbs[_verbCount++] = (byte)PathVerb.MoveTo;
        _coords[_coordCount++] = p.X;
        _coords[_coordCount++] = p.Y;
        _current = p;
        _hasMoveTo = true;
    }

    public void LineTo(Point p)
    {
        EnsureHasMoveTo();
        EnsureCapacity(2);
        _verbs[_verbCount++] = (byte)PathVerb.LineTo;
        _coords[_coordCount++] = p.X;
        _coords[_coordCount++] = p.Y;
        _current = p;
    }

    public void QuadTo(Point control, Point end)
    {
        EnsureHasMoveTo();
        EnsureCapacity(4);
        _verbs[_verbCount++] = (byte)PathVerb.QuadTo;
        _coords[_coordCount++] = control.X;
        _coords[_coordCount++] = control.Y;
        _coords[_coordCount++] = end.X;
        _coords[_coordCount++] = end.Y;
        _current = end;
    }

    public void CubicTo(Point control1, Point control2, Point end)
    {
        EnsureHasMoveTo();
        EnsureCapacity(6);
        _verbs[_verbCount++] = (byte)PathVerb.CubicTo;
        _coords[_coordCount++] = control1.X;
        _coords[_coordCount++] = control1.Y;
        _coords[_coordCount++] = control2.X;
        _coords[_coordCount++] = control2.Y;
        _coords[_coordCount++] = end.X;
        _coords[_coordCount++] = end.Y;
        _current = end;
    }

    public void Close()
    {
        EnsureHasMoveTo();
        EnsureCapacity(0);
        _verbs[_verbCount++] = (byte)PathVerb.Close;
    }

    public BezPath Build()
    {
        EnsureNotBuilt();
        _built = true;
        byte[] verbsCopy = new byte[_verbCount];
        double[] coordsCopy = new double[_coordCount];
        Array.Copy(_verbs, verbsCopy, _verbCount);
        Array.Copy(_coords, coordsCopy, _coordCount);
        return new BezPath(verbsCopy, coordsCopy, _verbCount);
    }

    public void Dispose()
    {
        if (_verbs != null)
        {
            ArrayPool<byte>.Shared.Return(_verbs);
            ArrayPool<double>.Shared.Return(_coords);
            _verbs = null!;
            _coords = null!;
        }
    }

    private void EnsureNotBuilt()
    {
        if (_built)
        {
            Etch.Panic.Invariant(
                Etch.PanicCodes.BuilderConsumed,
                "BezierPathBuilder.Build() has already been called on this builder.");
        }
    }

    private void EnsureHasMoveTo()
    {
        if (!_hasMoveTo)
        {
            Etch.Panic.Invariant(
                Etch.PanicCodes.PathVerbWithoutMoveTo,
                "A LineTo/QuadTo/CubicTo/Close was emitted before any MoveTo.");
        }
    }

    private void EnsureCapacity(int coordSlots)
    {
        if (_verbCount >= _verbs.Length)
        {
            byte[] bigger = ArrayPool<byte>.Shared.Rent(_verbs.Length * 2);
            Array.Copy(_verbs, bigger, _verbCount);
            ArrayPool<byte>.Shared.Return(_verbs);
            _verbs = bigger;
        }
        if (_coordCount + coordSlots > _coords.Length)
        {
            double[] bigger = ArrayPool<double>.Shared.Rent(_coords.Length * 2);
            Array.Copy(_coords, bigger, _coordCount);
            ArrayPool<double>.Shared.Return(_coords);
            _coords = bigger;
        }
    }
}

public ref struct BezPathEnumerator
{
    private readonly ReadOnlySpan<byte> _verbs;
    private readonly ReadOnlySpan<double> _coords;
    private readonly int _count;
    private int _index;
    private int _coordIdx;
    private int _segmentStartIdx;
    private Point _segmentStart;
    private Point _lastEnd;

    public BezPathEnumerator(ReadOnlySpan<byte> verbs, ReadOnlySpan<double> coords, int count)
    {
        _verbs = verbs;
        _coords = coords;
        _count = count;
        _index = -1;
        _coordIdx = 0;
        _segmentStartIdx = 0;
        _segmentStart = new Point(double.NaN, double.NaN);
        _lastEnd = new Point(double.NaN, double.NaN);
    }

    public PathSegment Current => new PathSegment(_verbs[_index], _index, _segmentStartIdx, _coords, _segmentStart);

    public bool MoveNext()
    {
        if (_index >= _count - 1) return false;
        _index++;
        PathVerb verb = (PathVerb)_verbs[_index];

        _segmentStartIdx = _coordIdx;
        _segmentStart = _lastEnd;
        _UpdateLastEnd(verb, _coordIdx, ref _lastEnd);

        _coordIdx += PathVerbData.CoordCount(verb);
        return true;
    }

    private void _UpdateLastEnd(PathVerb verb, int coordIdx, ref Point lastEnd)
    {
        int maxIdx = _coords.Length;
        switch (verb)
        {
            case PathVerb.MoveTo:
                if (coordIdx + 2 <= maxIdx)
                {
                    lastEnd = new Point(_coords[coordIdx], _coords[coordIdx + 1]);
                }
                break;
            case PathVerb.LineTo:
                if (coordIdx + 2 <= maxIdx)
                {
                    lastEnd = new Point(_coords[coordIdx], _coords[coordIdx + 1]);
                }
                break;
            case PathVerb.QuadTo:
                if (coordIdx + 4 <= maxIdx)
                {
                    lastEnd = new Point(_coords[coordIdx + 2], _coords[coordIdx + 3]);
                }
                break;
            case PathVerb.CubicTo:
                if (coordIdx + 6 <= maxIdx)
                {
                    lastEnd = new Point(_coords[coordIdx + 4], _coords[coordIdx + 5]);
                }
                break;
            case PathVerb.Close:
                lastEnd = new Point(double.NaN, double.NaN);
                break;
        }
    }

    public BezPathEnumerator GetEnumerator() => this;
}

public readonly ref struct PathSegment
{
    private readonly byte _verb;
    private readonly int _startCoordIdx;
    private readonly ReadOnlySpan<double> _coords;
    private readonly Point _startPoint;

    public PathSegment(byte verb, int verbIndex, int startCoordIdx, ReadOnlySpan<double> coords, Point startPoint)
    {
        _verb = verb;
        _startCoordIdx = startCoordIdx;
        _coords = coords;
        _startPoint = startPoint;
    }

    public PathVerb Verb => (PathVerb)_verb;

    public Point Start
    {
        get
        {
            if (Verb == PathVerb.MoveTo) return new Point(double.NaN, double.NaN);
            return _startPoint;
        }
    }

    public Point End
    {
        get
        {
            int cc = PathVerbData.CoordCount(Verb);
            if (cc == 0) return new Point(double.NaN, double.NaN);
            if (_startCoordIdx + cc > _coords.Length) return new Point(double.NaN, double.NaN);
            return new Point(_coords[_startCoordIdx + cc - 2], _coords[_startCoordIdx + cc - 1]);
        }
    }

    public Point Control0
    {
        get
        {
            if (Verb == PathVerb.QuadTo || Verb == PathVerb.CubicTo)
            {
                if (_startCoordIdx + 2 > _coords.Length) return new Point(double.NaN, double.NaN);
                return new Point(_coords[_startCoordIdx], _coords[_startCoordIdx + 1]);
            }
            return new Point(double.NaN, double.NaN);
        }
    }

    public Point Control1
    {
        get
        {
            if (Verb == PathVerb.CubicTo)
            {
                if (_startCoordIdx + 4 > _coords.Length) return new Point(double.NaN, double.NaN);
                return new Point(_coords[_startCoordIdx + 2], _coords[_startCoordIdx + 3]);
            }
            return new Point(double.NaN, double.NaN);
        }
    }
}
