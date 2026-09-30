using System;
using System.Buffers;
using Etch.Geometry;

namespace Etch.Scene.Damage;

public static class ScrollDetector
{
    private const double DefaultCoverageThreshold = 0.95;

    public static ScrollHint Detect(SceneBuffer prev, SceneBuffer curr, Rect viewport)
    {
        return DetectWithThreshold(prev, curr, viewport, DefaultCoverageThreshold);
    }

    public static ScrollHint DetectWithThreshold(SceneBuffer prev, SceneBuffer curr, Rect viewport, double coverageThreshold)
    {
#pragma warning disable CA1062
        if (prev.CommandCount == 0 || curr.CommandCount == 0)
            return ScrollHint.None;

        int matchedCount = 0;
        Vec2? commonDelta = null;
        int commonDeltaCount = 0;

        int prevIdx = 0;
        int currIdx = 0;

        while (prevIdx < prev.CommandCount && currIdx < curr.CommandCount)
        {
            ref readonly var prevCmd = ref prev.Commands[prevIdx];
            ref readonly var currCmd = ref curr.Commands[currIdx];

            int cmp = CompareCommandSignatures(prevCmd, currCmd);
            if (cmp == 0)
            {
                int transformId = GetTransformId(prevCmd);
                if (transformId >= 0)
                {
                    var prevTransform = prev.GetTransform(transformId);
                    var currTransform = curr.GetTransform(transformId);

                    var delta = GetTransformDelta(prevTransform, currTransform);

                    if (delta.HasValue)
                    {
                        matchedCount++;
                        if (!commonDelta.HasValue)
                        {
                            commonDelta = delta.Value;
                            commonDeltaCount = 1;
                        }
                        else if (DeltasMatch(commonDelta.Value, delta.Value))
                        {
                            commonDeltaCount++;
                        }
                        else if (commonDeltaCount == 1)
                        {
                            commonDelta = delta.Value;
                            commonDeltaCount = 1;
                        }
                        else
                        {
                            commonDeltaCount--;
                        }
                    }
                }

                prevIdx++;
                currIdx++;
            }
            else if (cmp < 0)
            {
                prevIdx++;
            }
            else
            {
                currIdx++;
            }
        }

        if (!commonDelta.HasValue || matchedCount == 0)
            return ScrollHint.None;

        double coverage = (double)matchedCount / Math.Max(prev.CommandCount, curr.CommandCount);
        if (coverage < coverageThreshold)
            return ScrollHint.None;

        var revealedStrip = ComputeRevealedStrip(viewport, commonDelta.Value);
        return new ScrollHint(true, commonDelta.Value, matchedCount, coverage);
#pragma warning restore CA1062
    }

    private static int CompareCommandSignatures(SceneCommand a, SceneCommand b)
    {
        int aSig = GetCommandSignature(a);
        int bSig = GetCommandSignature(b);
        return aSig.CompareTo(bSig);
    }

    private static int GetCommandSignature(SceneCommand cmd)
    {
        int sig = (int)cmd.Op * 10000;
        switch (cmd.Op)
        {
            case SceneOpcode.FillPath:
                sig += cmd.FillPath.PathId * 100 + cmd.FillPath.PaintId;
                break;
            case SceneOpcode.StrokePath:
                sig += cmd.StrokePath.PathId * 100 + cmd.StrokePath.PaintId;
                break;
            case SceneOpcode.FillRect:
                sig += cmd.FillRect.RectId * 100 + cmd.FillRect.PaintId;
                break;
            default:
                break;
        }
        return sig;
    }

    private const double LinearTermTolerance = 0.001;

    // A scroll is a pure integer translation: both transforms must have an identity linear part
    // (unit scale, no rotation or shear), leaving only the translation terms to differ.
    private static Vec2? GetTransformDelta(Affine prevTransform, Affine currTransform)
    {
        if (!HasIdentityLinearPart(prevTransform) || !HasIdentityLinearPart(currTransform))
            return null;

        double dx = currTransform.M02 - prevTransform.M02;
        double dy = currTransform.M12 - prevTransform.M12;

        if (Math.Abs(dx) < LinearTermTolerance && Math.Abs(dy) < LinearTermTolerance)
            return null;

        int intDx = (int)Math.Round(dx);
        int intDy = (int)Math.Round(dy);

        if (Math.Abs(dx - intDx) > LinearTermTolerance || Math.Abs(dy - intDy) > LinearTermTolerance)
            return null;

        return new Vec2(intDx, intDy);
    }

    private static bool HasIdentityLinearPart(Affine transform)
    {
        return Math.Abs(transform.M00 - 1.0) <= LinearTermTolerance
            && Math.Abs(transform.M11 - 1.0) <= LinearTermTolerance
            && Math.Abs(transform.M01) <= LinearTermTolerance
            && Math.Abs(transform.M10) <= LinearTermTolerance;
    }

    private static int GetTransformId(SceneCommand cmd)
    {
        switch (cmd.Op)
        {
            case SceneOpcode.FillPath:
                return cmd.FillPath.TransformId;
            case SceneOpcode.StrokePath:
                return cmd.StrokePath.TransformId;
            case SceneOpcode.FillRect:
                return cmd.FillRect.TransformId;
            default:
                return -1;
        }
    }

    private static bool DeltasMatch(Vec2 expected, Vec2 actual)
    {
        return expected.X == actual.X && expected.Y == actual.Y;
    }

    private static Rect ComputeRevealedStrip(Rect viewport, Vec2 delta)
    {
        double minX = viewport.MinX;
        double minY = viewport.MinY;
        double maxX = viewport.MaxX;
        double maxY = viewport.MaxY;

        double scrollX = delta.X;
        double scrollY = delta.Y;

        if (scrollY > 0)
        {
            minY = viewport.MinY;
            maxY = viewport.MinY + scrollY;
        }
        else if (scrollY < 0)
        {
            minY = viewport.MaxY + scrollY;
            maxY = viewport.MaxY;
        }

        if (scrollX > 0)
        {
            minX = viewport.MinX;
            maxX = viewport.MinX + scrollX;
        }
        else if (scrollX < 0)
        {
            minX = viewport.MaxX + scrollX;
            maxX = viewport.MaxX;
        }

        return Rect.FromLTRB(minX, minY, maxX, maxY);
    }
}