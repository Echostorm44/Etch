using System;
using System.Threading.Tasks;
using Etch.Geometry;

namespace Etch.Scene.Tests;

internal sealed class NoiseTests
{
    [Test]
    public async Task NoiseSpec_DefaultValues_Valid()
    {
        var spec = new NoiseSpec(0.02f, 3, 0.5f, 42, 0.15f);
        await Assert.That(spec.Scale).IsEqualTo(0.02f);
        await Assert.That(spec.Octaves).IsEqualTo(3);
        await Assert.That(spec.Persistence).IsEqualTo(0.5f);
        await Assert.That(spec.Seed).IsEqualTo(42u);
        await Assert.That(spec.Opacity).IsEqualTo(0.15f);
    }

    [Test]
    public async Task NoiseSpec_ZeroOctaves_Throws()
    {
        await Assert.That(() => new NoiseSpec(0.02f, 0, 0.5f, 42, 0.15f)).Throws<EtchException>();
    }

    [Test]
    public async Task NoiseSpec_NegativeScale_Throws()
    {
        await Assert.That(() => new NoiseSpec(0f, 3, 0.5f, 42, 0.15f)).Throws<EtchException>();
    }

    [Test]
    public async Task NoiseSpec_ZeroOpacity_Throws()
    {
        await Assert.That(() => new NoiseSpec(0.02f, 3, 0.5f, 42, 0f)).Throws<EtchException>();
    }

    [Test]
    public async Task NoiseSpec_MoreThan8Octaves_Throws()
    {
        await Assert.That(() => new NoiseSpec(0.02f, 9, 0.5f, 42, 0.15f)).Throws<EtchException>();
    }

    [Test]
    public async Task SceneBuilder_AddNoiseSpec_ReturnsIncrementingIds()
    {
        int id0, id1;
        var sb = SceneBuilder.Begin();
        try
        {
            var spec = new NoiseSpec(0.02f, 3, 0.5f, 42, 0.15f);
            id0 = sb.AddNoiseSpec(spec);
            id1 = sb.AddNoiseSpec(spec);
        }
        finally
        {
            sb.Dispose();
        }

        await Assert.That(id0).IsEqualTo(0);
        await Assert.That(id1).IsEqualTo(1);
    }

    [Test]
    public async Task SceneBuilder_NoisePaint_SerializationRoundTrip()
    {
        SceneBuffer scene;
        {
            var sb = SceneBuilder.Begin();
            sb.BeginFrame();
            var spec = new NoiseSpec(0.02f, 3, 0.5f, 42, 0.15f);
            int noiseId = sb.AddNoiseSpec(spec);
            int paintId = sb.AddPaint(Paint.Noise((uint)noiseId));
            var identity = sb.AddTransform(Affine.Identity);
            sb.FillRect(new Geometry.Rect(0, 0, 200, 200), paintId, identity);
            sb.EndFrame();
            scene = sb.End();
        }

        int requiredSize = Serialization.SceneWriter.GetRequiredSize(scene);
        var buffer = new byte[requiredSize];
        Serialization.SceneWriter.Write(scene, buffer);

        var restored = Serialization.SceneReader.Read(buffer);

        await Assert.That(restored.NoiseSpecCount).IsEqualTo(1);
        var restoredSpec = restored.GetNoiseSpec(0);
        await Assert.That(restoredSpec.Scale).IsEqualTo(0.02f);
        await Assert.That(restoredSpec.Octaves).IsEqualTo(3);
        await Assert.That(restoredSpec.Persistence).IsEqualTo(0.5f);
        await Assert.That(restoredSpec.Seed).IsEqualTo(42u);
        await Assert.That(restoredSpec.Opacity).IsEqualTo(0.15f);
    }

    [Test]
    public async Task NoisePaint_WithMeshGradient_Coexists()
    {
        SceneBuffer scene;
        {
            var sb = SceneBuilder.Begin();
            sb.BeginFrame();
            var spec = new NoiseSpec(0.02f, 3, 0.5f, 42, 0.15f);
            int noiseId = sb.AddNoiseSpec(spec);
            int noisePaint = sb.AddPaint(Paint.Noise((uint)noiseId));
            int solidPaint = sb.AddPaint(Paint.Solid(0xFFFF0000));
            var identity = sb.AddTransform(Affine.Identity);
            sb.FillRect(new Geometry.Rect(0, 0, 100, 100), noisePaint, identity);
            sb.FillRect(new Geometry.Rect(100, 0, 100, 100), solidPaint, identity);
            sb.EndFrame();
            scene = sb.End();
        }

        await Assert.That(scene.NoiseSpecCount).IsEqualTo(1);
        await Assert.That(scene.PaintCount).IsEqualTo(2);
    }
}
