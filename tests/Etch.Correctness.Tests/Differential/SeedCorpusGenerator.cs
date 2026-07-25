using System;
using System.IO;
using Etch.Correctness.Tests.Differential;

namespace Etch.Correctness.Tests.Differential;

internal static class SeedCorpusGenerator
{
    public static void GenerateAll(string outputDir)
    {
        Directory.CreateDirectory(outputDir);

        // Single cubic curve
        WriteSeed(Path.Combine(outputDir, "single_cubic.bin"), new byte[]
        {
            1,  // 1 segment
            3,  // CubicTo
            128, 128, 0, 0, 128, 128, 0, 0, 128, 128, 0, 0, // c1, c2, end
        });

        // Single quad curve
        WriteSeed(Path.Combine(outputDir, "single_quad.bin"), new byte[]
        {
            1,  // 1 segment
            2,  // QuadTo
            128, 128, 0, 0, 128, 128, // control, end
        });

        // Multiple segments
        WriteSeed(Path.Combine(outputDir, "multi_segment.bin"), new byte[]
        {
            4,  // 4 segments
            0,  // MoveTo
            64, 64, 0, 0, // point
            1,  // LineTo
            192, 192, 0, 0, // point
            2,  // QuadTo
            128, 64, 0, 0, 192, 64, 0, 0, // control, end
            3,  // CubicTo
            64, 128, 0, 0, 128, 192, 0, 0, 192, 128, 0, 0, // c1, c2, end
        });

        // Close without prior segments (should produce valid empty-ish path)
        WriteSeed(Path.Combine(outputDir, "close_first.bin"), new byte[]
        {
            1,  // 1 segment
            4,  // Close (guarded, should be no-op without MoveTo)
        });

        // MoveTo only
        WriteSeed(Path.Combine(outputDir, "move_only.bin"), new byte[]
        {
            1,  // 1 segment
            0,  // MoveTo
            128, 128, 0, 0,
        });

        // Deep nesting (many segments)
        var deep = new byte[128];
        deep[0] = 32; // 32 segments (max)
        for (int i = 1; i < deep.Length; i++)
        {
            deep[i] = (byte)(i % 5);
        }
        WriteSeed(Path.Combine(outputDir, "deep_path.bin"), deep);
    }

    private static void WriteSeed(string path, byte[] data)
    {
        File.WriteAllBytes(path, data);
    }
}
