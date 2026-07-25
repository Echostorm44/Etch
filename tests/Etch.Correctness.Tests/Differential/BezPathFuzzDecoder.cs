using System;
using System.Collections.Generic;
using Etch.Geometry;

namespace Etch.Correctness.Tests.Differential;

/// <summary>
/// Decodes a raw byte span into a <see cref="BezPath"/> for fuzz testing.
/// The encoding is deterministic and compact: each byte drives a decision,
/// so every possible input maps to a valid (though possibly degenerate) path.
/// </summary>
internal static class BezPathFuzzDecoder
{
    private const int MaxSegments = 32;
    private const double CoordScale = 256.0;

    public static BezPath Decode(ReadOnlySpan<byte> input)
    {
        using var builder = BezPathBuilder.Begin();

        if (input.Length < 2)
        {
            builder.MoveTo(new Point(0, 0));
            return builder.Build();
        }

        int segmentCount = input[0] % (MaxSegments + 1);
        int readIdx = 1;

        Point current = new Point(0, 0);
        bool hasCurrent = false;

        for (int seg = 0; seg < segmentCount && readIdx < input.Length; seg++)
        {
            int verb = input[readIdx++] % 5;

            switch (verb)
            {
                case 0: // MoveTo
                    current = ReadPoint(input, ref readIdx);
                    builder.MoveTo(current);
                    hasCurrent = true;
                    break;

                case 1: // LineTo
                    if (!hasCurrent)
                    {
                        builder.MoveTo(current);
                        hasCurrent = true;
                    }
                    current = ReadPoint(input, ref readIdx);
                    builder.LineTo(current);
                    break;

                case 2: // QuadTo
                    if (!hasCurrent)
                    {
                        builder.MoveTo(current);
                        hasCurrent = true;
                    }
                    Point qControl = ReadPoint(input, ref readIdx);
                    Point qEnd = ReadPoint(input, ref readIdx);
                    builder.QuadTo(qControl, qEnd);
                    current = qEnd;
                    break;

                case 3: // CubicTo
                    if (!hasCurrent)
                    {
                        builder.MoveTo(current);
                        hasCurrent = true;
                    }
                    Point c1 = ReadPoint(input, ref readIdx);
                    Point c2 = ReadPoint(input, ref readIdx);
                    Point cEnd = ReadPoint(input, ref readIdx);
                    builder.CubicTo(c1, c2, cEnd);
                    current = cEnd;
                    break;

                case 4: // Close
                    if (hasCurrent)
                    {
                        builder.Close();
                        hasCurrent = false;
                    }
                    break;
            }
        }

        if (!hasCurrent)
        {
            builder.MoveTo(new Point(0, 0));
        }

        return builder.Build();
    }

    private static Point ReadPoint(ReadOnlySpan<byte> input, ref int idx)
    {
        double x = ReadCoord(input, ref idx);
        double y = ReadCoord(input, ref idx);
        return new Point(x, y);
    }

    private static double ReadCoord(ReadOnlySpan<byte> input, ref int idx)
    {
        if (idx >= input.Length)
        {
            idx++;
            return 0.0;
        }

        byte b = input[idx++];
        // Map 0..255 to -1.0 .. 1.0, then scale
        double normalized = (b / 255.0) * 2.0 - 1.0;
        return normalized * CoordScale;
    }
}
