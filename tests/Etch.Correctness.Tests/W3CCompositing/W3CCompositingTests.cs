using System;
using System.Text;
using Etch.ClipBlendGradient;
using Etch.Geometry;
using Etch.Scene;
using Etch.Testing;
using TUnit;
using static Etch.ClipBlendGradient.BlendMode;
using CbBlendMode = Etch.ClipBlendGradient.BlendMode;

namespace Etch.Correctness.Tests.W3CCompositing;

public class W3CCompositingTests
{
    private const int RenderSize = 64;

    // Tolerance: CPU uses Rgba16f (half precision) vs reference double.
    // ±1/255 per channel is sufficient for exact blend-mode verification.
    private const int Tolerance = 1;

    [Test]
    public async Task Debug_SimpleRedScene()
    {
        var builder = SceneBuilder.Begin();
        builder.BeginFrame();
        int xform = builder.AddTransform(Affine.Identity);
        var paint = Paint.Solid(0xFFFF0000u);
        int paintId = builder.AddPaint(paint);
        builder.FillRect(new Rect(0, 0, 64, 64), paintId, xform);
        builder.EndFrame();
        var scene = builder.End();

        byte[] actual = SceneRunner.RunCpu(scene, 64, 64);
        int idx = (32 * 64 + 32) * 4;

        await Assert.That((int)actual[idx + 2]).IsEqualTo(255);
        await Assert.That((int)actual[idx + 1]).IsEqualTo(0);
        await Assert.That((int)actual[idx + 0]).IsEqualTo(0);
    }

    private static readonly uint[] TestColors = new[]
    {
        0xFFFF0000u, // opaque red
        0xFF00FF00u, // opaque green
        0xFF0000FFu, // opaque blue
        0xFFFFFFFFu, // opaque white
        0xFF000000u, // opaque black
        0xFF808080u, // opaque grey
        0x80FF0000u, // 50% red
        0x80FFFF00u, // 50% yellow
    };

    [Test]
    public async Task NormalBlend_CenterPixel_MatchesReference()
    {
        await RunBlendModeTest(CbBlendMode.Normal, 0xFFFF0000u, 0xFF00FF00u);
    }

    [Test]
    public async Task MultiplyBlend_CenterPixel_MatchesReference()
    {
        await RunBlendModeTest(CbBlendMode.Multiply, 0xFFFF0000u, 0xFF00FF00u);
    }

    [Test]
    public async Task ScreenBlend_CenterPixel_MatchesReference()
    {
        await RunBlendModeTest(CbBlendMode.Screen, 0xFFFF0000u, 0xFF00FF00u);
    }

    [Test]
    public async Task OverlayBlend_CenterPixel_MatchesReference()
    {
        await RunBlendModeTest(CbBlendMode.Overlay, 0xFF808080u, 0xFF404040u);
    }

    [Test]
    public async Task DarkenBlend_CenterPixel_MatchesReference()
    {
        await RunBlendModeTest(CbBlendMode.Darken, 0xFFFF0000u, 0xFF00FF00u);
    }

    [Test]
    public async Task LightenBlend_CenterPixel_MatchesReference()
    {
        await RunBlendModeTest(CbBlendMode.Lighten, 0xFFFF0000u, 0xFF00FF00u);
    }

    [Test]
    public async Task ColorDodgeBlend_CenterPixel_MatchesReference()
    {
        await RunBlendModeTest(CbBlendMode.ColorDodge, 0xFF808080u, 0xFF404040u);
    }

    [Test]
    public async Task ColorBurnBlend_CenterPixel_MatchesReference()
    {
        await RunBlendModeTest(CbBlendMode.ColorBurn, 0xFF808080u, 0xFF404040u);
    }

    [Test]
    public async Task HardLightBlend_CenterPixel_MatchesReference()
    {
        await RunBlendModeTest(CbBlendMode.HardLight, 0xFF808080u, 0xFF404040u);
    }

    [Test]
    public async Task SoftLightBlend_CenterPixel_MatchesReference()
    {
        await RunBlendModeTest(CbBlendMode.SoftLight, 0xFF808080u, 0xFF404040u);
    }

    [Test]
    public async Task DifferenceBlend_CenterPixel_MatchesReference()
    {
        await RunBlendModeTest(CbBlendMode.Difference, 0xFFFF0000u, 0xFF00FF00u);
    }

    [Test]
    public async Task ExclusionBlend_CenterPixel_MatchesReference()
    {
        await RunBlendModeTest(CbBlendMode.Exclusion, 0xFF808080u, 0xFF404040u);
    }

    [Test]
    public async Task HueBlend_CenterPixel_MatchesReference()
    {
        await RunBlendModeTest(CbBlendMode.Hue, 0xFF8000FFu, 0xFFFF8000u);
    }

    [Test]
    public async Task SaturationBlend_CenterPixel_MatchesReference()
    {
        await RunBlendModeTest(CbBlendMode.Saturation, 0xFF8000FFu, 0xFFFF8000u);
    }

    [Test]
    public async Task ColorBlend_CenterPixel_MatchesReference()
    {
        await RunBlendModeTest(CbBlendMode.Color, 0xFF8000FFu, 0xFFFF8000u);
    }

    [Test]
    public async Task LuminosityBlend_CenterPixel_MatchesReference()
    {
        await RunBlendModeTest(CbBlendMode.Luminosity, 0xFF8000FFu, 0xFFFF8000u);
    }

    [Test]
    public void AllBlendModes_AllColorPairs_CpuMatchesReference()
    {
        var modes = Enum.GetValues<Etch.ClipBlendGradient.BlendMode>();
        int failureCount = 0;
        var failureLog = new StringBuilder();

        foreach (var mode in modes)
        {
            foreach (uint back in TestColors)
            {
                foreach (uint src in TestColors)
                {
                    var scene = W3CTestTranslator.BuildTwoRectScene(
                        RenderSize, RenderSize, back, src, mode);

                    byte[] actual = SceneRunner.RunCpu(scene, RenderSize, RenderSize);
                    byte[] expected = W3CTestTranslator.ComputeReferenceRgba8(
                        RenderSize, RenderSize, back, src, mode);

                    int diff = MaxChannelDiff(actual, expected);
                    if (diff > Tolerance)
                    {
                        failureCount++;
                        if (failureCount <= 3)
                        {
                            failureLog.AppendLine(
                                $"Mode={mode} back=0x{back:X8} src=0x{src:X8} maxDiff={diff}");
                        }
                    }
                }
            }
        }

        if (failureCount > 0)
        {
            throw new InvalidOperationException(
                $"Failed {failureCount}/{modes.Length * TestColors.Length * TestColors.Length} " +
                $"combinations.\n{failureLog}");
        }
    }

    private static async Task RunBlendModeTest(Etch.ClipBlendGradient.BlendMode mode, uint back, uint src)
    {
        var scene = W3CTestTranslator.BuildTwoRectScene(
            RenderSize, RenderSize, back, src, mode);

        byte[] actual = SceneRunner.RunCpu(scene, RenderSize, RenderSize);
        byte[] expected = W3CTestTranslator.ComputeReferenceRgba8(
            RenderSize, RenderSize, back, src, mode);

        int diff = MaxChannelDiff(actual, expected);
        await Assert.That(diff).IsLessThanOrEqualTo(Tolerance);
    }

    private static int MaxChannelDiff(byte[] a, byte[] b)
    {
        int max = 0;
        int len = Math.Min(a.Length, b.Length);
        for (int i = 0; i < len; i++)
        {
            int d = Math.Abs(a[i] - b[i]);
            if (d > max) max = d;
        }
        return max;
    }
}
