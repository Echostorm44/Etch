using Etch.Testing;
using TUnit;

namespace Etch.Testing.Tests;

internal sealed class PixelDiffTests
{
    [Test]
    public async Task IdenticalImages_CompareEqual_MeanZeroP95ZeroMaxZero()
    {
        int w = 100;
        int h = 100;
        var actual = new byte[w * h * 4];
        var reference = new byte[w * h * 4];

        for (int i = 0; i < actual.Length; i += 4)
        {
            actual[i] = 128;
            actual[i + 1] = 64;
            actual[i + 2] = 32;
            reference[i] = 128;
            reference[i + 1] = 64;
            reference[i + 2] = 32;
            actual[i + 3] = 255;
            reference[i + 3] = 255;
        }

        var tolerance = new DiffTolerance(0, 0, 0);
        var result = PixelDiff.Compare(actual, reference, w, h, tolerance);

        await Assert.That(result.Pass).IsTrue();
        await Assert.That(result.MeanError).IsEqualTo(0);
        await Assert.That(result.P95Error).IsEqualTo(0);
        await Assert.That(result.MaxError).IsEqualTo(0);
    }

    [Test]
    public async Task SinglePixelShift10On255_MaxEquals10()
    {
        int w = 10;
        int h = 10;
        var actual = new byte[w * h * 4];
        var reference = new byte[w * h * 4];

        for (int i = 0; i < actual.Length; i += 4)
        {
            actual[i] = 128;
            actual[i + 1] = 64;
            actual[i + 2] = 32;
            reference[i] = 128;
            reference[i + 1] = 64;
            reference[i + 2] = 32;
            actual[i + 3] = 255;
            reference[i + 3] = 255;
        }

        actual[0] = 138;

        var tolerance = new DiffTolerance(float.MaxValue, float.MaxValue, 255);
        var result = PixelDiff.Compare(actual, reference, w, h, tolerance);

        await Assert.That(result.MaxError).IsEqualTo(10);
        await Assert.That(result.Pass).IsTrue();
    }

    [Test]
    public async Task ToleranceEnforcement_OutsideTolerance_Fails()
    {
        int w = 10;
        int h = 10;
        var actual = new byte[w * h * 4];
        var reference = new byte[w * h * 4];

        for (int i = 0; i < actual.Length; i += 4)
        {
            actual[i] = 200;
            actual[i + 1] = 64;
            actual[i + 2] = 32;
            reference[i] = 0;
            reference[i + 1] = 64;
            reference[i + 2] = 32;
            actual[i + 3] = 255;
            reference[i + 3] = 255;
        }

        var tolerance = new DiffTolerance(10, 10, 10);
        var result = PixelDiff.Compare(actual, reference, w, h, tolerance);

        await Assert.That(result.Pass).IsFalse();
        await Assert.That(result.MaxError).IsEqualTo(200);
    }

    [Test]
    public async Task ComputeMeanError_Identical_Zero()
    {
        int w = 10;
        int h = 10;
        var actual = new byte[w * h * 4];
        var reference = new byte[w * h * 4];

        for (int i = 0; i < actual.Length; i++)
        {
            actual[i] = 100;
            reference[i] = 100;
        }

        var error = PixelDiff.ComputeMeanError(actual, reference);

        await Assert.That(error).IsEqualTo(0);
    }

    [Test]
    public async Task ComputeMaxError_Identical_Zero()
    {
        int w = 10;
        int h = 10;
        var actual = new byte[w * h * 4];
        var reference = new byte[w * h * 4];

        for (int i = 0; i < actual.Length; i++)
        {
            actual[i] = 100;
            reference[i] = 100;
        }

        var error = PixelDiff.ComputeMaxError(actual, reference);

        await Assert.That(error).IsEqualTo(0);
    }

    [Test]
    public async Task DifferentLengthBuffers_ReturnsMaxValue()
    {
        var actual = new byte[100];
        var reference = new byte[50];

        var error = PixelDiff.ComputeMeanError(actual, reference);

        await Assert.That(error).IsEqualTo(float.MaxValue);
    }

    [Test]
    public async Task WidespreadDifferences_HighMeanError()
    {
        int w = 4;
        int h = 4;
        var actual = new byte[w * h * 4];
        var reference = new byte[w * h * 4];

        for (int i = 0; i < actual.Length; i += 4)
        {
            actual[i] = 255;
            actual[i + 1] = 255;
            actual[i + 2] = 255;
            reference[i] = 0;
            reference[i + 1] = 0;
            reference[i + 2] = 0;
            actual[i + 3] = 255;
            reference[i + 3] = 255;
        }

        var error = PixelDiff.ComputeMeanError(actual, reference);

        await Assert.That(error).IsEqualTo(255);
    }

    [Test]
    public async Task ComputeMaxError_SinglePixelDiff_ReturnsMaxDiff()
    {
        int w = 10;
        int h = 10;
        var actual = new byte[w * h * 4];
        var reference = new byte[w * h * 4];

        for (int i = 0; i < actual.Length; i++)
        {
            actual[i] = 128;
            reference[i] = 128;
        }
        actual[0] = 255;
        reference[0] = 0;

        var error = PixelDiff.ComputeMaxError(actual, reference);

        await Assert.That(error).IsEqualTo(255);
    }
}