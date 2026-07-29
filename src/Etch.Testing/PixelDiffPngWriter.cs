using System;

namespace Etch.Testing;

public static class PixelDiffPngWriter
{
    public static void Write4PanelPng(
        string path,
        ReadOnlySpan<byte> actual,
        ReadOnlySpan<byte> reference,
        int w,
        int h)
    {
        int panelWidth = Math.Max(1, w / 2);
        int panelHeight = Math.Max(1, h / 2);

        var pixels = new byte[w * h * 4];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int dstIdx = (y * w + x) * 4;
                byte r, g, b, a = 255;

                if (y < panelHeight && x < panelWidth)
                {
                    int srcIdx = (y * w + x) * 4;
                    r = actual[srcIdx];
                    g = actual[srcIdx + 1];
                    b = actual[srcIdx + 2];
                }
                else if (y < panelHeight && x >= panelWidth)
                {
                    int refX = x - panelWidth;
                    int srcIdx = (y * w + refX) * 4;
                    r = reference[srcIdx];
                    g = reference[srcIdx + 1];
                    b = reference[srcIdx + 2];
                }
                else if (y >= panelHeight && x < panelWidth)
                {
                    int diffY = y - panelHeight;
                    int diffX = x;
                    int srcIdx = (diffY * w + diffX) * 4;

                    int diffR = Math.Abs(actual[srcIdx] - reference[srcIdx]);
                    int diffG = Math.Abs(actual[srcIdx + 1] - reference[srcIdx + 1]);
                    int diffB = Math.Abs(actual[srcIdx + 2] - reference[srcIdx + 2]);

                    r = (byte)Math.Min(255, diffR * 1);
                    g = (byte)Math.Min(255, diffG * 1);
                    b = (byte)Math.Min(255, diffB * 1);
                }
                else
                {
                    int diffY = y - panelHeight;
                    int diffX = x - panelWidth;
                    int srcIdx = (diffY * w + diffX) * 4;

                    float errR = Math.Abs(actual[srcIdx] - reference[srcIdx]) / 255.0f;
                    float errG = Math.Abs(actual[srcIdx + 1] - reference[srcIdx + 1]) / 255.0f;
                    float errB = Math.Abs(actual[srcIdx + 2] - reference[srcIdx + 2]) / 255.0f;
                    float maxErr = Math.Max(errR, Math.Max(errG, errB));

                    r = (byte)Math.Min(255, maxErr * 8.0f * 255);
                    g = (byte)Math.Max(0, (1.0f - maxErr) * 128);
                    b = 0;
                }

                pixels[dstIdx] = r;
                pixels[dstIdx + 1] = g;
                pixels[dstIdx + 2] = b;
                pixels[dstIdx + 3] = a;
            }
        }

        // Encode via SharpImage (ImageWriter) — never a hand-rolled PNG codec.
        ImageWriter.WriteRgbaToPng(path, pixels, w, h);
    }
}
