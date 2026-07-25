using System;
using Etch.Gpu.Pipelines;
using Etch.Shaders;

namespace Etch.Gpu.Tests;

internal sealed class StripCoveragePipelineTests
{
    [Test]
    public void ShaderResources_ContainsStripCoverage()
    {
        var span = ShaderResources.strip_coverage;
        if (span.Length == 0)
        {
            throw new InvalidOperationException("strip_coverage shader is empty");
        }
    }

    [Test]
    public void ShaderResources_StripCoverageLayout_HasVertexEntryPoint()
    {
        var entryPoint = ShaderResources.Strip_coverageLayout.VertexEntryPoint;
        if (string.IsNullOrEmpty(entryPoint))
        {
            throw new InvalidOperationException("VertexEntryPoint is empty");
        }
    }

    [Test]
    public void ShaderResources_StripCoverageLayout_HasFragmentEntryPoint()
    {
        var entryPoint = ShaderResources.Strip_coverageLayout.FragmentEntryPoint;
        if (string.IsNullOrEmpty(entryPoint))
        {
            throw new InvalidOperationException("FragmentEntryPoint is empty");
        }
    }

    [Test]
    public void ShaderResources_StripCoverageLayout_HasGroupConstants()
    {
        var group0 = ShaderResources.Strip_coverageLayout.Group0;
        var group2 = ShaderResources.Strip_coverageLayout.Group2;

#pragma warning disable CA1508
        if (group0 != 0 || group2 != 2)
        {
            throw new InvalidOperationException($"Group0={group0}, Group2={group2}");
        }
#pragma warning restore CA1508
    }

    [Test]
    public void TileInfo_IsPublicAndAccessible()
    {
        var info = new TileInfo
        {
            TileIndex = 1,
            StripStart = 2,
            StripCount = 3,
            Reserved = 4
        };

#pragma warning disable CA1508
        if (info.TileIndex == 0 && info.StripStart == 0 && info.StripCount == 0 && info.Reserved == 0)
        {
            throw new InvalidOperationException("TileInfo fields not being set");
        }
#pragma warning restore CA1508
    }
}