using System;
using Etch.Effects.Blur;
using Etch.Geometry;

namespace Etch.Effects.Shadow;

public static class DropShadow
{
    public static Rect ComputeShadowBounds(Rect contentBounds, Vec2 offset, float blurRadius)
    {
        double inflation = 3.0 * blurRadius;
        double left = contentBounds.MinX + offset.X - inflation;
        double top = contentBounds.MinY + offset.Y - inflation;
        double right = contentBounds.MaxX + offset.X + inflation;
        double bottom = contentBounds.MaxY + offset.Y + inflation;
        return new Rect(left, top, right, bottom);
    }

    public static int ComputeBlurOctaves(float blurRadius)
    {
        if (blurRadius <= 0f)
        {
            return 0;
        }
        return DualFilterBlur.OctaveCount(blurRadius);
    }
}
