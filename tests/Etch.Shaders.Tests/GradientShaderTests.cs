using System;
using Etch.Shaders;

namespace Etch.Shaders.Tests;

internal sealed class GradientShaderTests
{
    [Test]
    public void ShaderResources_ContainsGradient()
    {
        var span = ShaderResources.gradient;
        if (span.Length == 0)
        {
            throw new InvalidOperationException("gradient shader is empty");
        }
    }

    [Test]
    public void ShaderResources_GradientLayout_HasVertexEntryPoint()
    {
        var entryPoint = ShaderResources.GradientLayout.VertexEntryPoint;
        if (string.IsNullOrEmpty(entryPoint))
        {
            throw new InvalidOperationException("VertexEntryPoint is empty");
        }
    }

    [Test]
    public void ShaderResources_GradientLayout_HasFragmentEntryPoint()
    {
        var entryPoint = ShaderResources.GradientLayout.FragmentEntryPoint;
        if (string.IsNullOrEmpty(entryPoint))
        {
            throw new InvalidOperationException("FragmentEntryPoint is empty");
        }
    }

    [Test]
    public void ShaderResources_GradientLayout_HasGroup0()
    {
        var group0 = ShaderResources.GradientLayout.Group0;
#pragma warning disable CA1508
        if (group0 != 0)
        {
            throw new InvalidOperationException($"Expected Group0=0, got {group0}");
        }
#pragma warning restore CA1508
    }
}