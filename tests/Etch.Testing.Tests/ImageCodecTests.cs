using System.Buffers.Binary;
using System.IO.Compression;
using Etch.Testing;
using TUnit;

namespace Etch.Testing.Tests;

internal sealed class ImageCodecTests
{
    [Test]
    public async Task WriteThenRead_RoundTripsDistinctChannelValues()
    {
        const int Width = 3;
        const int Height = 2;
        var written = new byte[Width * Height * 4];
        for (int i = 0; i < written.Length; i += 4)
        {
            written[i] = (byte)(10 + i);
            written[i + 1] = (byte)(60 + i);
            written[i + 2] = (byte)(120 + i);
            written[i + 3] = (byte)(200 + i);
        }

        string path = CreateTempPngPath();
        try
        {
            ImageWriter.WriteRgbaToPng(path, written, Width, Height);
            byte[] read = ImageReader.ReadPngToRgba8(path);

            await Assert.That(read.SequenceEqual(written)).IsTrue();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task WrittenRedPixel_DecodesAsRedInRawPngData()
    {
        byte[] opaqueRed = [255, 0, 0, 255];

        string path = CreateTempPngPath();
        try
        {
            ImageWriter.WriteRgbaToPng(path, opaqueRed, 1, 1);
            var (red, green, blue) = DecodeFirstPixelFromPngChunks(await File.ReadAllBytesAsync(path).ConfigureAwait(false));

            await Assert.That(red).IsEqualTo(255);
            await Assert.That(green).IsEqualTo(0);
            await Assert.That(blue).IsEqualTo(0);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task ReadPngToRgba8_RedPngEncodedByHand_ReadsAsRed()
    {
        string path = CreateTempPngPath();
        try
        {
            await File.WriteAllBytesAsync(path, EncodeSinglePixelRgb8Png(255, 0, 0)).ConfigureAwait(false);
            byte[] read = ImageReader.ReadPngToRgba8(path);

            await Assert.That(read[0]).IsEqualTo((byte)255);
            await Assert.That(read[1]).IsEqualTo((byte)0);
            await Assert.That(read[2]).IsEqualTo((byte)0);
            await Assert.That(read[3]).IsEqualTo((byte)255);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateTempPngPath()
        => Path.Combine(Path.GetTempPath(), $"etch_image_codec_{Guid.NewGuid():N}.png");

    // Independent of SharpImage: walks the PNG chunks, inflates IDAT and reads the first pixel of the
    // first scanline. For that pixel every PNG filter type reconstructs to the raw bytes, because its
    // left, up and up-left neighbours are all defined as zero.
    private static (int Red, int Green, int Blue) DecodeFirstPixelFromPngChunks(byte[] png)
    {
        const int SignatureLength = 8;
        int bitDepth = 0;
        using var compressedImageData = new MemoryStream();

        int offset = SignatureLength;
        while (offset < png.Length)
        {
            int chunkLength = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4));
            string chunkType = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            var chunkData = png.AsSpan(offset + 8, chunkLength);

            if (chunkType == "IHDR")
            {
                bitDepth = chunkData[8];
                byte colorType = chunkData[9];
                if (colorType != 2 && colorType != 6)
                    throw new InvalidOperationException($"Expected an RGB or RGBA PNG, got color type {colorType}");
            }
            else if (chunkType == "IDAT")
            {
                compressedImageData.Write(chunkData);
            }
            else if (chunkType == "IEND")
            {
                break;
            }

            offset += 12 + chunkLength;
        }

        compressedImageData.Position = 0;
        using var inflater = new ZLibStream(compressedImageData, CompressionMode.Decompress);
        using var scanlines = new MemoryStream();
        inflater.CopyTo(scanlines);
        byte[] decoded = scanlines.ToArray();

        // decoded[0] is the first scanline's filter-type byte.
        int bytesPerSample = bitDepth / 8;
        return (decoded[1], decoded[1 + bytesPerSample], decoded[1 + 2 * bytesPerSample]);
    }

    private static byte[] EncodeSinglePixelRgb8Png(byte red, byte green, byte blue)
    {
        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), 1);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), 1);
        header[8] = 8;
        header[9] = 2;
        WriteChunk(png, "IHDR", header);

        using var compressed = new MemoryStream();
        using (var deflater = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflater.Write([0, red, green, blue]);
        }
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream png, string chunkType, byte[] data)
    {
        byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(chunkType);
        Span<byte> lengthBytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(lengthBytes, data.Length);
        png.Write(lengthBytes);
        png.Write(typeBytes);
        png.Write(data);

        var crc = new System.IO.Hashing.Crc32();
        crc.Append(typeBytes);
        crc.Append(data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc.GetCurrentHashAsUInt32());
        png.Write(crcBytes);
    }
}
