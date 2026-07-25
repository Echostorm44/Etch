using System;
using Etch.ClipBlendGradient;
using Etch.Gpu.Compositor.Pipelines;
using TUnit;

namespace Etch.Gpu.Compositor.Tests;

internal sealed class BlendModeDispatcherTests
{
    [Test]
    public async Task DrawGroup_Constructor_SetsFields()
    {
        var src = new LinearColor(0.25f, 0.5f, 0.75f, 1.0f);
        var dst = new LinearColor(0.1f, 0.2f, 0.3f, 0.8f);

        var group = new DrawGroup(BlendMode.Multiply, src, dst, 10);

        await Assert.That(group.BlendMode).IsEqualTo(BlendMode.Multiply);
        await Assert.That(group.SrcColor.R).IsEqualTo(0.25f);
        await Assert.That(group.InstanceCount).IsEqualTo(10);
    }

    [Test]
    public async Task GetDistinctBlendModeCount_Empty_ReturnsZero()
    {
        var groups = Array.Empty<DrawGroup>();
        var count = BlendModeDispatcher.GetDistinctBlendModeCount(groups);
        await Assert.That(count).IsEqualTo(0);
    }

    [Test]
    public async Task GetDistinctBlendModeCount_SingleMode_ReturnsOne()
    {
        var src = new LinearColor(1, 0, 0, 1);
        var dst = new LinearColor(0, 1, 0, 1);
        var groups = new[]
        {
            new DrawGroup(BlendMode.Normal, src, dst, 1),
            new DrawGroup(BlendMode.Normal, src, dst, 2),
            new DrawGroup(BlendMode.Normal, src, dst, 3)
        };

        var count = BlendModeDispatcher.GetDistinctBlendModeCount(groups);
        await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async Task GetDistinctBlendModeCount_MultipleModes_ReturnsDistinctCount()
    {
        var src = new LinearColor(1, 0, 0, 1);
        var dst = new LinearColor(0, 1, 0, 1);
        var groups = new[]
        {
            new DrawGroup(BlendMode.Normal, src, dst, 1),
            new DrawGroup(BlendMode.Multiply, src, dst, 2),
            new DrawGroup(BlendMode.Normal, src, dst, 3),
            new DrawGroup(BlendMode.Screen, src, dst, 4),
            new DrawGroup(BlendMode.Multiply, src, dst, 5)
        };

        var count = BlendModeDispatcher.GetDistinctBlendModeCount(groups);
        await Assert.That(count).IsEqualTo(3);
    }

    [Test]
    public async Task GetDistinctBlendModeCount_All16Modes_Returns16()
    {
        var src = new LinearColor(1, 0, 0, 1);
        var dst = new LinearColor(0, 1, 0, 1);
        var groups = new DrawGroup[16];

        for (int i = 0; i < 16; i++)
        {
            groups[i] = new DrawGroup((BlendMode)i, src, dst, i + 1);
        }

        var count = BlendModeDispatcher.GetDistinctBlendModeCount(groups);
        await Assert.That(count).IsEqualTo(16);
    }

    [Test]
    public async Task BlendMode_AllModes_HaveValues0To15()
    {
        await Assert.That((int)BlendMode.Normal).IsEqualTo(0);
        await Assert.That((int)BlendMode.Multiply).IsEqualTo(1);
        await Assert.That((int)BlendMode.Screen).IsEqualTo(2);
        await Assert.That((int)BlendMode.Overlay).IsEqualTo(3);
        await Assert.That((int)BlendMode.Darken).IsEqualTo(4);
        await Assert.That((int)BlendMode.Lighten).IsEqualTo(5);
        await Assert.That((int)BlendMode.ColorDodge).IsEqualTo(6);
        await Assert.That((int)BlendMode.ColorBurn).IsEqualTo(7);
        await Assert.That((int)BlendMode.HardLight).IsEqualTo(8);
        await Assert.That((int)BlendMode.SoftLight).IsEqualTo(9);
        await Assert.That((int)BlendMode.Difference).IsEqualTo(10);
        await Assert.That((int)BlendMode.Exclusion).IsEqualTo(11);
        await Assert.That((int)BlendMode.Hue).IsEqualTo(12);
        await Assert.That((int)BlendMode.Saturation).IsEqualTo(13);
        await Assert.That((int)BlendMode.Color).IsEqualTo(14);
        await Assert.That((int)BlendMode.Luminosity).IsEqualTo(15);
    }

    [Test]
    public async Task BlendMode_SeparableModes_Are0To11()
    {
        for (int i = 0; i <= 11; i++)
        {
            await Assert.That((int)(BlendMode)i).IsLessThan(12);
        }
    }

    [Test]
    public async Task BlendMode_NonSeparableModes_Are12To15()
    {
        await Assert.That((int)BlendMode.Hue).IsEqualTo(12);
        await Assert.That((int)BlendMode.Saturation).IsEqualTo(13);
        await Assert.That((int)BlendMode.Color).IsEqualTo(14);
        await Assert.That((int)BlendMode.Luminosity).IsEqualTo(15);
    }
}
