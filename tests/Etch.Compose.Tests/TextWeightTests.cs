using Etch.Compose.Cpu;

namespace Etch.Compose.Tests;

/// <summary>
/// The adaptive text-weight curve, pinned (the GPU runs the same arithmetic — the text parity
/// scenes hold the shader to it). Coverage c is first given a displayed weight: dark-on-light gets
/// pc = 1 − (1 − c)^γ; light-on-dark is moved toward linear coverage as its contrast grows, by the
/// light-weight strength. The displayed weight is then mapped to the linear-light blend alpha for
/// its polarity: 1 − lin(1 − pc) dark-on-light, lin(w) light-on-dark, blended by smoothstep over a
/// ±0.1 luminance band. Luminances are of sRGB-encoded colour.
/// </summary>
internal sealed class TextWeightTests
{
    private const float Gamma = 1.5f;

    private static double Lin(double c) => c > 0.04045 ? Math.Pow((c + 0.055) / 1.055, 2.4) : c / 12.92;

    private static double Pc(double c) => 1 - Math.Pow(1 - c, Gamma);

    // The curve written out independently, in double precision.
    private static double Expected(double c, double fg, double bg, double strength)
    {
        double pc = Pc(c);
        double rel = Math.Clamp(fg - bg, 0, 1);
        double wLight = c + (pc - c) * (1 - strength * rel);
        double aDark = 1 - Lin(1 - pc);
        double aLight = Lin(wLight);
        double t = Math.Clamp((fg - bg + 0.1) / 0.2, 0, 1);
        double polarity = t * t * (3 - 2 * t);
        return aDark + (aLight - aDark) * polarity;
    }

    [Test]
    public async Task GammaZero_IsLinearCoverage()
    {
        for (float c = 0f; c <= 1f; c += 0.05f)
        {
            await Assert.That(CpuShading.WeightedCoverage(c, 0.5f, 0.5f, 0f, 1f)).IsEqualTo(c);
        }
    }

    [Test]
    [Arguments(0f, 1f, 1f)]
    [Arguments(0f, 1f, 0f)]
    [Arguments(1f, 0f, 1f)]
    [Arguments(1f, 0f, 0f)]
    [Arguments(0.5f, 1f, 1f)]
    [Arguments(0.5f, 0f, 1f)]
    [Arguments(0.55f, 0.5f, 1f)]
    [Arguments(0.6f, 0f, 0.5f)]
    public async Task Curve_IsPinned(float fg, float bg, float strength)
    {
        for (int i = 0; i <= 255; i++)
        {
            float c = i / 255f;
            double actual = CpuShading.WeightedCoverage(c, fg, bg, Gamma, strength);
            await Assert.That(Math.Abs(actual - Expected(c, fg, bg, strength))).IsLessThan(2e-6);
        }
    }

    [Test]
    public async Task Polarities_BehaveAsDesigned()
    {
        for (float c = 0.05f; c < 1f; c += 0.05f)
        {
            // Dark on light: the full perceptual weight, whatever the strength.
            double dark = CpuShading.WeightedCoverage(c, 0f, 1f, Gamma, 1f);
            await Assert.That(Math.Abs(dark - (1 - Lin(1 - Pc(c))))).IsLessThan(1e-6);
            await Assert.That(CpuShading.WeightedCoverage(c, 0f, 1f, Gamma, 0f)).IsEqualTo((float)dark);

            // White on black at full strength: linear coverage, displayed as c.
            double white = CpuShading.WeightedCoverage(c, 1f, 0f, Gamma, 1f);
            await Assert.That(Math.Abs(white - Lin(c))).IsLessThan(1e-6);

            // Strength 0: the symmetric weight, pc displayed light-on-dark too.
            double symmetric = CpuShading.WeightedCoverage(c, 1f, 0f, Gamma, 0f);
            await Assert.That(Math.Abs(symmetric - Lin(Pc(c)))).IsLessThan(1e-6);
            await Assert.That(symmetric).IsGreaterThan(white);
        }
    }

    [Test]
    public async Task Weight_FollowsTheLocalBackground()
    {
        // The same mid-gray foreground: dark-on-light on white (full weight), light-on-dark on black.
        const float c = 0.5f;
        double onWhite = CpuShading.WeightedCoverage(c, 0.5f, 1f, Gamma, 1f);
        double onBlack = CpuShading.WeightedCoverage(c, 0.5f, 0f, Gamma, 1f);
        await Assert.That(Math.Abs(onWhite - (1 - Lin(1 - Pc(c))))).IsLessThan(1e-6);
        await Assert.That(Math.Abs(onBlack - Expected(c, 0.5, 0, 1))).IsLessThan(1e-6);
        await Assert.That(onWhite).IsNotEqualTo(onBlack);
    }
}
