using System;
using System.IO;
using SharpImage.Core;
using SharpImage.Image;
using SharpImage.Formats;

namespace Etch.Testing;

public static class ImageWriter
{
    public static void WriteRgbaToPng(string path, ReadOnlySpan<byte> rgba, int w, int h)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

        using var frame = new ImageFrame();
        frame.Initialize(w, h, ColorspaceType.SRGB, true);

        for (int y = 0; y < h; y++)
        {
            var row = frame.GetPixelRowForWrite(y);
            for (int x = 0; x < w; x++)
            {
                int srcIdx = (y * w + x) * 4;
                int dstIdx = x * 4;
                row[dstIdx + 0] = (ushort)(rgba[srcIdx + 2] * 257);
                row[dstIdx + 1] = (ushort)(rgba[srcIdx + 1] * 257);
                row[dstIdx + 2] = (ushort)(rgba[srcIdx + 0] * 257);
                row[dstIdx + 3] = (ushort)(rgba[srcIdx + 3] * 257);
            }
        }

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        PngCoder.Write(frame, stream);
    }
}
