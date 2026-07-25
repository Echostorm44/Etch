using TUnit;
using Etch.Gpu.Compositor.Pipelines;

namespace Etch.Gpu.Compositor.Tests;

public sealed class ImageGpuPipelineTests
{
    [Test]
    public async Task ImageFilterMode_HasExpectedValues()
    {
        await Assert.That((int)ImageFilterMode.Bilinear).IsEqualTo(0);
        await Assert.That((int)ImageFilterMode.Bicubic).IsEqualTo(1);
    }

    [Test]
    public async Task ImageDrawCommand_Constructor_SetsAllProperties()
    {
        var cmd = new ImageDrawCommand(
            default,
            default,
            1f, 0f, 0f,
            0f, 1f, 0f,
            0f, 0f, 1f,
            ImageFilterMode.Bicubic);

        await Assert.That(cmd.FilterMode).IsEqualTo(ImageFilterMode.Bicubic);
        await Assert.That(cmd.M00).IsEqualTo(1f);
        await Assert.That(cmd.M11).IsEqualTo(1f);
        await Assert.That(cmd.M22).IsEqualTo(1f);
    }

    [Test]
    public async Task ImageDrawCommand_IdentityTransform_HasCorrectValues()
    {
        var cmd = new ImageDrawCommand(
            default,
            default,
            1f, 0f, 0f,
            0f, 1f, 0f,
            0f, 0f, 1f,
            ImageFilterMode.Bilinear);

        await Assert.That(cmd.M00).IsEqualTo(1f);
        await Assert.That(cmd.M01).IsEqualTo(0f);
        await Assert.That(cmd.M02).IsEqualTo(0f);
        await Assert.That(cmd.M10).IsEqualTo(0f);
        await Assert.That(cmd.M11).IsEqualTo(1f);
        await Assert.That(cmd.M12).IsEqualTo(0f);
        await Assert.That(cmd.M20).IsEqualTo(0f);
        await Assert.That(cmd.M21).IsEqualTo(0f);
        await Assert.That(cmd.M22).IsEqualTo(1f);
    }

    [Test]
    public async Task ImageDrawCommand_ScaleTransform_EncodesCorrectly()
    {
        var cmd = new ImageDrawCommand(
            default,
            default,
            2f, 0f, 0f,
            0f, 2f, 0f,
            0f, 0f, 1f,
            ImageFilterMode.Bilinear);

        await Assert.That(cmd.M00).IsEqualTo(2f);
        await Assert.That(cmd.M11).IsEqualTo(2f);
    }

    [Test]
    public async Task ImageDrawCommand_TranslateTransform_EncodesCorrectly()
    {
        var cmd = new ImageDrawCommand(
            default,
            default,
            1f, 0f, 10f,
            0f, 1f, 20f,
            0f, 0f, 1f,
            ImageFilterMode.Bilinear);

        await Assert.That(cmd.M02).IsEqualTo(10f);
        await Assert.That(cmd.M12).IsEqualTo(20f);
    }
}
