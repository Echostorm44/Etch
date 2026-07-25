using Etch.Text;
using TUnit;

namespace Etch.Text.Tests;

internal sealed class QuantizedSubpixelTests
{
    [Test]
    public async Task QuantizeX_0_1_Quarter_Returns0()
    {
        await Assert.That(QuantizedSubpixel.QuantizeX(0.1f, SubpixelQuant.Quarter)).IsEqualTo((byte)0);
    }

    [Test]
    public async Task QuantizeX_0_13_Quarter_Returns1()
    {
        await Assert.That(QuantizedSubpixel.QuantizeX(0.13f, SubpixelQuant.Quarter)).IsEqualTo((byte)1);
    }

    [Test]
    public async Task QuantizeX_0_25_Quarter_Returns1()
    {
        // 0.25 * 4 = 1.0 -> rounds to 1
        await Assert.That(QuantizedSubpixel.QuantizeX(0.25f, SubpixelQuant.Quarter)).IsEqualTo((byte)1);
    }

    [Test]
    public async Task QuantizeX_0_375_Quarter_Returns2()
    {
        // 0.375 * 4 = 1.5 -> banker's round to even -> 2
        await Assert.That(QuantizedSubpixel.QuantizeX(0.375f, SubpixelQuant.Quarter)).IsEqualTo((byte)2);
    }

    [Test]
    public async Task QuantizeX_0_5_Quarter_Returns2()
    {
        // 0.5 * 4 = 2.0 -> rounds to 2
        await Assert.That(QuantizedSubpixel.QuantizeX(0.5f, SubpixelQuant.Quarter)).IsEqualTo((byte)2);
    }

    [Test]
    public async Task QuantizeX_0_625_Quarter_Returns2()
    {
        // 0.625 * 4 = 2.5 -> banker's round to even -> 2
        await Assert.That(QuantizedSubpixel.QuantizeX(0.625f, SubpixelQuant.Quarter)).IsEqualTo((byte)2);
    }

    [Test]
    public async Task QuantizeX_0_75_Quarter_Returns3()
    {
        // 0.75 * 4 = 3.0 -> rounds to 3
        await Assert.That(QuantizedSubpixel.QuantizeX(0.75f, SubpixelQuant.Quarter)).IsEqualTo((byte)3);
    }

    [Test]
    public async Task QuantizeX_0_875_Quarter_Returns0()
    {
        // 0.875 * 4 = 3.5 -> banker's round to even -> 4, clamped to 0
        await Assert.That(QuantizedSubpixel.QuantizeX(0.875f, SubpixelQuant.Quarter)).IsEqualTo((byte)0);
    }

    [Test]
    public async Task QuantizeX_0_0_Quarter_Returns0()
    {
        await Assert.That(QuantizedSubpixel.QuantizeX(0.0f, SubpixelQuant.Quarter)).IsEqualTo((byte)0);
    }

    [Test]
    public async Task QuantizeX_Negative0_3_SameAsPositive0_7()
    {
        byte neg = QuantizedSubpixel.QuantizeX(-0.3f, SubpixelQuant.Quarter);
        byte pos = QuantizedSubpixel.QuantizeX(0.7f, SubpixelQuant.Quarter);
        await Assert.That(neg).IsEqualTo(pos);
    }

    [Test]
    public async Task QuantizeX_Negative1_3_SameAsPositive0_7()
    {
        byte neg = QuantizedSubpixel.QuantizeX(-1.3f, SubpixelQuant.Quarter);
        byte pos = QuantizedSubpixel.QuantizeX(0.7f, SubpixelQuant.Quarter);
        await Assert.That(neg).IsEqualTo(pos);
    }

    [Test]
    public async Task Eighth_Produces8DistinctValues()
    {
        var seen = new System.Collections.Generic.HashSet<byte>();
        for (int i = 0; i < 80; i++)
        {
            float x = i / 80.0f;
            seen.Add(QuantizedSubpixel.QuantizeX(x, SubpixelQuant.Eighth));
        }
        await Assert.That(seen.Count).IsEqualTo(8);
    }

    [Test]
    public async Task RoundTrip_Quarter_ErrorWithinQuarterPixel()
    {
        for (int i = 0; i < 100; i++)
        {
            float x = i / 100.0f;
            byte q = QuantizedSubpixel.QuantizeX(x, SubpixelQuant.Quarter);
            float back = QuantizedSubpixel.Dequantize(q, SubpixelQuant.Quarter);
            float frac = x - (float)Math.Floor(x);
            float err = Math.Abs(back - frac);
            if (err > 0.5f) err = 1.0f - err; // wrap-around distance
            await Assert.That(err).IsLessThanOrEqualTo(0.25f);
        }
    }

    [Test]
    public async Task RoundTrip_Eighth_ErrorWithinEighthPixel()
    {
        for (int i = 0; i < 100; i++)
        {
            float x = i / 100.0f;
            byte q = QuantizedSubpixel.QuantizeX(x, SubpixelQuant.Eighth);
            float back = QuantizedSubpixel.Dequantize(q, SubpixelQuant.Eighth);
            float frac = x - (float)Math.Floor(x);
            float err = Math.Abs(back - frac);
            if (err > 0.5f) err = 1.0f - err; // wrap-around distance
            await Assert.That(err).IsLessThanOrEqualTo(0.125f);
        }
    }

    [Test]
    public async Task Dequantize_Quarter_0_Returns0()
    {
        await Assert.That(QuantizedSubpixel.Dequantize(0, SubpixelQuant.Quarter)).IsEqualTo(0.0f);
    }

    [Test]
    public async Task Dequantize_Quarter_1_ReturnsQuarter()
    {
        await Assert.That(QuantizedSubpixel.Dequantize(1, SubpixelQuant.Quarter)).IsEqualTo(0.25f);
    }

    [Test]
    public async Task Dequantize_Quarter_2_ReturnsHalf()
    {
        await Assert.That(QuantizedSubpixel.Dequantize(2, SubpixelQuant.Quarter)).IsEqualTo(0.5f);
    }

    [Test]
    public async Task Dequantize_Quarter_3_ReturnsThreeQuarters()
    {
        await Assert.That(QuantizedSubpixel.Dequantize(3, SubpixelQuant.Quarter)).IsEqualTo(0.75f);
    }

    [Test]
    public async Task Dequantize_Eighth_7_ReturnsSevenEighths()
    {
        await Assert.That(QuantizedSubpixel.Dequantize(7, SubpixelQuant.Eighth)).IsEqualTo(0.875f);
    }
}
