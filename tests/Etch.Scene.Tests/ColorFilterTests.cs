using System;
using System.Threading.Tasks;
using Etch.Geometry;

namespace Etch.Scene.Tests;

internal sealed class ColorFilterTests
{
    [Test]
    public async Task ColorFilter_Identity_IsIdentity()
    {
        var filter = ColorFilter.Identity;
        await Assert.That(filter.IsIdentity).IsTrue();
    }

    [Test]
    public async Task ColorFilter_Grayscale_NotIdentity()
    {
        var filter = ColorFilter.Grayscale;
        await Assert.That(filter.IsIdentity).IsFalse();
    }

    [Test]
    public async Task ColorFilter_Brightness_GeneratesCorrectMatrix()
    {
        var filter = ColorFilter.Brightness(1.5f);
        await Assert.That(filter.M11).IsEqualTo(1.5f);
        await Assert.That(filter.M22).IsEqualTo(1.5f);
        await Assert.That(filter.M44).IsEqualTo(1f);
    }

    [Test]
    public async Task SceneBuilder_PushPopColorFilter()
    {
        SceneBuffer scene;
        {
            var sb = SceneBuilder.Begin();
            sb.BeginFrame();
            int filterId = sb.AddColorFilter(ColorFilter.Grayscale);
            sb.PushColorFilter(filterId);
            var identity = sb.AddTransform(Affine.Identity);
            int red = sb.AddPaint(Paint.Solid(0xFFFF0000));
            sb.FillRect(new Rect(0, 0, 100, 100), red, identity);
            sb.PopColorFilter();
            sb.EndFrame();
            scene = sb.End();
        }

        bool hasPush = false;
        bool hasPop = false;
        foreach (var cmd in scene.Commands)
        {
            if (cmd.Op == SceneOpcode.PushColorFilter) hasPush = true;
            if (cmd.Op == SceneOpcode.PopColorFilter) hasPop = true;
        }
        await Assert.That(hasPush).IsTrue();
        await Assert.That(hasPop).IsTrue();
        await Assert.That(scene.ColorFilterCount).IsEqualTo(1);
    }

    [Test]
    public async Task ColorFilter_Serialization_RoundTrip()
    {
        SceneBuffer scene;
        {
            var sb = SceneBuilder.Begin();
            sb.BeginFrame();
            int filterId = sb.AddColorFilter(ColorFilter.Sepia);
            sb.PushColorFilter(filterId);
            var identity = sb.AddTransform(Affine.Identity);
            sb.FillRect(new Rect(0, 0, 100, 100), sb.AddPaint(Paint.Solid(0xFFFF0000)), identity);
            sb.PopColorFilter();
            sb.EndFrame();
            scene = sb.End();
        }

        int requiredSize = Serialization.SceneWriter.GetRequiredSize(scene);
        var buffer = new byte[requiredSize];
        Serialization.SceneWriter.Write(scene, buffer);

        var restored = Serialization.SceneReader.Read(buffer);
        await Assert.That(restored.ColorFilterCount).IsEqualTo(1);

        var filter = restored.GetColorFilter(0);
        await Assert.That(filter.M11).IsGreaterThan(0.3f);
        await Assert.That(filter.M45).IsEqualTo(0f);
    }

    [Test]
    public async Task ColorFilter_InvalidPush_Throws()
    {
        bool threw = false;
        var sb = SceneBuilder.Begin();
        try { sb.PushColorFilter(999); }
        catch { threw = true; }
        finally { sb.Dispose(); }
        await Assert.That(threw).IsTrue();
    }
}
