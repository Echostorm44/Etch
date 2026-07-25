using System;
using TUnit;
using Etch.Effects.Blur;

namespace Etch.Effects.Tests;

public sealed class DualFilterBlurTests
{
    [Test]
    public async Task OctaveCount_Radius0_Returns0()
    {
        int count = DualFilterBlur.OctaveCount(0f);
        int expected = 0;
        await Assert.That(count == expected).IsTrue();
    }

    [Test]
    public async Task OctaveCount_RadiusNegative_Returns0()
    {
        int count = DualFilterBlur.OctaveCount(-5f);
        int expected = 0;
        await Assert.That(count == expected).IsTrue();
    }

    [Test]
    public async Task OctaveCount_Radius1_Returns1()
    {
        int count = DualFilterBlur.OctaveCount(1f);
        int expected = 1;
        await Assert.That(count == expected).IsTrue();
    }

    [Test]
    public async Task OctaveCount_Radius3_Returns2()
    {
        int count = DualFilterBlur.OctaveCount(3f);
        int expected = 2;
        await Assert.That(count == expected).IsTrue();
    }

    [Test]
    public async Task OctaveCount_Radius7_Returns3()
    {
        int count = DualFilterBlur.OctaveCount(7f);
        int expected = 3;
        await Assert.That(count == expected).IsTrue();
    }

    [Test]
    public async Task OctaveCount_Radius15_Returns4()
    {
        int count = DualFilterBlur.OctaveCount(15f);
        int expected = 4;
        await Assert.That(count == expected).IsTrue();
    }

    [Test]
    public async Task OctaveCount_Radius31_Returns5()
    {
        int count = DualFilterBlur.OctaveCount(31f);
        int expected = 5;
        await Assert.That(count == expected).IsTrue();
    }

    [Test]
    public async Task OctaveCount_Radius63_Returns6()
    {
        int count = DualFilterBlur.OctaveCount(63f);
        int expected = 6;
        await Assert.That(count == expected).IsTrue();
    }

    [Test]
    public async Task OctaveCount_Radius64_Returns6Clamped()
    {
        int count = DualFilterBlur.OctaveCount(64f);
        int expected = 6;
        await Assert.That(count == expected).IsTrue();
    }

    [Test]
    public async Task BlurParams_HasCorrectRadius()
    {
        var p = new BlurParams(16f);
        await Assert.That(p.RadiusPx == 16f).IsTrue();
    }

    [Test]
    public async Task BlurParams_HasDefaultEdge()
    {
        var p = new BlurParams(16f);
        await Assert.That((int)p.Edge == 0).IsTrue();
    }

    [Test]
    public async Task BlurParams_CanSetEdge()
    {
        var p = new BlurParams(16f, BlurEdge.Clamp);
        await Assert.That(p.Edge == BlurEdge.Clamp).IsTrue();
    }
}
