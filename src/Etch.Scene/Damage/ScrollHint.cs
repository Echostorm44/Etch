using Etch.Geometry;

namespace Etch.Scene.Damage;

public readonly struct ScrollHint
{
    public static ScrollHint None => default;

    public readonly bool IsScroll;
    public readonly Vec2 Delta;
    public readonly int MatchedCommandCount;
    public readonly double CoveragePercent;

    public ScrollHint(bool isScroll, Vec2 delta, int matchedCommandCount, double coveragePercent)
    {
        IsScroll = isScroll;
        Delta = delta;
        MatchedCommandCount = matchedCommandCount;
        CoveragePercent = coveragePercent;
    }
}