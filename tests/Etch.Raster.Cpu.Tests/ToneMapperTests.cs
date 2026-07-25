using System.Threading.Tasks;

namespace Etch.Raster.Cpu.Tests;

internal sealed class ToneMapperTests
{
    [Test]
    public async Task ApplyReinhard_SdrPixel_StaysInRange()
    {
        var pixels = new[] { Rgba16f.From(0.5f, 0.3f, 0.2f, 1f) };
        ToneMapper.ApplyReinhard(pixels);

        await Assert.That((float)pixels[0].R).IsGreaterThanOrEqualTo(0f);
        await Assert.That((float)pixels[0].R).IsLessThanOrEqualTo(1f);
        await Assert.That((float)pixels[0].A).IsGreaterThanOrEqualTo(0.9f);
    }

    [Test]
    public async Task ApplyReinhard_HdrPixel_ClampedToMaxOne()
    {
        var pixels = new[] { Rgba16f.From(5f, 3f, 2f, 1f) };
        ToneMapper.ApplyReinhard(pixels);

        await Assert.That((float)pixels[0].R).IsLessThanOrEqualTo(1f);
        await Assert.That((float)pixels[0].G).IsLessThanOrEqualTo(1f);
        await Assert.That((float)pixels[0].B).IsLessThanOrEqualTo(1f);
    }

    [Test]
    public async Task ApplyReinhard_AlphaUnchanged()
    {
        var pixels = new[] { Rgba16f.From(10f, 10f, 10f, 0.5f) };
        ToneMapper.ApplyReinhard(pixels);

        await Assert.That((float)pixels[0].A).IsGreaterThanOrEqualTo(0.49f);
        await Assert.That((float)pixels[0].A).IsLessThanOrEqualTo(0.51f);
    }

    [Test]
    public async Task ApplyReinhard_ExtremelyBright_CompressedSignificantly()
    {
        var pixels = new[] { Rgba16f.From(100f, 100f, 100f, 1f) };
        ToneMapper.ApplyReinhard(pixels);

        float r = (float)pixels[0].R;
        await Assert.That(r).IsLessThanOrEqualTo(1f);
    }
}
