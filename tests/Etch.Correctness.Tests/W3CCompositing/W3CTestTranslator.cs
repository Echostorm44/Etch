using System;
using Etch.ClipBlendGradient;
using Etch.Geometry;
using Etch.Raster.Cpu;
using Etch.Scene;

namespace Etch.Correctness.Tests.W3CCompositing;

/// <summary>
/// Translates simple W3C-style compositing test descriptions into
/// <see cref="SceneBuffer"/> commands. Supports overlapping filled
/// rectangles with per-paint blend modes — the narrow subset the
/// W3C CSS Compositing corpus uses most frequently.
/// </summary>
public static class W3CTestTranslator
{
    /// <summary>
    /// Builds a scene with two overlapping opaque rectangles.
    /// The backdrop covers the full surface; the source is centred
    /// and uses the specified blend mode.
    /// </summary>
    public static SceneBuffer BuildTwoRectScene(
        int width, int height,
        uint backdropArgb,
        uint sourceArgb,
        Etch.ClipBlendGradient.BlendMode blendMode)
    {
        var builder = SceneBuilder.Begin();
        builder.BeginFrame();

        int identityTransform = builder.AddTransform(Affine.Identity);

        // Backdrop paint — always Normal (blend mode 0)
        var backdropPaint = Paint.Solid(backdropArgb, blendModeId: 0);
        int backdropPaintId = builder.AddPaint(backdropPaint);

        // Source paint — uses the test blend mode
        var sourcePaint = Paint.Solid(sourceArgb, blendModeId: (byte)blendMode);
        int sourcePaintId = builder.AddPaint(sourcePaint);

        // Backdrop rect: full canvas
        var backdropRect = new Rect(0, 0, width, height);
        builder.FillRect(backdropRect, backdropPaintId, identityTransform);

        // Source rect: centred, 50% of canvas
        int srcW = width / 2;
        int srcH = height / 2;
        int srcX = (width - srcW) / 2;
        int srcY = (height - srcH) / 2;
        var sourceRect = new Rect(srcX, srcY, srcX + srcW, srcY + srcH);
        builder.FillRect(sourceRect, sourcePaintId, identityTransform);

        builder.EndFrame();
        return builder.End();
    }

    /// <summary>
    /// Computes the expected RGBA8 output for a two-rect scene by evaluating
    /// <see cref="BlendReference"/> pixel-by-pixel. This is the oracle against
    /// which the CPU/GPU renderers are diffed.
    /// </summary>
    public static byte[] ComputeReferenceRgba8(
        int width, int height,
        uint backdropArgb,
        uint sourceArgb,
        Etch.ClipBlendGradient.BlendMode blendMode)
    {
        // Unpack ARGB → linear double
        double backR = Srgb.DecodeChannelScalar((byte)((backdropArgb >> 16) & 0xFF));
        double backG = Srgb.DecodeChannelScalar((byte)((backdropArgb >> 8) & 0xFF));
        double backB = Srgb.DecodeChannelScalar((byte)(backdropArgb & 0xFF));
        double backA = ((backdropArgb >> 24) & 0xFF) / 255.0;

        double srcR = Srgb.DecodeChannelScalar((byte)((sourceArgb >> 16) & 0xFF));
        double srcG = Srgb.DecodeChannelScalar((byte)((sourceArgb >> 8) & 0xFF));
        double srcB = Srgb.DecodeChannelScalar((byte)(sourceArgb & 0xFF));
        double srcA = ((sourceArgb >> 24) & 0xFF) / 255.0;

        var backdrop = new LinearColor(backR, backG, backB, backA);
        var source = new LinearColor(srcR, srcG, srcB, srcA);

        int srcW = width / 2;
        int srcH = height / 2;
        int srcX = (width - srcW) / 2;
        int srcY = (height - srcH) / 2;
        int srcX2 = srcX + srcW;
        int srcY2 = srcY + srcH;

        byte[] output = new byte[width * height * 4];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                LinearColor result;
                if (x >= srcX && x < srcX2 && y >= srcY && y < srcY2)
                {
                    // Overlapping region: apply blend mode
                    result = BlendReference.Apply(source, backdrop, blendMode);
                }
                else
                {
                    // Non-overlapping: just backdrop
                    result = backdrop;
                }

                // RGBA byte order (R at index 0), matching SceneRunner.RunCpu output so the
                // reference and actual buffers are compared channel-for-channel.
                int idx = (y * width + x) * 4;
                output[idx + 0] = Srgb.EncodeChannelScalar((float)(result.R * result.A));
                output[idx + 1] = Srgb.EncodeChannelScalar((float)(result.G * result.A));
                output[idx + 2] = Srgb.EncodeChannelScalar((float)(result.B * result.A));
                output[idx + 3] = (byte)Math.Clamp(result.A * 255.0 + 0.5, 0, 255);
            }
        }

        return output;
    }
}
