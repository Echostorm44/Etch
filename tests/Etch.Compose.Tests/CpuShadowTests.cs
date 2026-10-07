using Etch.Compose.Cpu;

namespace Etch.Compose.Tests;

/// <summary>The vectorized shadow rows equal the per-pixel shadow coverage bit for bit, on every path.</summary>
internal sealed class CpuShadowTests
{
    [Test]
    [Arguments(8)]
    [Arguments(4)]
    [Arguments(1)]
    public async Task RowsEqualThePerPixelFunction(int vectorWidth)
    {
        var random = new Random(1234);
        Span<float> row = stackalloc float[61];
        int mismatches = 0;
        for (int n = 0; n < 200; n++)
        {
            float scale = random.Next(4) switch { 0 => 1f, 1 => 1.25f, 2 => 1.5f, _ => 2f };
            float w = 10 + random.NextSingle() * 300;
            float h = 10 + random.NextSingle() * 200;
            float x = random.NextSingle() * 100;
            float y = random.NextSingle() * 100;
            var inst = new ShapeInstance
            {
                Type = ShapeType.Shadow,
                P0 = x,
                P1 = y,
                P2 = x + w,
                P3 = y + h,
                Q0 = random.NextSingle() * 30,
                Q1 = 0.5f + random.NextSingle() * 20,
                FrameA = 1f / scale,
                FrameD = 1f / scale,
                FrameTx = -random.NextSingle() * 3,
                FrameTy = -random.NextSingle() * 3,
                Scale = scale,
            };
            int py = random.Next(-40, (int)((y + h) * scale) + 40);
            int x0 = random.Next(-40, (int)((x + w) * scale));
            CpuShadow.Row(inst, py, x0, row, vectorWidth);
            for (int i = 0; i < row.Length; i++)
            {
                float expected = CpuShading.ShapeCoverage(inst, x0 + i + 0.5f, py + 0.5f, ReadOnlySpan<byte>.Empty);
                if (BitConverter.SingleToInt32Bits(expected) != BitConverter.SingleToInt32Bits(row[i]))
                {
                    mismatches++;
                }
            }
        }
        await Assert.That(mismatches).IsEqualTo(0);
    }
}
