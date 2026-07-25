using System;
using Etch.Geometry;

namespace Etch.ClipBlendGradient;

public readonly struct ConicGradient
{
    public readonly Vec2 Center;
    public readonly float StartAngleRad;
    public readonly GradientStop[] Stops;
    public readonly GradientInterpolationSpace InterpolationSpace;

    public ConicGradient(Vec2 center, float startAngleRad, GradientStop[] stops, GradientInterpolationSpace interpolationSpace)
    {
        Center = center;
        StartAngleRad = startAngleRad;
        Stops = stops;
        InterpolationSpace = interpolationSpace;
    }

    public ConicGradient(double centerX, double centerY, float startAngleRad, GradientStop[] stops, GradientInterpolationSpace interpolationSpace)
        : this(new Vec2(centerX, centerY), startAngleRad, stops, interpolationSpace)
    {
    }
}
