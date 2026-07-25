using Etch.Effects.Blur;
using Etch.Gpu.Compositor.Pipelines;
using TUnit;

namespace Etch.Gpu.Compositor.Tests;

public sealed class BlurGpuPipelineTests
{
    [Test]
    public async Task OctaveCount_Radius0_Returns0()
    {
        int count = DualFilterBlur.OctaveCount(0f);
        await Assert.That(count == 0).IsTrue();
    }

    [Test]
    public async Task OctaveCount_Radius1_Returns1()
    {
        int count = DualFilterBlur.OctaveCount(1f);
        await Assert.That(count == 1).IsTrue();
    }

    [Test]
    public async Task OctaveCount_Radius3_Returns2()
    {
        int count = DualFilterBlur.OctaveCount(3f);
        await Assert.That(count == 2).IsTrue();
    }

    [Test]
    public async Task OctaveCount_Radius7_Returns3()
    {
        int count = DualFilterBlur.OctaveCount(7f);
        await Assert.That(count == 3).IsTrue();
    }

    [Test]
    public async Task OctaveCount_Radius15_Returns4()
    {
        int count = DualFilterBlur.OctaveCount(15f);
        await Assert.That(count == 4).IsTrue();
    }

    [Test]
    public async Task OctaveCount_Radius31_Returns5()
    {
        int count = DualFilterBlur.OctaveCount(31f);
        await Assert.That(count == 5).IsTrue();
    }

    [Test]
    public async Task OctaveCount_Radius63_Returns6()
    {
        int count = DualFilterBlur.OctaveCount(63f);
        await Assert.That(count == 6).IsTrue();
    }

    [Test]
    public async Task OctaveCount_Radius64_Returns6Clamped()
    {
        int count = DualFilterBlur.OctaveCount(64f);
        await Assert.That(count == 6).IsTrue();
    }
}
