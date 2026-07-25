using System;
using Etch.Geometry;

namespace Etch.ClipBlendGradient;

public readonly struct SweepGradient
{
    public readonly Vec2 Center;
    public readonly float StartAngleRad;
    public readonly float EndAngleRad;
    public readonly GradientStop[] Stops;
    public readonly GradientInterpolationSpace InterpolationSpace;

    public SweepGradient(Vec2 center, float startAngleRad, float endAngleRad, GradientStop[] stops, GradientInterpolationSpace interpolationSpace)
    {
        if (endAngleRad <= startAngleRad)
            Etch.Panic.Invariant(Etch.PanicCodes.DegenerateSweepGradient, $"endAngle ({endAngleRad}) must be greater than startAngle ({startAngleRad})");

        Center = center;
        StartAngleRad = startAngleRad;
        EndAngleRad = endAngleRad;
        Stops = stops;
        InterpolationSpace = interpolationSpace;
    }

    public SweepGradient(double centerX, double centerY, float startAngleRad, float endAngleRad, GradientStop[] stops, GradientInterpolationSpace interpolationSpace)
        : this(new Vec2(centerX, centerY), startAngleRad, endAngleRad, stops, interpolationSpace)
    {
    }
}
