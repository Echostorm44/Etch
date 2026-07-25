using System;
using Etch.Effects.Blur;
using Etch.Effects.Shadow;
using Etch.Geometry;
using TUnit;

namespace Etch.Effects.Tests;

public sealed class BlurShadowComposabilityTests
{
    [Test]
    public async Task ShadowBlurOctaves_MatchDualFilterBlurOctaves_ForRadius1()
    {
        const float radius = 1f;
        int shadowOctaves = DropShadow.ComputeBlurOctaves(radius);
        int blurOctaves = DualFilterBlur.OctaveCount(radius);
        await Assert.That(shadowOctaves).IsEqualTo(blurOctaves);
    }

    [Test]
    public async Task ShadowBlurOctaves_MatchDualFilterBlurOctaves_ForRadius3()
    {
        const float radius = 3f;
        int shadowOctaves = DropShadow.ComputeBlurOctaves(radius);
        int blurOctaves = DualFilterBlur.OctaveCount(radius);
        await Assert.That(shadowOctaves).IsEqualTo(blurOctaves);
    }

    [Test]
    public async Task ShadowBlurOctaves_MatchDualFilterBlurOctaves_ForRadius7()
    {
        const float radius = 7f;
        int shadowOctaves = DropShadow.ComputeBlurOctaves(radius);
        int blurOctaves = DualFilterBlur.OctaveCount(radius);
        await Assert.That(shadowOctaves).IsEqualTo(blurOctaves);
    }

    [Test]
    public async Task ShadowBlurOctaves_MatchDualFilterBlurOctaves_ForRadius15()
    {
        const float radius = 15f;
        int shadowOctaves = DropShadow.ComputeBlurOctaves(radius);
        int blurOctaves = DualFilterBlur.OctaveCount(radius);
        await Assert.That(shadowOctaves).IsEqualTo(blurOctaves);
    }

    [Test]
    public async Task ShadowBlurOctaves_MatchDualFilterBlurOctaves_ForRadius31()
    {
        const float radius = 31f;
        int shadowOctaves = DropShadow.ComputeBlurOctaves(radius);
        int blurOctaves = DualFilterBlur.OctaveCount(radius);
        await Assert.That(shadowOctaves).IsEqualTo(blurOctaves);
    }

    [Test]
    public async Task ShadowBlurOctaves_MatchDualFilterBlurOctaves_ForRadius63()
    {
        const float radius = 63f;
        int shadowOctaves = DropShadow.ComputeBlurOctaves(radius);
        int blurOctaves = DualFilterBlur.OctaveCount(radius);
        await Assert.That(shadowOctaves).IsEqualTo(blurOctaves);
    }

    [Test]
    public async Task ShadowBounds_InflationMatches3xBlurRadius()
    {
        var contentBounds = new Rect(10, 20, 110, 70);
        var offset = new Vec2(0, 0);
        float blurRadius = 10f;

        var shadowBounds = DropShadow.ComputeShadowBounds(contentBounds, offset, blurRadius);

        double expectedInflation = 3.0 * blurRadius;
        await Assert.That(shadowBounds.MinX).IsEqualTo(contentBounds.MinX - expectedInflation);
        await Assert.That(shadowBounds.MaxX).IsEqualTo(contentBounds.MaxX + expectedInflation);
        await Assert.That(shadowBounds.MinY).IsEqualTo(contentBounds.MinY - expectedInflation);
        await Assert.That(shadowBounds.MaxY).IsEqualTo(contentBounds.MaxY + expectedInflation);
    }

    [Test]
    public async Task ShadowBounds_WithOffset_AppliesOffsetCorrectly()
    {
        var contentBounds = new Rect(0, 0, 100, 100);
        var offset = new Vec2(20, -10);
        float blurRadius = 5f;

        var shadowBounds = DropShadow.ComputeShadowBounds(contentBounds, offset, blurRadius);

        double expectedInflation = 3.0 * blurRadius;
        await Assert.That(shadowBounds.MinX).IsEqualTo(contentBounds.MinX + offset.X - expectedInflation);
        await Assert.That(shadowBounds.MaxX).IsEqualTo(contentBounds.MaxX + offset.X + expectedInflation);
        await Assert.That(shadowBounds.MinY).IsEqualTo(contentBounds.MinY + offset.Y - expectedInflation);
        await Assert.That(shadowBounds.MaxY).IsEqualTo(contentBounds.MaxY + offset.Y + expectedInflation);
    }

    [Test]
    public async Task ShadowParams_WithBlurRadius_ProducesCorrectBlurOctaves()
    {
        var shadowParams = new ShadowParams(new Vec2(5, -3), 7f, 0x40000000);
        int octaves = DropShadow.ComputeBlurOctaves(shadowParams.BlurRadius);
        await Assert.That(octaves).IsEqualTo(3);
    }

    [Test]
    public async Task ShadowParams_ZeroBlur_ProducesZeroOctaves()
    {
        var shadowParams = new ShadowParams(new Vec2(0, 0), 0f);
        int octaves = DropShadow.ComputeBlurOctaves(shadowParams.BlurRadius);
        await Assert.That(octaves).IsEqualTo(0);
    }

    [Test]
    public async Task ShadowParams_NegativeBlur_ProducesZeroOctaves()
    {
        var shadowParams = new ShadowParams(new Vec2(0, 0), -5f);
        int octaves = DropShadow.ComputeBlurOctaves(shadowParams.BlurRadius);
        await Assert.That(octaves).IsEqualTo(0);
    }

    [Test]
    public async Task BlurParams_LargeRadius_ProducesMaxOctaves()
    {
        var blurParams = new BlurParams(63f);
        int octaves = DualFilterBlur.OctaveCount(blurParams.RadiusPx);
        await Assert.That(octaves).IsEqualTo(6);
    }

    [Test]
    public async Task BlurParams_ZeroRadius_ProducesZeroOctaves()
    {
        var blurParams = new BlurParams(0f);
        int octaves = DualFilterBlur.OctaveCount(blurParams.RadiusPx);
        await Assert.That(octaves).IsEqualTo(0);
    }
}