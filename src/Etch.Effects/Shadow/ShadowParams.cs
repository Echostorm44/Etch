using System;
using Etch.Geometry;

namespace Etch.Effects.Shadow;

public readonly struct ShadowParams
{
    public readonly Vec2 Offset;
    public readonly float BlurRadius;
    public readonly uint ShadowColor;

    public ShadowParams(Vec2 offset, float blurRadius, uint shadowColor = 0x40000000)
    {
        Offset = offset;
        BlurRadius = blurRadius;
        ShadowColor = shadowColor;
    }
}
