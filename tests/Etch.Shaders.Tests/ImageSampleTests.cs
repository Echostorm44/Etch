using System;
using Etch.Shaders;

namespace Etch.Shaders.Tests;

internal sealed class ImageSampleTests
{
    [Test]
    public void ShaderResources_ContainsImage_sample()
    {
        var span = ShaderResources.image_sample;
        if (span.Length == 0)
        {
            throw new InvalidOperationException("image_sample shader is empty");
        }
    }

    [Test]
    public void ShaderResources_Image_sampleLayout_HasVertexEntryPoint()
    {
        var entryPoint = ShaderResources.Image_sampleLayout.VertexEntryPoint;
        if (string.IsNullOrEmpty(entryPoint))
        {
            throw new InvalidOperationException("VertexEntryPoint is empty");
        }
    }

    [Test]
    public void ShaderResources_Image_sampleLayout_HasFragmentEntryPoint()
    {
        var entryPoint = ShaderResources.Image_sampleLayout.FragmentEntryPoint;
        if (string.IsNullOrEmpty(entryPoint))
        {
            throw new InvalidOperationException("FragmentEntryPoint is empty");
        }
    }

    [Test]
    public void ShaderResources_Image_sampleLayout_HasGroupConstants()
    {
        var group0 = ShaderResources.Image_sampleLayout.Group0;
#pragma warning disable CA1508
        if (group0 != 0)
        {
            throw new InvalidOperationException($"Expected Group0=0, got {group0}");
        }
#pragma warning restore CA1508
    }
}