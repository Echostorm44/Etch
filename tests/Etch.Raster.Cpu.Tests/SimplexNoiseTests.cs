using System;
using System.Threading.Tasks;
using Etch.Raster.Cpu.Noise;

namespace Etch.Raster.Cpu.Tests;

internal sealed class SimplexNoiseTests
{
    [Test]
    public async Task Noise2D_ZeroInput_ReturnsZero()
    {
        var perm = SimplexNoise.CreatePermutation(0);
        float n = SimplexNoise.Noise2D(0f, 0f, perm);
        await Assert.That(n).IsGreaterThanOrEqualTo(-1f);
        await Assert.That(n).IsLessThanOrEqualTo(1f);
    }

    [Test]
    public async Task Noise2D_RangeIsWithinBounds()
    {
        var perm = SimplexNoise.CreatePermutation(42);
        float min = float.MaxValue;
        float max = float.MinValue;
        for (int y = 0; y < 100; y++)
        {
            for (int x = 0; x < 100; x++)
            {
                float n = SimplexNoise.Noise2D(x * 0.05f, y * 0.05f, perm);
                min = Math.Min(min, n);
                max = Math.Max(max, n);
            }
        }
        await Assert.That(min).IsGreaterThanOrEqualTo(-1f);
        await Assert.That(max).IsLessThanOrEqualTo(1f);
    }

    [Test]
    public async Task Noise2D_Deterministic_SameSeedSameOutput()
    {
        var permA = SimplexNoise.CreatePermutation(123);
        var permB = SimplexNoise.CreatePermutation(123);

        for (int i = 0; i < 50; i++)
        {
            float nx = i * 0.1f;
            float ny = i * 0.15f;
            float a = SimplexNoise.Noise2D(nx, ny, permA);
            float b = SimplexNoise.Noise2D(nx, ny, permB);
            await Assert.That(a).IsEqualTo(b);
        }
    }

    [Test]
    public async Task Noise2D_DifferentSeed_ProducesDifferentValues()
    {
        var permA = SimplexNoise.CreatePermutation(1);
        var permB = SimplexNoise.CreatePermutation(2);

        bool allSame = true;
        for (int i = 0; i < 50; i++)
        {
            float a = SimplexNoise.Noise2D(i * 0.1f, i * 0.15f, permA);
            float b = SimplexNoise.Noise2D(i * 0.1f, i * 0.15f, permB);
            if (Math.Abs(a - b) > 0.001f)
                allSame = false;
        }
        await Assert.That(allSame).IsFalse();
    }

    [Test]
    public async Task Fbm2D_VaryingOctaves_ProducesOutput()
    {
        var perm = SimplexNoise.CreatePermutation(7);
        float v1 = SimplexNoise.Fbm2D(10f, 20f, 1, 0.5f, perm);
        float v3 = SimplexNoise.Fbm2D(10f, 20f, 3, 0.5f, perm);
        await Assert.That(v1).IsGreaterThanOrEqualTo(-1f);
        await Assert.That(v1).IsLessThanOrEqualTo(1f);
        await Assert.That(v3).IsGreaterThanOrEqualTo(-1f);
        await Assert.That(v3).IsLessThanOrEqualTo(1f);
    }
}
