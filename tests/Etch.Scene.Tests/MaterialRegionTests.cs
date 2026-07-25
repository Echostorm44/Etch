using System;
using System.Threading.Tasks;
using Etch.Geometry;

namespace Etch.Scene.Tests;

internal sealed class MaterialRegionTests
{
    [Test]
    public async Task SceneBuilder_DrawMaterialRegion_WritesCommand()
    {
        SceneBuffer scene;
        {
            var sb = SceneBuilder.Begin();
            sb.BeginFrame();
            var identity = sb.AddTransform(Affine.Identity);
            sb.DrawMaterialRegion(new Rect(10, 20, 200, 100), 8f, identity);
            sb.EndFrame();
            scene = sb.End();
        }

        await Assert.That(scene.CommandCount).IsGreaterThanOrEqualTo(3);
    }

    [Test]
    public async Task MaterialRegion_Serialization_RoundTrip()
    {
        SceneBuffer scene;
        {
            var sb = SceneBuilder.Begin();
            sb.BeginFrame();
            var identity = sb.AddTransform(Affine.Identity);
            sb.DrawMaterialRegion(new Rect(10, 20, 200, 100), 8f, identity);
            sb.EndFrame();
            scene = sb.End();
        }

        int requiredSize = Serialization.SceneWriter.GetRequiredSize(scene);
        var buffer = new byte[requiredSize];
        Serialization.SceneWriter.Write(scene, buffer);

        var restored = Serialization.SceneReader.Read(buffer);

        var commands = restored.Commands;
        bool found = false;
        foreach (var cmd in commands)
        {
            if (cmd.Op == SceneOpcode.DrawMaterialRegion)
            {
                found = true;
                await Assert.That(cmd.DrawMaterialRegion.Radius).IsEqualTo(8f);
                break;
            }
        }
        await Assert.That(found).IsTrue();
    }

    [Test]
    public async Task MaterialRegion_InvalidTransform_Validated()
    {
        bool threw = false;
        var sb = SceneBuilder.Begin();
        try
        {
            sb.DrawMaterialRegion(new Rect(0, 0, 100, 100), 0f, 999);
        }
        catch
        {
            threw = true;
        }
        finally
        {
            sb.Dispose();
        }

        await Assert.That(threw).IsTrue();
    }

    [Test]
    public async Task MaterialRegion_Payload_DefaultValues()
    {
        var payload = new DrawMaterialRegionPayload();
        await Assert.That(payload.RectId).IsEqualTo(0);
        await Assert.That(payload.TransformId).IsEqualTo(0);
        await Assert.That(payload.Radius).IsEqualTo(0f);
    }
}
