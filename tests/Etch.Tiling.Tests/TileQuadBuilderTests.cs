using System;
using System.Runtime.InteropServices;
using Etch.Geometry;
using Etch.Scene;
using Etch.Tiling;
using Etch.Tiling.Classify;
using Etch.Tiling.Strips;
using TUnit;

namespace Etch.Tiling.Tests;

internal sealed class TileQuadBuilderTests
{
    [Test]
    public void TileQuad_SizeIs32()
    {
        if (Marshal.SizeOf<TileQuad>() != 32)
            throw new InvalidOperationException($"sizeof(TileQuad) = {Marshal.SizeOf<TileQuad>()}, expected 32");
    }

    [Test]
    public void EmptyStripBuffer_ZeroQuads()
    {
        var grid = new TileGrid<TTile16>(1920, 1080);
        var emptyStrips = new StripBuffer([], [0], [], 0);

        var quads = TileQuadBuilder.Build(emptyStrips, grid, tileIndex => 0u);

        if (quads.Count != 0)
            throw new InvalidOperationException($"Expected 0 quads, got {quads.Count}");
    }

    [Test]
    public void FullCoverageSolidTile_SetsSolidFlag()
    {
        var grid = new TileGrid<TTile16>(16, 16);
        var strips = new Strip[]
        {
            new Strip(tileIndex: 0, rowMask: 0xFFFF, x0: 0, x1: 15, coverageOffset: 0, paintId: 0)
        };
        var coverage = new byte[256];
        for (int i = 0; i < coverage.Length; i++)
            coverage[i] = 0xFF;
        var offsets = new[] { 0, 1 };
        var stripBuffer = new StripBuffer(strips, offsets, coverage, 1);

        var quads = TileQuadBuilder.Build(stripBuffer, grid, tileIndex => 0u);

        if (quads.Count != 1)
            throw new InvalidOperationException($"Expected 1 quad, got {quads.Count}");

        var quad = quads.Quads[0];
        if ((quad.Flags & TileQuadBuilder.FlagSolidColor) == 0)
            throw new InvalidOperationException("Expected solid color flag to be set");
    }

    [Test]
    public void QuadsEmittedInTileOrder()
    {
        var grid = new TileGrid<TTile16>(32, 32);
        var strips = new Strip[]
        {
            new Strip(tileIndex: 2, rowMask: 0xFFFF, x0: 0, x1: 15, coverageOffset: 0, paintId: 0),
            new Strip(tileIndex: 0, rowMask: 0xFFFF, x0: 0, x1: 15, coverageOffset: 0, paintId: 0),
            new Strip(tileIndex: 1, rowMask: 0xFFFF, x0: 0, x1: 15, coverageOffset: 0, paintId: 0),
        };
        var coverage = new byte[256];
        for (int i = 0; i < coverage.Length; i++)
            coverage[i] = 0xFF;
        var offsets = new[] { 0, 1, 2, 3, 3 };
        var stripBuffer = new StripBuffer(strips, offsets, coverage, 4);

        var quads = TileQuadBuilder.Build(stripBuffer, grid, tileIndex => 0u);

        if (quads.Count != 3)
            throw new InvalidOperationException($"Expected 3 quads, got {quads.Count}");

        if (quads.Quads[0].TileX != 0 || quads.Quads[0].TileY != 0)
            throw new InvalidOperationException("First quad should be at pixel (0,0)");
        if (quads.Quads[1].TileX != 16 || quads.Quads[1].TileY != 0)
            throw new InvalidOperationException("Second quad should be at pixel (16,0)");
        if (quads.Quads[2].TileX != 0 || quads.Quads[2].TileY != 16)
            throw new InvalidOperationException("Third quad should be at pixel (0,16)");
    }

    [Test]
    public void TileInsideFrame_SetsFlag()
    {
        var grid = new TileGrid<TTile16>(32, 32);
        var strips = new Strip[]
        {
            new Strip(tileIndex: 0, rowMask: 0xFFFF, x0: 0, x1: 15, coverageOffset: 0, paintId: 0)
        };
        var coverage = new byte[256];
        for (int i = 0; i < coverage.Length; i++)
            coverage[i] = 0xFF;
        var offsets = new[] { 0, 1, 1, 1, 1 };
        var stripBuffer = new StripBuffer(strips, offsets, coverage, 4);

        var quads = TileQuadBuilder.Build(stripBuffer, grid, tileIndex => 0u);

        if (quads.Count != 1)
            throw new InvalidOperationException($"Expected 1 quad, got {quads.Count}");

        var quad = quads.Quads[0];
        if ((quad.Flags & TileQuadBuilder.FlagTileInsideFrame) == 0)
            throw new InvalidOperationException("Expected tile inside frame flag to be set for fully inside tile");
    }

    [Test]
    public void NonSolidCoverage_NoSolidFlag()
    {
        var grid = new TileGrid<TTile16>(16, 16);
        var strips = new Strip[]
        {
            new Strip(tileIndex: 0, rowMask: 0xFFFF, x0: 0, x1: 15, coverageOffset: 0, paintId: 0)
        };
        var coverage = new byte[256];
        for (int i = 0; i < coverage.Length; i++)
            coverage[i] = 0x80;
        var offsets = new[] { 0, 1 };
        var stripBuffer = new StripBuffer(strips, offsets, coverage, 1);

        var quads = TileQuadBuilder.Build(stripBuffer, grid, tileIndex => 0u);

        if (quads.Count != 1)
            throw new InvalidOperationException($"Expected 1 quad, got {quads.Count}");

        var quad = quads.Quads[0];
        if ((quad.Flags & TileQuadBuilder.FlagSolidColor) != 0)
            throw new InvalidOperationException("Expected solid color flag NOT to be set for non-full coverage");
    }
}
