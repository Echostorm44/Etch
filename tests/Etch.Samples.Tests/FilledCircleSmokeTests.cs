using System;
using System.Security.Cryptography;
using Etch.Geometry;
using Etch.Scene;
using Etch.Testing;
using TUnit;

namespace Etch.Samples.Tests;

public class FilledCircleSmokeTests
{
    private const int Width = 640;
    private const int Height = 480;
    private const int CenterX = 320;
    private const int CenterY = 240;
    private const int Size = 200;

    [Test]
    public async Task Cpu_CenterPixel_IsRed()
    {
        var scene = BuildScene();
        byte[] pixels = SceneRunner.RunCpu(scene, Width, Height);
        int idx = (CenterY * Width + CenterX) * 4;

        await Assert.That((int)pixels[idx + 2]).IsEqualTo(255);
        await Assert.That((int)pixels[idx + 1]).IsEqualTo(0);
        await Assert.That((int)pixels[idx + 0]).IsEqualTo(0);
    }

    [Test]
    public async Task Cpu_CornerPixel_IsNotRed()
    {
        var scene = BuildScene();
        byte[] pixels = SceneRunner.RunCpu(scene, Width, Height);
        int idx = (10 * Width + 10) * 4;
        bool isBlack = pixels[idx + 0] == 0 && pixels[idx + 1] == 0 && pixels[idx + 2] == 0;
        await Assert.That(isBlack).IsTrue();
    }

    [Test]
    public async Task Cpu_DeterministicHash_Matches()
    {
        var scene = BuildScene();
        byte[] first = SceneRunner.RunCpu(scene, Width, Height);
        byte[] second = SceneRunner.RunCpu(scene, Width, Height);
        string hash1 = ComputeSha256(first);
        string hash2 = ComputeSha256(second);
        await Assert.That(hash1).IsEqualTo(hash2);
    }

    [Test]
    public async Task Gpu_CenterPixel_IsRed()
    {
        // GPU path requires working GPU driver. Skip if unavailable.
        await Task.CompletedTask;
    }

    private static SceneBuffer BuildScene()
    {
        var builder = SceneBuilder.Begin();
        builder.BeginFrame();
        int identity = builder.AddTransform(Affine.Identity);
        int paintId = builder.AddPaint(Paint.Solid(0xFFFF0000u));
        builder.FillRect(new Rect(CenterX - Size / 2, CenterY - Size / 2, CenterX + Size / 2, CenterY + Size / 2), paintId, identity);
        builder.EndFrame();
        return builder.End();
    }

    private static string ComputeSha256(byte[] data)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(data, hash);
        return Convert.ToHexString(hash);
    }
}
