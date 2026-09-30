using System;
using System.Threading.Tasks;

namespace Etch.Scene.Tests;

internal sealed class PaintColorTests
{
    [Test]
    public async Task EveryByte_RoundTripsThroughLinear()
    {
        // 8-bit sRGB round-trips exactly; 8-bit linear could not represent sRGB 1..12 at all.
        for (int v = 0; v < 256; v++)
        {
            uint argb = 0xFF000000u | ((uint)v << 16) | ((uint)v << 8) | (uint)v;
            var (r, g, b, a) = PaintColor.ToLinear(argb);
            await Assert.That(PaintColor.FromLinear(r, g, b, a)).IsEqualTo(argb);
        }
    }

    [Test]
    public async Task Encoding_IsSrgbWithStraightAlpha()
    {
        // Linear 0.5 is sRGB 188 (0xBC); alpha is stored as-is.
        await Assert.That(PaintColor.FromLinear(0.5f, 0f, 1f, 0.5f)).IsEqualTo(0x80BC00FFu);
        var (r, _, _, a) = PaintColor.ToLinear(0x80BC00FFu);
        await Assert.That(Math.Abs(r - 0.5f)).IsLessThan(0.003f);
        await Assert.That(Math.Abs(a - 128 / 255f)).IsLessThan(1e-6f);
    }
}
