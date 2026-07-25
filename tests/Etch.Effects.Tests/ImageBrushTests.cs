using TUnit;
using Etch.Effects.Image;

namespace Etch.Effects.Tests;

public sealed class ImageBrushTests
{
    [Test]
    public async Task ImageFilter_Nearest_HasZeroValue()
    {
        var filter = ImageFilter.Nearest;
        await Assert.That((int)filter).IsEqualTo(0);
    }

    [Test]
    public async Task ImageFilter_Bilinear_HasValueOne()
    {
        var filter = ImageFilter.Bilinear;
        await Assert.That((int)filter).IsEqualTo(1);
    }

    [Test]
    public async Task ImageFilter_Bicubic_HasValueTwo()
    {
        var filter = ImageFilter.Bicubic;
        await Assert.That((int)filter).IsEqualTo(2);
    }

    [Test]
    public async Task ImageExtend_Clamp_HasZeroValue()
    {
        var extend = ImageExtend.Clamp;
        await Assert.That((int)extend).IsEqualTo(0);
    }

    [Test]
    public async Task ImageExtend_Repeat_HasValueOne()
    {
        var extend = ImageExtend.Repeat;
        await Assert.That((int)extend).IsEqualTo(1);
    }

    [Test]
    public async Task ImageExtend_Mirror_HasValueTwo()
    {
        var extend = ImageExtend.Mirror;
        await Assert.That((int)extend).IsEqualTo(2);
    }

    [Test]
    public async Task ImageExtend_Pad_HasValueThree()
    {
        var extend = ImageExtend.Pad;
        await Assert.That((int)extend).IsEqualTo(3);
    }

    [Test]
    public async Task ImageBrush_Constructor_SetsAllFields()
    {
        using var source = CreateTestHdrImage();
        var brush = new ImageBrush(source, ImageFilter.Bicubic, ImageExtend.Mirror, ImageExtend.Repeat);

        await Assert.That((int)brush.Filter).IsEqualTo(2);
        await Assert.That((int)brush.ExtendX).IsEqualTo(2);
        await Assert.That((int)brush.ExtendY).IsEqualTo(1);
    }

    [Test]
    public async Task ImageBrush_CreateBilinear_HasCorrectFilter()
    {
        using var source = CreateTestHdrImage();
        var brush = ImageBrush.CreateBilinear(source);

        await Assert.That((int)brush.Filter).IsEqualTo(1);
    }

    [Test]
    public async Task ImageBrush_CreateBilinear_HasClampExtend()
    {
        using var source = CreateTestHdrImage();
        var brush = ImageBrush.CreateBilinear(source);

        await Assert.That((int)brush.ExtendX).IsEqualTo(0);
        await Assert.That((int)brush.ExtendY).IsEqualTo(0);
    }

    [Test]
    public async Task ImageBrush_CreateNearest_HasCorrectFilter()
    {
        using var source = CreateTestHdrImage();
        var brush = ImageBrush.CreateNearest(source);

        await Assert.That((int)brush.Filter).IsEqualTo(0);
    }

    [Test]
    public async Task ImageBrush_CreateBilinearRepeat_HasRepeatExtend()
    {
        using var source = CreateTestHdrImage();
        var brush = ImageBrush.CreateBilinearRepeat(source);

        await Assert.That((int)brush.ExtendX).IsEqualTo(1);
        await Assert.That((int)brush.ExtendY).IsEqualTo(1);
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

public sealed class BilinearSamplingTests
{
    [Test]
    public async Task Extend_Clamp_ReturnsEdgeTexels()
    {
        var clampedCoord = Clamp(-1, 0, 1);
        await Assert.That(clampedCoord).IsEqualTo(0);
    }

    [Test]
    public async Task Extend_Clamp_WithinRange_ReturnsSame()
    {
        var clampedCoord = Clamp(1, 0, 1);
        await Assert.That(clampedCoord).IsEqualTo(1);
    }

    [Test]
    public async Task Extend_Repeat_WrapsCorrectly()
    {
        var repeatedCoord = Repeat(-1, 2);
        await Assert.That(repeatedCoord).IsEqualTo(1);
    }

    [Test]
    public async Task Extend_Repeat_NegativeTwo_ReturnsZero()
    {
        var repeatedCoord = Repeat(-2, 2);
        await Assert.That(repeatedCoord).IsEqualTo(0);
    }

    [Test]
    public async Task Extend_Repeat_Positive_ReturnsSame()
    {
        var repeatedCoord = Repeat(3, 2);
        await Assert.That(repeatedCoord).IsEqualTo(1);
    }

    [Test]
    public async Task Extend_Mirror_FirstNegativeCoord()
    {
        var mirroredCoord = Mirror(-1, 2);
        await Assert.That(mirroredCoord).IsEqualTo(1);
    }

    [Test]
    public async Task Extend_Mirror_SecondNegativeCoord()
    {
        var mirroredCoord = Mirror(-2, 2);
        await Assert.That(mirroredCoord).IsEqualTo(0);
    }

    [Test]
    public async Task Extend_Mirror_PositiveCoords()
    {
        await Assert.That(Mirror(0, 2)).IsEqualTo(0);
        await Assert.That(Mirror(1, 2)).IsEqualTo(1);
        await Assert.That(Mirror(2, 2)).IsEqualTo(1);
        await Assert.That(Mirror(3, 2)).IsEqualTo(0);
    }

    [Test]
    public async Task Extend_Pad_DetectsOutOfRange()
    {
        var isOutOfRange = IsPadOutOfRange(-0.25f, 2);
        await Assert.That(isOutOfRange).IsTrue();
    }

    [Test]
    public async Task Extend_Pad_DetectsInRange()
    {
        var isOutOfRange = IsPadOutOfRange(0.5f, 2);
        await Assert.That(isOutOfRange).IsFalse();
    }

    [Test]
    public async Task Extend_Pad_DetectsAtUpperBound()
    {
        var isOutOfRange = IsPadOutOfRange(2.0f, 2);
        await Assert.That(isOutOfRange).IsTrue();
    }

    private static int Clamp(int coord, int min, int max)
    {
        return coord < min ? min : coord > max ? max : coord;
    }

    private static int Repeat(int coord, int size)
    {
        int c = coord % size;
        return c < 0 ? c + size : c;
    }

    private static int Mirror(int coord, int size)
    {
        int m = coord >= 0 ? coord : -coord - 1;
        int cycle = m / size;
        int pos = m % size;
        if (cycle % 2 == 1)
        {
            pos = size - 1 - pos;
        }
        return coord >= 0 ? pos : (size - 1 - pos);
    }

    private static bool IsPadOutOfRange(float uv, int size)
    {
        return uv < 0.0f || uv >= (float)size;
    }
}
