using System.Threading.Tasks;
using Etch.Geometry;
using Etch.Scene;
using Etch.Testing;

namespace Etch.Testing.Tests;

internal sealed class SceneGpuRendererTests
{
    [Test]
    public async Task FillRectSolidRedCenterPixelIsRed()
    {
        var sb = SceneBuilder.Begin();
        sb.BeginFrame();

        var identity = sb.AddTransform(Affine.Identity);
        var red = sb.AddPaint(Paint.Solid(0xFFFF0000));

        sb.FillRect(new Rect(10, 10, 54, 54), red, identity);
        sb.EndFrame();

        var scene = sb.End();
        byte[] output = SceneRunner.RunGpu(scene, 64, 64);

        // Center of the 64×64 image, inside the red rect
        int cx = 32;
        int cy = 32;
        int idx = (cy * 64 + cx) * 4;

        byte b = output[idx + 0];
        byte g = output[idx + 1];
        byte r = output[idx + 2];
        byte a = output[idx + 3];

        await Assert.That((int)r).IsGreaterThan(200);
        await Assert.That((int)g).IsEqualTo(0);
        await Assert.That((int)b).IsEqualTo(0);
        await Assert.That((int)a).IsGreaterThan(200);
    }
}
