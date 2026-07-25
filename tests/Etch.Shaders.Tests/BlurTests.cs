using System;
using Etch.Shaders;

namespace Etch.Shaders.Tests;

internal sealed class BlurTests
{
    [Test]
    public void ShaderResources_ContainsDown()
    {
        var span = ShaderResources.down;
        if (span.Length == 0)
        {
            throw new InvalidOperationException("down shader is empty");
        }
    }

    [Test]
    public void ShaderResources_ContainsUp()
    {
        var span = ShaderResources.up;
        if (span.Length == 0)
        {
            throw new InvalidOperationException("up shader is empty");
        }
    }

    [Test]
    public void ShaderResources_DownLayout_HasVertexEntryPoint()
    {
        var entryPoint = ShaderResources.DownLayout.VertexEntryPoint;
        if (string.IsNullOrEmpty(entryPoint))
        {
            throw new InvalidOperationException("VertexEntryPoint is empty");
        }
    }

    [Test]
    public void ShaderResources_DownLayout_HasFragmentEntryPoint()
    {
        var entryPoint = ShaderResources.DownLayout.FragmentEntryPoint;
        if (string.IsNullOrEmpty(entryPoint))
        {
            throw new InvalidOperationException("FragmentEntryPoint is empty");
        }
    }

    [Test]
    public void ShaderResources_UpLayout_HasVertexEntryPoint()
    {
        var entryPoint = ShaderResources.UpLayout.VertexEntryPoint;
        if (string.IsNullOrEmpty(entryPoint))
        {
            throw new InvalidOperationException("VertexEntryPoint is empty");
        }
    }

    [Test]
    public void ShaderResources_UpLayout_HasFragmentEntryPoint()
    {
        var entryPoint = ShaderResources.UpLayout.FragmentEntryPoint;
        if (string.IsNullOrEmpty(entryPoint))
        {
            throw new InvalidOperationException("FragmentEntryPoint is empty");
        }
    }

    [Test]
    public void ShaderResources_DownLayout_HasGroupConstants()
    {
        var group0 = ShaderResources.DownLayout.Group0;
#pragma warning disable CA1508
        if (group0 != 0)
        {
            throw new InvalidOperationException($"Expected Group0=0, got {group0}");
        }
#pragma warning restore CA1508
    }

    [Test]
    public void ShaderResources_UpLayout_HasGroupConstants()
    {
        var group0 = ShaderResources.UpLayout.Group0;
#pragma warning disable CA1508
        if (group0 != 0)
        {
            throw new InvalidOperationException($"Expected Group0=0, got {group0}");
        }
#pragma warning restore CA1508
    }
}