using System;
using TUnit;
using Etch.Effects.Image;

namespace Etch.Effects.Tests;

public sealed class MipmapBuilderTests
{
    [Test]
    public async Task GetMipLevelCount_1024x1024_Returns11()
    {
        int count = MipmapBuilder.GetMipLevelCount(1024, 1024);
        int expectedCount = 11;
        await Assert.That(count == expectedCount).IsTrue();
    }

    [Test]
    public async Task GetMipLevelCount_1x1_Returns1()
    {
        int count = MipmapBuilder.GetMipLevelCount(1, 1);
        int expectedCount = 1;
        await Assert.That(count == expectedCount).IsTrue();
    }

    [Test]
    public async Task GetMipSize_HalvesCorrectly()
    {
        int baseSize = 1024;
        int expectedSize = baseSize;
        for (int level = 0; level < 11; level++)
        {
            int actualSize = MipmapBuilder.GetMipSize(baseSize, level);
            await Assert.That(actualSize == expectedSize).IsTrue();
            expectedSize = Math.Max(1, expectedSize >> 1);
        }
    }

    [Test]
    public async Task GetTotalBytes_1024x1024x4_IsCorrect()
    {
        int bytes = MipmapBuilder.GetTotalBytes(1024, 1024, ImageFormat.Rgba8Unorm);
        int expectedBytes = 0;
        int w = 1024;
        int h = 1024;
        while (w > 1 || h > 1)
        {
            expectedBytes += w * h * 4;
            w >>= 1;
            h >>= 1;
            if (w == 0) w = 1;
            if (h == 0) h = 1;
        }
        expectedBytes += w * h * 4;
        await Assert.That(bytes == expectedBytes).IsTrue();
    }

    [Test]
    public async Task GetTotalBytes_2x2x4_Returns20()
    {
        int bytes = MipmapBuilder.GetTotalBytes(2, 2, ImageFormat.Rgba8Unorm);
        int expectedBytes = 2 * 2 * 4 + 1 * 1 * 4;
        await Assert.That(bytes == expectedBytes).IsTrue();
    }

    [Test]
    public async Task GetTotalBytes_4x4x4_Returns84()
    {
        int bytes = MipmapBuilder.GetTotalBytes(4, 4, ImageFormat.Rgba8Unorm);
        int expected = 4 * 4 * 4 + 2 * 2 * 4 + 1 * 1 * 4;
        await Assert.That(bytes == expected).IsTrue();
    }

    [Test]
    public async Task Build_BoxFilter_4IdenticalPixels_ProducesIdenticalOutput()
    {
        byte[] srcData = new byte[2 * 2 * 4];
        for (int i = 0; i < 4; i++)
        {
            srcData[i * 4] = 128;
            srcData[i * 4 + 1] = 64;
            srcData[i * 4 + 2] = 192;
            srcData[i * 4 + 3] = 255;
        }

        byte[] destData = new byte[1 * 1 * 4];

        MipmapBuilder.Build(srcData, 2, 2, ImageFormat.Rgba8Unorm, destData);

        byte expectedR = 128;
        byte expectedG = 64;
        byte expectedB = 192;
        byte expectedA = 255;
        await Assert.That(destData[0] == expectedR).IsTrue();
        await Assert.That(destData[1] == expectedG).IsTrue();
        await Assert.That(destData[2] == expectedB).IsTrue();
        await Assert.That(destData[3] == expectedA).IsTrue();
    }

    [Test]
    public async Task Build_2x2Checker_Level1_IsUniform()
    {
        byte[] srcData = new byte[2 * 2 * 4];
        srcData[0] = 0;
        srcData[1] = 0;
        srcData[2] = 0;
        srcData[3] = 255;
        srcData[4] = 255;
        srcData[5] = 255;
        srcData[6] = 255;
        srcData[7] = 255;
        srcData[8] = 255;
        srcData[9] = 255;
        srcData[10] = 255;
        srcData[11] = 255;
        srcData[12] = 0;
        srcData[13] = 0;
        srcData[14] = 0;
        srcData[15] = 255;

        byte[] destData = new byte[1 * 1 * 4];

        MipmapBuilder.Build(srcData, 2, 2, ImageFormat.Rgba8Unorm, destData);

        byte expected = 127;
        byte epsilon = 1;
        await Assert.That(destData[0] <= expected + epsilon).IsTrue();
        await Assert.That(destData[0] >= expected - epsilon).IsTrue();
        await Assert.That(destData[1] <= expected + epsilon).IsTrue();
        await Assert.That(destData[1] >= expected - epsilon).IsTrue();
        await Assert.That(destData[2] <= expected + epsilon).IsTrue();
        await Assert.That(destData[2] >= expected - epsilon).IsTrue();
    }

    [Test]
    public async Task Build_ZeroManagedAlloc()
    {
        byte[] srcData = new byte[64 * 64 * 4];
        int destBytes = MipmapBuilder.GetTotalBytes(64, 64, ImageFormat.Rgba8Unorm);
        byte[] destData = new byte[destBytes];
        int gen0Before = GC.CollectionCount(0);
        int gen1Before = GC.CollectionCount(1);
        int gen2Before = GC.CollectionCount(2);

        MipmapBuilder.Build(srcData, 64, 64, ImageFormat.Rgba8Unorm, destData);

        int gen0After = GC.CollectionCount(0);
        int gen1After = GC.CollectionCount(1);
        int gen2After = GC.CollectionCount(2);
        await Assert.That(gen0After == gen0Before).IsTrue();
        await Assert.That(gen1After == gen1Before).IsTrue();
        await Assert.That(gen2After == gen2Before).IsTrue();
    }
}
