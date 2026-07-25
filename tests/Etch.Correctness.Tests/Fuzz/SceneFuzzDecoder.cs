using System;
using Etch.Geometry;
using Etch.Scene;

namespace Etch.Correctness.Tests.Fuzz;

/// <summary>
/// Decodes a raw byte span into a <see cref="SceneBuffer"/> for fuzz testing.
/// The encoding is deterministic and compact: each byte drives a decision,
/// so every possible input maps to a valid scene (balanced clips, valid IDs).
/// </summary>
internal static class SceneFuzzDecoder
{
    private const int MaxPaths = 4;
    private const int MaxPaints = 4;
    private const int MaxTransforms = 4;
    private const int MaxCommands = 16;
    private const double CoordScale = 64.0;

    public static SceneBuffer Decode(ReadOnlySpan<byte> input)
    {
        var builder = SceneBuilder.Begin();

        if (input.Length < 4)
        {
            // Minimal fallback: single solid rect
            int paintId = builder.AddPaint(Paint.Solid(0xFF0000FF));
            int transformId = builder.AddTransform(Geometry.Affine.Identity);
            builder.FillRect(new Rect(0, 0, 10, 10), paintId, transformId);
            return builder.End();
        }

        int readIdx = 0;

        // --- Paths ---
        int pathCount = SafeByte(input, ref readIdx) % (MaxPaths + 1);
        Span<int> paths = stackalloc int[pathCount];
        for (int i = 0; i < pathCount; i++)
        {
            paths[i] = builder.AddPath(BuildPath(input, ref readIdx));
        }

        // --- Paints ---
        int paintCount = Math.Max(1, SafeByte(input, ref readIdx) % (MaxPaints + 1));
        Span<int> paints = stackalloc int[paintCount];
        for (int i = 0; i < paintCount; i++)
        {
            uint color = ReadColor(input, ref readIdx);
            paints[i] = builder.AddPaint(Paint.Solid(color));
        }

        // --- Transforms ---
        int transformCount = Math.Max(1, SafeByte(input, ref readIdx) % (MaxTransforms + 1));
        Span<int> transforms = stackalloc int[transformCount];
        for (int i = 0; i < transformCount; i++)
        {
            transforms[i] = builder.AddTransform(ReadTransform(input, ref readIdx));
        }

        // --- Commands ---
        int commandCount = Math.Max(1, SafeByte(input, ref readIdx) % (MaxCommands + 1));
        int clipDepth = 0;

        for (int cmd = 0; cmd < commandCount && readIdx < input.Length; cmd++)
        {
            int verb = SafeByte(input, ref readIdx) % 7;

            switch (verb)
            {
                case 0: // FillRect
                    if (paints.Length > 0 && transforms.Length > 0)
                    {
                        var rect = ReadRect(input, ref readIdx);
                        int paintIdx = SafeByte(input, ref readIdx) % paints.Length;
                        int transformIdx = SafeByte(input, ref readIdx) % transforms.Length;
                        builder.FillRect(rect, paints[paintIdx], transforms[transformIdx]);
                    }
                    break;

                case 1: // FillPath
                    if (paths.Length > 0 && paints.Length > 0 && transforms.Length > 0)
                    {
                        int pathIdx = SafeByte(input, ref readIdx) % paths.Length;
                        int paintIdx = SafeByte(input, ref readIdx) % paints.Length;
                        int transformIdx = SafeByte(input, ref readIdx) % transforms.Length;
                        FillRule rule = (SafeByte(input, ref readIdx) & 1) == 0 ? FillRule.NonZero : FillRule.EvenOdd;
                        builder.FillPath(paths[pathIdx], paints[paintIdx], transforms[transformIdx], rule);
                    }
                    break;

                case 2: // PushClip
                    if (paths.Length > 0 && clipDepth < 16)
                    {
                        int pathIdx = SafeByte(input, ref readIdx) % paths.Length;
                        FillRule rule = (SafeByte(input, ref readIdx) & 1) == 0 ? FillRule.NonZero : FillRule.EvenOdd;
                        builder.PushClip(paths[pathIdx], rule);
                        clipDepth++;
                    }
                    break;

                case 3: // PopClip
                    if (clipDepth > 0)
                    {
                        builder.PopClip();
                        clipDepth--;
                    }
                    break;

                case 4: // SetTransform
                    if (transforms.Length > 0)
                    {
                        int transformIdx = SafeByte(input, ref readIdx) % transforms.Length;
                        builder.SetTransform(transforms[transformIdx]);
                    }
                    break;

                case 5: // DrawShadow
                    if (paths.Length > 0 && paints.Length > 0 && transforms.Length > 0)
                    {
                        int pathIdx = SafeByte(input, ref readIdx) % paths.Length;
                        int paintIdx = SafeByte(input, ref readIdx) % paints.Length;
                        int transformIdx = SafeByte(input, ref readIdx) % transforms.Length;
                        double offsetX = ReadCoord(input, ref readIdx);
                        double offsetY = ReadCoord(input, ref readIdx);
                        float blurRadius = (float)Math.Abs(ReadCoord(input, ref readIdx));
                        uint shadowColor = ReadColor(input, ref readIdx);
                        builder.DrawShadow(paths[pathIdx], paints[paintIdx], transforms[transformIdx],
                            new Vec2(offsetX, offsetY), blurRadius, shadowColor);
                    }
                    break;

                case 6: // StrokePath
                    if (paths.Length > 0 && paints.Length > 0 && transforms.Length > 0)
                    {
                        int pathIdx = SafeByte(input, ref readIdx) % paths.Length;
                        int paintIdx = SafeByte(input, ref readIdx) % paints.Length;
                        int transformIdx = SafeByte(input, ref readIdx) % transforms.Length;
                        float width = Math.Max(0.5f, (float)Math.Abs(ReadCoord(input, ref readIdx)));
                        var style = new StrokeStyle();
                        builder.StrokePath(paths[pathIdx], paints[paintIdx], transforms[transformIdx], width, style);
                    }
                    break;
            }
        }

        // Balance clip stack
        while (clipDepth > 0)
        {
            builder.PopClip();
            clipDepth--;
        }

        return builder.End();
    }

    private static BezPath BuildPath(ReadOnlySpan<byte> input, ref int readIdx)
    {
        // Reuse the BezPathFuzzDecoder with a sub-slice of bytes
        int len = input.Length - readIdx;
        if (len <= 0)
        {
            using var b = BezPathBuilder.Begin();
            b.MoveTo(new Point(0, 0));
            b.LineTo(new Point(10, 0));
            b.LineTo(new Point(10, 10));
            b.Close();
            return b.Build();
        }

        int consume = Math.Min(len, 64);
        var sub = input.Slice(readIdx, consume);
        readIdx += consume;
        return Differential.BezPathFuzzDecoder.Decode(sub);
    }

    private static Rect ReadRect(ReadOnlySpan<byte> input, ref int readIdx)
    {
        double x0 = ReadCoord(input, ref readIdx);
        double y0 = ReadCoord(input, ref readIdx);
        double x1 = ReadCoord(input, ref readIdx);
        double y1 = ReadCoord(input, ref readIdx);
        double minX = Math.Min(x0, x1);
        double minY = Math.Min(y0, y1);
        double maxX = Math.Max(x0, x1);
        double maxY = Math.Max(y0, y1);
        // Ensure non-degenerate
        if (maxX <= minX) maxX = minX + 1.0;
        if (maxY <= minY) maxY = minY + 1.0;
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private static Affine ReadTransform(ReadOnlySpan<byte> input, ref int readIdx)
    {
        int kind = SafeByte(input, ref readIdx) % 4;
        switch (kind)
        {
            case 0: return Affine.Identity;
            case 1:
                double tx = ReadCoord(input, ref readIdx);
                double ty = ReadCoord(input, ref readIdx);
                return Affine.Translate(tx, ty);
            case 2:
                double sx = ReadScale(input, ref readIdx);
                double sy = ReadScale(input, ref readIdx);
                return Affine.Scale(sx, sy);
            case 3:
                double angle = ReadCoord(input, ref readIdx);
                return Affine.Rotate(angle);
            default:
                return Affine.Identity;
        }
    }

    private static uint ReadColor(ReadOnlySpan<byte> input, ref int readIdx)
    {
        if (readIdx + 4 > input.Length)
        {
            readIdx += 4;
            return 0xFF000000;
        }
        uint a = input[readIdx++];
        uint r = input[readIdx++];
        uint g = input[readIdx++];
        uint b = input[readIdx++];
        return (a << 24) | (r << 16) | (g << 8) | b;
    }

    private static double ReadCoord(ReadOnlySpan<byte> input, ref int readIdx)
    {
        if (readIdx >= input.Length)
        {
            readIdx++;
            return 0.0;
        }
        byte b = input[readIdx++];
        double normalized = (b / 255.0) * 2.0 - 1.0;
        return normalized * CoordScale;
    }

    private static double ReadScale(ReadOnlySpan<byte> input, ref int readIdx)
    {
        byte b = SafeByte(input, ref readIdx, 128);
        // Map 0..255 to 0.1 .. 3.0
        return 0.1 + (b / 255.0) * 2.9;
    }

    private static byte SafeByte(ReadOnlySpan<byte> input, ref int readIdx, byte fallback = 0)
    {
        if (readIdx >= input.Length)
        {
            readIdx++;
            return fallback;
        }
        return input[readIdx++];
    }
}
