using System;
using Etch.Geometry;

namespace Etch.ClipBlendGradient;

public readonly struct LinearGradient
{
    public readonly Vec2 Start;
    public readonly Vec2 End;
    public readonly GradientStop[] Stops;
    public readonly GradientExtend Extend;
    public readonly GradientInterpolationSpace InterpolationSpace;

    public LinearGradient(Vec2 start, Vec2 end, GradientStop[] stops, GradientExtend extend, GradientInterpolationSpace interpolationSpace)
    {
        Start = start;
        End = end;
        Stops = stops;
        Extend = extend;
        InterpolationSpace = interpolationSpace;
    }

    public LinearGradient(double startX, double startY, double endX, double endY, GradientStop[] stops, GradientExtend extend, GradientInterpolationSpace interpolationSpace)
        : this(new Vec2(startX, startY), new Vec2(endX, endY), stops, extend, interpolationSpace)
    {
    }
}
