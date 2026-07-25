using System;
using TUnit;

namespace Etch.Tiling.Tests;

public sealed class TileSizeTests
{
    [Test]
    public void TTile8_Width_Is8()
    {
        if (TTile8.Width != 8)
            throw new InvalidOperationException($"Expected 8, got {TTile8.Width}");
    }

    [Test]
    public void TTile8_Height_Is8()
    {
        if (TTile8.Height != 8)
            throw new InvalidOperationException($"Expected 8, got {TTile8.Height}");
    }

    [Test]
    public void TTile8_PixelCount_Is64()
    {
        if (TTile8.PixelCount != 64)
            throw new InvalidOperationException($"Expected 64, got {TTile8.PixelCount}");
    }

    [Test]
    public void TTile8_Log2Width_Is3()
    {
        if (TTile8.Log2Width != 3)
            throw new InvalidOperationException($"Expected 3, got {TTile8.Log2Width}");
    }

    [Test]
    public void TTile8_Log2Height_Is3()
    {
        if (TTile8.Log2Height != 3)
            throw new InvalidOperationException($"Expected 3, got {TTile8.Log2Height}");
    }

    [Test]
    public void TTile8_Log2Consistency()
    {
        if ((1 << TTile8.Log2Width) != TTile8.Width)
            throw new InvalidOperationException($"Log2Width inconsistency: 1 << {TTile8.Log2Width} = {1 << TTile8.Log2Width}, expected {TTile8.Width}");
        if ((1 << TTile8.Log2Height) != TTile8.Height)
            throw new InvalidOperationException($"Log2Height inconsistency: 1 << {TTile8.Log2Height} = {1 << TTile8.Log2Height}, expected {TTile8.Height}");
    }

    [Test]
    public void TTile16_Width_Is16()
    {
        if (TTile16.Width != 16)
            throw new InvalidOperationException($"Expected 16, got {TTile16.Width}");
    }

    [Test]
    public void TTile16_Height_Is16()
    {
        if (TTile16.Height != 16)
            throw new InvalidOperationException($"Expected 16, got {TTile16.Height}");
    }

    [Test]
    public void TTile16_PixelCount_Is256()
    {
        if (TTile16.PixelCount != 256)
            throw new InvalidOperationException($"Expected 256, got {TTile16.PixelCount}");
    }

    [Test]
    public void TTile16_Log2Width_Is4()
    {
        if (TTile16.Log2Width != 4)
            throw new InvalidOperationException($"Expected 4, got {TTile16.Log2Width}");
    }

    [Test]
    public void TTile16_Log2Height_Is4()
    {
        if (TTile16.Log2Height != 4)
            throw new InvalidOperationException($"Expected 4, got {TTile16.Log2Height}");
    }

    [Test]
    public void TTile16_Log2Consistency()
    {
        if ((1 << TTile16.Log2Width) != TTile16.Width)
            throw new InvalidOperationException($"Log2Width inconsistency: 1 << {TTile16.Log2Width} = {1 << TTile16.Log2Width}, expected {TTile16.Width}");
        if ((1 << TTile16.Log2Height) != TTile16.Height)
            throw new InvalidOperationException($"Log2Height inconsistency: 1 << {TTile16.Log2Height} = {1 << TTile16.Log2Height}, expected {TTile16.Height}");
    }

    [Test]
    public void TTile32_Width_Is32()
    {
        if (TTile32.Width != 32)
            throw new InvalidOperationException($"Expected 32, got {TTile32.Width}");
    }

    [Test]
    public void TTile32_Height_Is32()
    {
        if (TTile32.Height != 32)
            throw new InvalidOperationException($"Expected 32, got {TTile32.Height}");
    }

    [Test]
    public void TTile32_PixelCount_Is1024()
    {
        if (TTile32.PixelCount != 1024)
            throw new InvalidOperationException($"Expected 1024, got {TTile32.PixelCount}");
    }

    [Test]
    public void TTile32_Log2Width_Is5()
    {
        if (TTile32.Log2Width != 5)
            throw new InvalidOperationException($"Expected 5, got {TTile32.Log2Width}");
    }

    [Test]
    public void TTile32_Log2Height_Is5()
    {
        if (TTile32.Log2Height != 5)
            throw new InvalidOperationException($"Expected 5, got {TTile32.Log2Height}");
    }

    [Test]
    public void TTile32_Log2Consistency()
    {
        if ((1 << TTile32.Log2Width) != TTile32.Width)
            throw new InvalidOperationException($"Log2Width inconsistency: 1 << {TTile32.Log2Width} = {1 << TTile32.Log2Width}, expected {TTile32.Width}");
        if ((1 << TTile32.Log2Height) != TTile32.Height)
            throw new InvalidOperationException($"Log2Height inconsistency: 1 << {TTile32.Log2Height} = {1 << TTile32.Log2Height}, expected {TTile32.Height}");
    }

    [Test]
    public void ITileSize_GenericConsumer_Works()
    {
        int result = ComputeArea<TTile16>();
        if (result != 256)
            throw new InvalidOperationException($"Expected 256, got {result}");
    }

    private static int ComputeArea<TTile>()
        where TTile : struct, ITileSize
    {
        return TTile.Width * TTile.Height;
    }
}