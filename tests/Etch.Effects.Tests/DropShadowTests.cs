using Etch.Effects.Shadow;
using Etch.Geometry;
using TUnit;

namespace Etch.Effects.Tests;

public sealed class DropShadowTests
{
    [Test]
    public async Task ComputeShadowBounds_ZeroBlur_ReturnsContentBoundsOffset()
    {
        var contentBounds = new Rect(10, 20, 110, 70);
        var offset = new Vec2(5, -3);
        float blurRadius = 0f;

        var shadowBounds = DropShadow.ComputeShadowBounds(contentBounds, offset, blurRadius);

        await Assert.That(shadowBounds.MinX).IsEqualTo(15);
        await Assert.That(shadowBounds.MinY).IsEqualTo(17);
        await Assert.That(shadowBounds.MaxX).IsEqualTo(115);
        await Assert.That(shadowBounds.MaxY).IsEqualTo(67);
    }

    [Test]
    public async Task ComputeShadowBounds_PositiveBlur_InflatesBounds()
    {
        var contentBounds = new Rect(10, 20, 110, 70);
        var offset = new Vec2(5, -3);
        float blurRadius = 10f;

        var shadowBounds = DropShadow.ComputeShadowBounds(contentBounds, offset, blurRadius);

        double expectedInflation = 3.0 * 10f;
        await Assert.That(shadowBounds.MinX).IsEqualTo(contentBounds.MinX + offset.X - expectedInflation);
        await Assert.That(shadowBounds.MinY).IsEqualTo(contentBounds.MinY + offset.Y - expectedInflation);
        await Assert.That(shadowBounds.MaxX).IsEqualTo(contentBounds.MaxX + offset.X + expectedInflation);
        await Assert.That(shadowBounds.MaxY).IsEqualTo(contentBounds.MaxY + offset.Y + expectedInflation);
    }

    [Test]
    public async Task ComputeShadowBounds_LargeBlur_InflatesSignificantly()
    {
        var contentBounds = new Rect(0, 0, 100, 100);
        var offset = new Vec2(0, 0);
        float blurRadius = 20f;

        var shadowBounds = DropShadow.ComputeShadowBounds(contentBounds, offset, blurRadius);

        double expectedInflation = 3.0 * 20f;
        await Assert.That(shadowBounds.MinX).IsEqualTo(-expectedInflation);
        await Assert.That(shadowBounds.MinY).IsEqualTo(-expectedInflation);
        await Assert.That(shadowBounds.MaxX).IsEqualTo(100 + expectedInflation);
        await Assert.That(shadowBounds.MaxY).IsEqualTo(100 + expectedInflation);
    }

    [Test]
    public async Task ComputeBlurOctaves_Radius0_Returns0()
    {
        int octaves = DropShadow.ComputeBlurOctaves(0f);
        await Assert.That(octaves).IsEqualTo(0);
    }

    [Test]
    public async Task ComputeBlurOctaves_RadiusNegative_Returns0()
    {
        int octaves = DropShadow.ComputeBlurOctaves(-5f);
        await Assert.That(octaves).IsEqualTo(0);
    }

    [Test]
    public async Task ComputeBlurOctaves_Radius1_Returns1()
    {
        int octaves = DropShadow.ComputeBlurOctaves(1f);
        await Assert.That(octaves).IsEqualTo(1);
    }

    [Test]
    public async Task ComputeBlurOctaves_Radius3_Returns2()
    {
        int octaves = DropShadow.ComputeBlurOctaves(3f);
        await Assert.That(octaves).IsEqualTo(2);
    }

    [Test]
    public async Task ComputeBlurOctaves_Radius7_Returns3()
    {
        int octaves = DropShadow.ComputeBlurOctaves(7f);
        await Assert.That(octaves).IsEqualTo(3);
    }
}

public sealed class ShadowParamsTests
{
    [Test]
    public async Task Constructor_SetsOffset()
    {
        var offset = new Vec2(3.5f, -2.0f);
        var shadowParams = new ShadowParams(offset, 10f);
        await Assert.That(shadowParams.Offset.X).IsEqualTo(3.5f);
        await Assert.That(shadowParams.Offset.Y).IsEqualTo(-2.0f);
    }

    [Test]
    public async Task Constructor_SetsBlurRadius()
    {
        var shadowParams = new ShadowParams(new Vec2(0, 0), 16f);
        await Assert.That(shadowParams.BlurRadius).IsEqualTo(16f);
    }

    [Test]
    public async Task Constructor_DefaultShadowColor_IsSemiTransparentBlack()
    {
        var shadowParams = new ShadowParams(new Vec2(0, 0), 10f);
        await Assert.That(shadowParams.ShadowColor).IsEqualTo(0x40000000u);
    }

    [Test]
    public async Task Constructor_CanSetCustomShadowColor()
    {
        var shadowParams = new ShadowParams(new Vec2(0, 0), 10f, 0x80000000u);
        await Assert.That(shadowParams.ShadowColor).IsEqualTo(0x80000000u);
    }

    [Test]
    public async Task Constructor_ZeroOffset_IsAllowed()
    {
        var shadowParams = new ShadowParams(new Vec2(0, 0), 5f);
        await Assert.That(shadowParams.Offset.X).IsEqualTo(0f);
        await Assert.That(shadowParams.Offset.Y).IsEqualTo(0f);
    }

    [Test]
    public async Task Constructor_NegativeBlurRadius_IsAllowed()
    {
        var shadowParams = new ShadowParams(new Vec2(0, 0), -5f);
        await Assert.That(shadowParams.BlurRadius).IsEqualTo(-5f);
    }
}