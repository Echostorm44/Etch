using Etch.Gpu.Compositor.Pipelines;
using Etch.Gpu.Pipelines;
using TUnit;

namespace Etch.Gpu.Compositor.Tests;

public sealed class GlyphAtlasGpuPipelineTests
{
    [Test]
    public async Task SubpixelMode_HasExpectedValues()
    {
        await Assert.That((int)SubpixelMode.None).IsEqualTo(0);
        await Assert.That((int)SubpixelMode.ThreeChannel).IsEqualTo(1);
        await Assert.That((int)SubpixelMode.FiveChannel).IsEqualTo(2);
    }

    [Test]
    public async Task GlyphInstance_HasCorrectFieldCount()
    {
        var instance = new GlyphInstance
        {
            AtlasUvX = 0.1f,
            AtlasUvY = 0.2f,
            QuadWidth = 10.0f,
            QuadHeight = 20.0f,
            SubpixelOffset = 0.33f
        };

        await Assert.That(instance.AtlasUvX).IsEqualTo(0.1f);
        await Assert.That(instance.AtlasUvY).IsEqualTo(0.2f);
        await Assert.That(instance.QuadWidth).IsEqualTo(10.0f);
        await Assert.That(instance.QuadHeight).IsEqualTo(20.0f);
        await Assert.That(instance.SubpixelOffset).IsEqualTo(0.33f);
    }

    [Test]
    public async Task GlyphInstance_StructLayout_IsSequential()
    {
        int expectedSize = 20;
        await Assert.That(System.Runtime.InteropServices.Marshal.SizeOf<GlyphInstance>()).IsEqualTo(expectedSize);
    }

    [Test]
    public async Task GlyphInstance_SubpixelOffset_Phases()
    {
        var phase0 = new GlyphInstance { SubpixelOffset = 0.0f };
        var phase1 = new GlyphInstance { SubpixelOffset = 1.0f / 3.0f };
        var phase2 = new GlyphInstance { SubpixelOffset = 2.0f / 3.0f };

        await Assert.That(phase0.SubpixelOffset).IsEqualTo(0.0f);
        await Assert.That(MathF.Abs(phase1.SubpixelOffset - 0.333f) < 0.001f).IsTrue();
        await Assert.That(MathF.Abs(phase2.SubpixelOffset - 0.666f) < 0.001f).IsTrue();
    }

    [Test]
    public async Task GlyphInstance_DefaultValues_AreZero()
    {
        var instance = default(GlyphInstance);
        await Assert.That(instance.AtlasUvX).IsEqualTo(0f);
        await Assert.That(instance.AtlasUvY).IsEqualTo(0f);
        await Assert.That(instance.QuadWidth).IsEqualTo(0f);
        await Assert.That(instance.QuadHeight).IsEqualTo(0f);
        await Assert.That(instance.SubpixelOffset).IsEqualTo(0f);
    }
}