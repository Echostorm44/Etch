using TUnit;
using Etch.Effects.Image;

namespace Etch.Effects.Tests;

public sealed class ImageSourceTests
{
    [Test]
    public async Task ImageFormat_Rgba8Unorm_HasZeroValue()
    {
        var format = ImageFormat.Rgba8Unorm;
        await Assert.That((int)format).IsEqualTo(0);
    }

    [Test]
    public async Task ImageFormat_Srgb8UnormAlpha_HasValueTwo()
    {
        var format = ImageFormat.Srgb8UnormAlpha;
        await Assert.That((int)format).IsEqualTo(2);
    }

    [Test]
    public async Task ImageFormat_Rgba16f_HasValueOne()
    {
        var format = ImageFormat.Rgba16f;
        await Assert.That((int)format).IsEqualTo(1);
    }

    [Test]
    public async Task ImageDecodeOptions_Defaults_HaveCorrectValues()
    {
        var options = new ImageDecodeOptions();
        var expectedPremultiply = true;
        var expectedSrgb = true;

        await Assert.That(options.PremultiplyAlpha).IsEqualTo(expectedPremultiply);
        await Assert.That(options.SrgbToLinear).IsEqualTo(expectedSrgb);
        await Assert.That(options.ForceFormat).IsNull();
    }

    [Test]
    public async Task ImageDecodeOptions_CanOverridePremultiplyAlpha()
    {
        var options = new ImageDecodeOptions { PremultiplyAlpha = false };
        await Assert.That(options.PremultiplyAlpha).IsFalse();
    }

    [Test]
    public async Task ImageDecodeOptions_CanOverrideSrgbToLinear()
    {
        var options = new ImageDecodeOptions { SrgbToLinear = false };
        await Assert.That(options.SrgbToLinear).IsFalse();
    }

    [Test]
    public async Task ImageDecodeOptions_CanSetForceFormat()
    {
        var options = new ImageDecodeOptions { ForceFormat = ImageFormat.Rgba16f };
        await Assert.That(options.ForceFormat).IsEqualTo(ImageFormat.Rgba16f);
    }
}
