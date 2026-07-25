using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Etch.Scene;
using Etch.Tiling;
using Etch.Tiling.Classify;
using Etch.Tiling.Scheduler;
using Etch.Tiling.Strips;

namespace Etch.Raster.Cpu;

public static class ParallelStripRenderer
{
    [SuppressMessage("CodeAnalysis", "CA1062:Validate arguments before using them", Justification = "Project uses Panic system for validation")]
    public static unsafe void Render<TTile>(
        SceneBuffer scene,
        StripBuffer strips,
        TileGrid<TTile> grid,
        Framebuffer target,
        ITileScheduler scheduler)
        where TTile : struct, ITileSize
    {
        Render(scene, strips, grid, target, scheduler, ClipMask.Empty);
    }

    [SuppressMessage("CodeAnalysis", "CA1062:Validate arguments before using them", Justification = "Project uses Panic system for validation")]
    public static unsafe void Render<TTile>(
        SceneBuffer scene,
        StripBuffer strips,
        TileGrid<TTile> grid,
        Framebuffer target,
        ITileScheduler scheduler,
        ClipMask clipMask)
        where TTile : struct, ITileSize
    {
#pragma warning disable CA1062
        if (scene is null)
            Panic.ArgumentNull(nameof(scene));
        if (strips is null)
            Panic.ArgumentNull(nameof(strips));
        if (scheduler is null)
            Panic.ArgumentNull(nameof(scheduler));
#pragma warning restore CA1062

        if (strips.TileCount == 0)
            return;

        var renderContext = new RenderContext<TTile>
        {
            Scene = scene,
            Strips = strips,
            Grid = grid,
            Target = target,
            ClipMask = clipMask,
            CompletedCount = 0
        };

        scheduler.ParallelFor(strips.TileCount, ref renderContext, &RenderTile<TTile>);
    }

    private static unsafe void RenderTile<TTile>(int tileIndex, ref RenderContext<TTile> context, ref ClassificationAccumulator accumulator)
        where TTile : struct, ITileSize
    {
        var scene = context.Scene;
        var strips = context.Strips;
        var grid = context.Grid;
        var target = context.Target;
        var clipMask = context.ClipMask;

        var (tileX, tileY) = grid.TileXY(tileIndex);
        int tilePixelX = tileX * TTile.Width;
        int tilePixelY = tileY * TTile.Height;

        var tileStrips = strips.StripsForTile(tileIndex);
        for (int i = 0; i < tileStrips.Length; i++)
        {
            ref readonly var strip = ref tileStrips[i];

            var paint = scene.GetPaint((int)strip.PaintId);
            if (paint.Kind != PaintKind.Solid)
                continue;

            var coverage = strips.CoverageForStrip(in strip);
            if (coverage.Length == 0)
                continue;

            RenderStripCoverage<TTile>(strip, paint, coverage, tilePixelX, tilePixelY, target, clipMask);
        }

        Interlocked.Increment(ref context.CompletedCount);
    }

    private static void RenderStripCoverage<TTile>(
        in Strip strip,
        Paint paint,
        ReadOnlySpan<byte> coverage,
        int tilePixelX,
        int tilePixelY,
        Framebuffer target,
        ClipMask clipMask)
        where TTile : struct, ITileSize
    {
        int rowLength = (int)(strip.X1 - strip.X0 + 1);
        int currentOffset = 0;
        uint rowMask = strip.RowMask;
        Span<byte> tempCoverage = stackalloc byte[64];

        for (int row = 0; row < TTile.Height; row++)
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

            uint argb = paint.Color;
            byte a = (byte)((argb >> 24) & 0xFF);
            byte r = (byte)((argb >> 16) & 0xFF);
            byte g = (byte)((argb >> 8) & 0xFF);
            byte b = (byte)(argb & 0xFF);

            float rLin = Srgb.DecodeChannelScalar(r);
            float gLin = Srgb.DecodeChannelScalar(g);
            float bLin = Srgb.DecodeChannelScalar(b);
            float aLin = a * (1.0f / 255.0f);

            Rgba16f paintColor = Rgba16f.From(rLin, gLin, bLin, aLin);

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

    private struct RenderContext<TTile>
        where TTile : struct, ITileSize
    {
        public SceneBuffer Scene;
        public StripBuffer Strips;
        public TileGrid<TTile> Grid;
        public Framebuffer Target;
        public ClipMask ClipMask;
        public int CompletedCount;
    }
}
