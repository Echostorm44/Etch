namespace Etch.Effects.Blur;

public enum BlurEdge
{
    Clamp = 0,
}

public readonly struct BlurParams
{
    public readonly float RadiusPx;
    public readonly BlurEdge Edge;

    public BlurParams(float radiusPx, BlurEdge edge = BlurEdge.Clamp)
    {
        RadiusPx = radiusPx;
        Edge = edge;
    }
}
