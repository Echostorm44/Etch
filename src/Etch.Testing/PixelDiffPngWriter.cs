using System;
using System.IO;
using System.IO.Compression;

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

        WritePng(path, pixels, w, h);
    }

    private static void WritePng(string path, byte[] rgba, int w, int h)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        WritePngStream(stream, rgba, w, h);
    }

    private static void WritePngStream(FileStream stream, byte[] rgba, int w, int h)
    {
        stream.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);

        WriteChunk(stream, "IHDR", MakeIHDR(w, h));

        var idatData = EncodeIdat(rgba, w, h);
        WriteChunk(stream, "IDAT", idatData);

        WriteChunk(stream, "IEND", ReadOnlySpan<byte>.Empty);
    }

    private static byte[] EncodeIdat(byte[] rgba, int w, int h)
    {
        using var compressed = new MemoryStream();

        compressed.WriteByte(0x78);
        compressed.WriteByte(0x01);

        using (var deflate = new DeflateStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (int y = 0; y < h; y++)
            {
                deflate.WriteByte(0);
                for (int x = 0; x < w; x++)
                {
                    int idx = (y * w + x) * 4;
                    deflate.WriteByte(rgba[idx]);
                    deflate.WriteByte(rgba[idx + 1]);
                    deflate.WriteByte(rgba[idx + 2]);
                }
            }
        }

        byte[] result = compressed.ToArray();

        return result;
    }

    private static byte[] MakeIHDR(int w, int h)
    {
        return new byte[]
        {
            (byte)((w >> 24) & 0xFF), (byte)((w >> 16) & 0xFF), (byte)((w >> 8) & 0xFF), (byte)(w & 0xFF),
            (byte)((h >> 24) & 0xFF), (byte)((h >> 16) & 0xFF), (byte)((h >> 8) & 0xFF), (byte)(h & 0xFF),
            8, 2, 0, 0, 0
        };
    }

    private static void WriteChunk(FileStream stream, string type, ReadOnlySpan<byte> data)
    {
        int length = data.Length;
        stream.WriteByte((byte)((length >> 24) & 0xFF));
        stream.WriteByte((byte)((length >> 16) & 0xFF));
        stream.WriteByte((byte)((length >> 8) & 0xFF));
        stream.WriteByte((byte)(length & 0xFF));

        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes, 0, 4);

        if (data.Length > 0)
            stream.Write(data.ToArray(), 0, data.Length);

        uint crc = Crc32(typeBytes, data);
        stream.WriteByte((byte)((crc >> 24) & 0xFF));
        stream.WriteByte((byte)((crc >> 16) & 0xFF));
        stream.WriteByte((byte)((crc >> 8) & 0xFF));
        stream.WriteByte((byte)(crc & 0xFF));
    }

    private static uint Crc32(byte[] type, ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in type)
            crc = s_crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        foreach (byte b in data)
            crc = s_crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFF;
    }

    private static readonly uint[] s_crcTable = GenerateCrcTable();

    private static uint[] GenerateCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = ((c & 1) != 0) ? (0xEDB88320 ^ (c >> 1)) : (c >> 1);
            table[n] = c;
        }
        return table;
    }
}