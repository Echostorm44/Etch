using System;
using Etch.Geometry;

namespace Etch.ClipBlendGradient;

public readonly struct RadialGradient
{
    public readonly Vec2 Center;
    public readonly float Radius;
    public readonly GradientStop[] Stops;
    public readonly GradientExtend Extend;
    public readonly GradientInterpolationSpace InterpolationSpace;

    public RadialGradient(Vec2 center, float radius, GradientStop[] stops, GradientExtend extend, GradientInterpolationSpace interpolationSpace)
    {
        if (radius <= 0)
            Etch.Panic.Invariant(Etch.PanicCodes.DegenerateRadialGradient, $"Radius must be positive, got {radius}");

        Center = center;
        Radius = radius;
        Stops = stops;
        Extend = extend;
        InterpolationSpace = interpolationSpace;
    }

    public RadialGradient(double centerX, double centerY, float radius, GradientStop[] stops, GradientExtend extend, GradientInterpolationSpace interpolationSpace)
        : this(new Vec2(centerX, centerY), radius, stops, extend, interpolationSpace)
    {
    }
}
