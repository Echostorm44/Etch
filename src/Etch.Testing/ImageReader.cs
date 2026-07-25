using System;
using System.IO;
using SharpImage.Formats;
using SharpImage.Image;

namespace Etch.Testing;

public static class ImageReader
{
    public static byte[] ReadPngToRgba8(string path)
    {
        using var frame = PngCoder.Read(path);
        int w = (int)frame.Columns;
        int h = (int)frame.Rows;
        int channels = (int)frame.NumberOfChannels;

        var result = new byte[w * h * 4];

        for (int y = 0; y < h; y++)
        {
            var row = frame.GetPixelRow(y);
            int dstRowOffset = y * w * 4;

            for (int x = 0; x < w; x++)
            {
                int srcIdx = x * channels;
                int dstIdx = dstRowOffset + x * 4;

                result[dstIdx + 0] = (byte)((row[srcIdx + 2] / 65535.0f) * 255.0f + 0.5f);
                result[dstIdx + 1] = (byte)((row[srcIdx + 1] / 65535.0f) * 255.0f + 0.5f);
                result[dstIdx + 2] = (byte)((row[srcIdx + 0] / 65535.0f) * 255.0f + 0.5f);
                result[dstIdx + 3] = channels >= 4
                    ? (byte)((row[srcIdx + 3] / 65535.0f) * 255.0f + 0.5f)
                    : (byte)255;
            }
        }

        return result;
    }
}
