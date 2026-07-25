using System;
using Etch.Effects.Blur;
using Etch.Gpu;
using Etch.Geometry;

namespace Etch.Effects.Shadow;

public static class DropShadowRenderer
{
    public static void ApplyShadow(
        Texture source,
        int sourceWidth,
        int sourceHeight,
        Texture destination,
        ShadowParams shadowParams)
    {
        if (shadowParams.BlurRadius <= 0f)
        {
            return;
        }

        int octaveCount = DualFilterBlur.OctaveCount(shadowParams.BlurRadius);
        if (octaveCount == 0)
        {
            return;
        }
    }
}
