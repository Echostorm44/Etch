using TUnit;
using Etch.Effects.Image;

namespace Etch.Effects.Tests;

public sealed class MitchellNetravaliTests
{
    [Test]
    public async Task Constants_HaveCorrectValues()
    {
        float b = MitchellNetravali.B;
        float c = MitchellNetravali.C;
        float computedB = 1f / 3f;
        float computedC = 1f / 3f;
        await Assert.That(b == computedB).IsTrue();
        await Assert.That(c == computedC).IsTrue();
    }

    [Test]
    public async Task Weight_AtZero_ReturnsMaximum()
    {
        float w = MitchellNetravali.Weight(0f);
        float oneMinusEpsilon = 0.999f;
        await Assert.That(w > oneMinusEpsilon).IsTrue();
    }

    [Test]
    public async Task Weight_AtOne_ReturnsCorrectValue()
    {
        float w = MitchellNetravali.Weight(1f);
        float zero = 0f;
        float one = 1f;
        await Assert.That(w > zero).IsTrue();
        await Assert.That(w < one).IsTrue();
    }

    [Test]
    public async Task Weight_AtNegativeOne_ReturnsSameAsPositive()
    {
        float wPos = MitchellNetravali.Weight(1f);
        float wNeg = MitchellNetravali.Weight(-1f);
        await Assert.That(wPos == wNeg).IsTrue();
    }

    [Test]
    public async Task Weight_AtTwo_ReturnsZero()
    {
        float w = MitchellNetravali.Weight(2f);
        float epsilon = 0.001f;
        await Assert.That(w < epsilon).IsTrue();
    }

    [Test]
    public async Task Weight_BeyondTwo_ReturnsZero()
    {
        float w = MitchellNetravali.Weight(3f);
        float epsilon = 0.001f;
        await Assert.That(w < epsilon).IsTrue();
    }

    [Test]
    public async Task Weight2D_CanBeComputed()
    {
        float w = MitchellNetravali.Weight2D(0.5f, 0.5f);
        float zero = 0f;
        await Assert.That(w > zero).IsTrue();
    }

    [Test]
    public async Task Weight2D_Symmetric()
    {
        float w1 = MitchellNetravali.Weight2D(0.3f, 0.7f);
        float w2 = MitchellNetravali.Weight2D(0.7f, 0.3f);
        await Assert.That(w1 == w2).IsTrue();
    }
}

public sealed class BicubicSamplingTests
{
    [Test]
    public async Task ImageFilter_Bicubic_HasCorrectValue()
    {
        var filter = ImageFilter.Bicubic;
        int value = 2;
        await Assert.That((int)filter).IsEqualTo(value);
    }

    [Test]
    public async Task ImageBrush_WithBicubicFilter_HasCorrectFilter()
    {
        using var source = CreateTestHdrImage();
        var brush = new ImageBrush(source, ImageFilter.Bicubic, ImageExtend.Clamp, ImageExtend.Clamp);
        await Assert.That((int)brush.Filter).IsEqualTo(2);
    }

    [Test]
    public async Task ImageBrush_BicubicAndClamp_IsValid()
    {
        using var source = CreateTestHdrImage();
        var brush = new ImageBrush(source, ImageFilter.Bicubic, ImageExtend.Clamp, ImageExtend.Clamp);
        await Assert.That(brush.Source).IsNotNull();
        int bicubicValue = (int)ImageFilter.Bicubic;
        int clampValue = (int)ImageExtend.Clamp;
        await Assert.That((int)brush.Filter).IsEqualTo(bicubicValue);
        await Assert.That((int)brush.ExtendX).IsEqualTo(clampValue);
        await Assert.That((int)brush.ExtendY).IsEqualTo(clampValue);
    }

    private static ImageSource CreateTestHdrImage()
    {
        byte[] hdrData = System.Text.Encoding.ASCII.GetBytes(
            "#?RADIANCE\n" +
            "FORMAT=32-bit_rle_rgbe\n" +
            "\n" +
            "-Y 2 +X 2\n" +
            "        \n" +
            "        \n"
        );
        return ImageSource.Decode(hdrData);
    }
}
