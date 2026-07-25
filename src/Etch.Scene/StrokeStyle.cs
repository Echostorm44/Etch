using System;
using System.Runtime.InteropServices;

namespace Etch.Scene;

public enum StrokeCap
{
    Butt = 0,
    Round = 1,
    Square = 2,
}

public enum StrokeJoin
{
    Miter = 0,
    Round = 1,
    Bevel = 2,
}

[StructLayout(LayoutKind.Sequential, Size = 48)]
public readonly struct StrokeStyle
{
    public readonly float Width;
    public readonly float MiterLimit;
    public readonly StrokeCap Cap;
    public readonly StrokeJoin Join;
    public readonly byte HasDash;
    private readonly byte _align0, _align1, _align2, _align3, _align4, _align5;
    public readonly int DashCount;
    public readonly int DashPatternOffset;
    private readonly long _alignLong0, _alignLong1, _alignLong2;
}
