using System;
using System.Buffers;
using Etch.Scene;
using Etch.Tiling;
using Etch.Tiling.Strips;

namespace Etch.Raster.Cpu;

public static class StripRenderer
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeAnalysis", "CA1062:Validate arguments before using them", Justification = "Project uses Panic system for validation")]
    public static void Render<TTile>(
        SceneBuffer scene,
        StripBuffer strips,
        TileGrid<TTile> grid,
        Framebuffer target)
        where TTile : struct, ITileSize
    {
        Render(scene, strips, grid, target, ClipMask.Empty);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeAnalysis", "CA1062:Validate arguments before using them", Justification = "Project uses Panic system for validation")]
    public static void Render<TTile>(
        SceneBuffer scene,
        StripBuffer strips,
        TileGrid<TTile> grid,
        Framebuffer target,
        ClipMask clipMask)
        where TTile : struct, ITileSize
    {
#pragma warning disable CA1062
        if (scene is null)
            Panic.ArgumentNull(nameof(scene));
        if (strips is null)
            Panic.ArgumentNull(nameof(strips));
#pragma warning restore CA1062

        if (strips.TileCount == 0)
            return;

        int tileHeight = TTile.Height;

        for (int tileIndex = 0; tileIndex < strips.TileCount; tileIndex++)
        {
            var (tileX, tileY) = grid.TileXY(tileIndex);
            int tilePixelX = tileX * TTile.Width;
            int tilePixelY = tileY * tileHeight;

            var tileStrips = strips.StripsForTile(tileIndex);
            for (int i = 0; i < tileStrips.Length; i++)
            {
                ref readonly var strip = ref tileStrips[i];

                var paint = scene.GetPaint((int)strip.PaintId);
                Rgba16f paintColor;
                if (paint.Kind == PaintKind.Solid)
                {
                    uint argb = paint.Color;
                    byte a = (byte)((argb >> 24) & 0xFF);
                    byte r = (byte)((argb >> 16) & 0xFF);
                    byte g = (byte)((argb >> 8) & 0xFF);
                    byte b = (byte)(argb & 0xFF);
                    float rLin = Srgb.DecodeChannelScalar(r);
                    float gLin = Srgb.DecodeChannelScalar(g);
                    float bLin = Srgb.DecodeChannelScalar(b);
                    float aLin = a * (1.0f / 255.0f);
                    paintColor = Rgba16f.From(rLin, gLin, bLin, aLin);
                }
                else if (paint.Kind == PaintKind.LinearGradient || paint.Kind == PaintKind.RadialGradient)
                {
                    var stops = scene.GetGradientStops((int)paint.GradientId);
                    if (stops.Count > 0)
                    {
                        var (_, argb) = stops.GetStop(0);
                        byte a = (byte)((argb >> 24) & 0xFF);
                        byte r = (byte)((argb >> 16) & 0xFF);
                        byte g = (byte)((argb >> 8) & 0xFF);
                        byte b = (byte)(argb & 0xFF);
                        paintColor = Rgba16f.From(
                            Srgb.DecodeChannelScalar(r),
                            Srgb.DecodeChannelScalar(g),
                            Srgb.DecodeChannelScalar(b),
                            a * (1.0f / 255.0f));
                    }
                    else
                    {
                        paintColor = Rgba16f.From(0, 0, 0, 1);
                    }
                }
                else if (paint.Kind == PaintKind.MeshGradient)
                {
                    int meshId = (int)paint.GradientId;
                    if (meshId >= 0 && meshId < scene.MeshGradientCount)
                    {
                        var mesh = scene.GetMeshGradient(meshId);
                        if (mesh.VertexCount > 0)
                        {
                            var firstVertexColor = mesh.Vertices[0].Color;
                            paintColor = Rgba16f.From(firstVertexColor.R, firstVertexColor.G, firstVertexColor.B, firstVertexColor.A);
                        }
                        else
                        {
                            paintColor = Rgba16f.From(0, 0, 0, 1);
                        }
                    }
                    else
                    {
                        paintColor = Rgba16f.From(0, 0, 0, 1);
                    }
                }
                else if (paint.Kind == PaintKind.Noise)
                {
                    int noiseId = (int)paint.GradientId;
                    if (noiseId >= 0 && noiseId < scene.NoiseSpecCount)
                    {
                        var noiseSpec = scene.GetNoiseSpec(noiseId);
                        paintColor = Rgba16f.From(0.5f, 0.5f, 0.5f, noiseSpec.Opacity);
                    }
                    else
                    {
                        paintColor = Rgba16f.From(0, 0, 0, 1);
                    }
                }
                else
                {
                    paintColor = Rgba16f.From(0, 0, 0, 1);
                }

                var coverage = strips.CoverageForStrip(in strip);
                if (coverage.Length == 0)
                    continue;

                RenderStripCoverage(strip, paint, coverage, tilePixelX, tilePixelY, tileHeight, target, clipMask, paintColor);
            }
        }
    }

    private static void RenderStripCoverage(
        in Strip strip,
        Paint paint,
        ReadOnlySpan<byte> coverage,
        int tilePixelX,
        int tilePixelY,
        int tileHeight,
        Framebuffer target,
        ClipMask clipMask,
        Rgba16f paintColor)
    {
        int rowLength = (int)(strip.X1 - strip.X0 + 1);
        int currentOffset = 0;
        uint rowMask = strip.RowMask;
        Span<byte> tempCoverage = stackalloc byte[64];

        for (int row = 0; row < tileHeight; row++)
        {
            if ((rowMask & (1 << row)) == 0)
                continue;

            int pixelY = tilePixelY + row;
            if ((uint)pixelY >= (uint)target.Height)
                continue;

            var rowSpan = target.RowSpan(pixelY);
            int startX = tilePixelX + (int)strip.X0;
            int count = Math.Min(rowLength, rowSpan.Length - startX);

            if (count <= 0)
                continue;

            var coverageSlice = coverage.Slice(currentOffset, count);
            var rowSlice = rowSpan.Slice(startX, count);

            if (clipMask.Coverage.Width > 0)
            {
                var clipRow = clipMask.Coverage.RowSpan(pixelY);
                if (count <= 64)
                {
                    Span<byte> maskedCoverage = tempCoverage;
                    coverageSlice.CopyTo(maskedCoverage);
                    ClipBlender.ApplyClipCoverageRgba16f(clipRow, maskedCoverage.Slice(0, count), startX, count);
                    BlendModeDispatch.Blend((Etch.ClipBlendGradient.BlendMode)paint.BlendModeId, maskedCoverage.Slice(0, count), paintColor, rowSlice);
                }
                else
                {
                    byte[] maskedCoverageArray = ArrayPool<byte>.Shared.Rent(count);
                    coverageSlice.CopyTo(maskedCoverageArray);
                    ClipBlender.ApplyClipCoverageRgba16f(clipRow, maskedCoverageArray.AsSpan(0, count), startX, count);
                    BlendModeDispatch.Blend((Etch.ClipBlendGradient.BlendMode)paint.BlendModeId, maskedCoverageArray.AsSpan(0, count), paintColor, rowSlice);
                    ArrayPool<byte>.Shared.Return(maskedCoverageArray);
                }
            }
            else
            {
                BlendModeDispatch.Blend((Etch.ClipBlendGradient.BlendMode)paint.BlendModeId, coverageSlice, paintColor, rowSlice);
            }

            currentOffset += rowLength;
        }
    }
}
