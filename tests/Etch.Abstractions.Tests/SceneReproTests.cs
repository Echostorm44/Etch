using System;
using System.Buffers.Binary;
using Etch;
using Etch.Abstractions.Diagnostics;
using TUnit;

namespace Etch.Tests;

internal sealed class SceneReproTests
{
    [Test]
    public async Task SceneReproFormatMagicBytesIsCorrect()
    {
        ReadOnlySpan<byte> magic = SceneReproFormat.MagicBytes;
        byte b0 = magic[0];
        byte b1 = magic[1];
        byte b2 = magic[2];
        byte b3 = magic[3];
        int len = magic.Length;
        await Assert.That(len).IsEqualTo(4);
        await Assert.That(b0 == (byte)'E').IsTrue();
        await Assert.That(b1 == (byte)'T').IsTrue();
        await Assert.That(b2 == (byte)'R').IsTrue();
        await Assert.That(b3 == (byte)'P').IsTrue();
    }

    [Test]
    public async Task SceneReproReaderInvalidMagicReturnsInvalidMagic()
    {
        byte[] data = new byte[100];
        data[0] = (byte)'X';
        data[1] = (byte)'Y';
        data[2] = (byte)'Z';
        data[3] = (byte)'W';

        var reader = new SceneReproReader(data);
        reader.TryReadHeader();

        ReproReadResult result = reader.Result;
        await Assert.That(result).IsEqualTo(ReproReadResult.InvalidMagic);
    }

    [Test]
    public async Task SceneReproReaderTruncatedHeaderReturnsTruncated()
    {
        byte[] data = new byte[5];
        data[0] = (byte)'E';
        data[1] = (byte)'T';
        data[2] = (byte)'R';
        data[3] = (byte)'P';

        var reader = new SceneReproReader(data);
        reader.TryReadHeader();

        ReproReadResult result = reader.Result;
        await Assert.That(result).IsEqualTo(ReproReadResult.Truncated);
    }

    [Test]
    public async Task SceneReproReaderValidHeaderParsesVersionAndSectionCount()
    {
        byte[] data = new byte[12];
        data[0] = (byte)'E';
        data[1] = (byte)'T';
        data[2] = (byte)'R';
        data[3] = (byte)'P';
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan().Slice(4, 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan().Slice(8, 4), 2);

        var reader = new SceneReproReader(data);
        reader.TryReadHeader();

        ReproReadResult result = reader.Result;
        uint version = reader.Version;
        int sectionCount = reader.SectionCount;
        await Assert.That(result).IsEqualTo(ReproReadResult.Success);
        await Assert.That(version).IsEqualTo(1u);
        await Assert.That(sectionCount).IsEqualTo(2);
    }

    [Test]
    public async Task SceneReproReaderSupportsCurrentVersion()
    {
        byte[] data = new byte[12];
        data[0] = (byte)'E';
        data[1] = (byte)'T';
        data[2] = (byte)'R';
        data[3] = (byte)'P';
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan().Slice(4, 4), SceneReproFormat.CurrentVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan().Slice(8, 4), 0);

        var reader = new SceneReproReader(data);
        reader.TryReadHeader();

        ReproReadResult result = reader.Result;
        await Assert.That(result).IsEqualTo(ReproReadResult.Success);
    }

    [Test]
    public async Task SceneReproReaderFutureVersionReturnsUnsupportedVersion()
    {
        byte[] data = new byte[12];
        data[0] = (byte)'E';
        data[1] = (byte)'T';
        data[2] = (byte)'R';
        data[3] = (byte)'P';
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan().Slice(4, 4), SceneReproFormat.CurrentVersion + 1);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan().Slice(8, 4), 0);

        var reader = new SceneReproReader(data);
        reader.TryReadHeader();

        ReproReadResult result = reader.Result;
        await Assert.That(result).IsEqualTo(ReproReadResult.UnsupportedVersion);
    }

    [Test]
    public async Task SceneReproWriterTryWriteEnvelopeWritesCorrectHeader()
    {
        byte[] buffer = new byte[1024];
        ReproSection[] sectionIds = new ReproSection[] { ReproSection.Scene, ReproSection.Seams };
        byte[] sceneData = new byte[] { 1, 2, 3, 4 };
        byte[] seamsData = new byte[] { 5, 6 };
        byte[][] payloads = new byte[][] { sceneData, seamsData };

        bool success = SceneReproWriter.TryWriteEnvelope(buffer, 1, sectionIds, payloads, out int written);

        byte b0 = buffer[0];
        byte b1 = buffer[1];
        byte b2 = buffer[2];
        byte b3 = buffer[3];
        uint ver = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan().Slice(4, 4));
        uint secCount = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan().Slice(8, 4));
        await Assert.That(success).IsTrue();
        await Assert.That(b0 == (byte)'E').IsTrue();
        await Assert.That(b1 == (byte)'T').IsTrue();
        await Assert.That(b2 == (byte)'R').IsTrue();
        await Assert.That(b3 == (byte)'P').IsTrue();
        await Assert.That(ver).IsEqualTo(1u);
        await Assert.That(secCount).IsEqualTo(2u);
    }

    [Test]
    public async Task SceneReproWriterRoundTripPreservesData()
    {
        byte[] buffer = new byte[1024];
        ReproSection[] sectionIds = new ReproSection[] { ReproSection.Scene, ReproSection.Seams, ReproSection.BuildInfo };
        byte[] sceneData = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        byte[] seamsData = new byte[] { 0xFE };
        byte[] buildInfoData = new byte[] { 0xAA, 0xBB, 0xCC };
        byte[][] payloads = new byte[][] { sceneData, seamsData, buildInfoData };

        bool writeSuccess = SceneReproWriter.TryWriteEnvelope(buffer, 1, sectionIds, payloads, out int written);
        await Assert.That(writeSuccess).IsTrue();

        RoundTripData roundTrip = ReadAllSections(buffer.AsSpan().Slice(0, written));

        await Assert.That(roundTrip.Result).IsEqualTo(ReproReadResult.Success);
        await Assert.That(roundTrip.Version).IsEqualTo(1u);
        await Assert.That(roundTrip.SectionCount).IsEqualTo(3);

        await Assert.That(roundTrip.Sections[0].Id).IsEqualTo(ReproSection.Scene);
        await Assert.That(roundTrip.Sections[0].PayloadLength).IsEqualTo(4);
        await Assert.That(roundTrip.Sections[0].Payload[0] == 0xDE).IsTrue();

        await Assert.That(roundTrip.Sections[1].Id).IsEqualTo(ReproSection.Seams);
        await Assert.That(roundTrip.Sections[1].PayloadLength).IsEqualTo(1);
        await Assert.That(roundTrip.Sections[1].Payload[0] == 0xFE).IsTrue();

        await Assert.That(roundTrip.Sections[2].Id).IsEqualTo(ReproSection.BuildInfo);
        await Assert.That(roundTrip.Sections[2].PayloadLength).IsEqualTo(3);
        await Assert.That(roundTrip.Sections[2].Payload[0] == 0xAA).IsTrue();
    }

    [Test]
    public async Task SceneReproWriterCalculateEnvelopeSizeIsAccurate()
    {
        int[] sectionSizes = new int[] { 4, 1, 3 };
        int size = SceneReproWriter.CalculateEnvelopeSize(3, sectionSizes);

        int expected = SceneReproFormat.HeaderSize;
        expected += 3 * SceneReproFormat.SectionHeaderSize;
        expected += 4 + 1 + 3;

        await Assert.That(size).IsEqualTo(expected);
    }

    [Test]
    public async Task PanicCodesSceneReproVersionMismatchIsAllocated()
    {
        string val = PanicCodes.SceneReproVersionMismatch.Value;
        await Assert.That(val).IsEqualTo("ET-P-0020");
    }

    private static RoundTripData ReadAllSections(ReadOnlySpan<byte> data)
    {
        var reader = new SceneReproReader(data);
        reader.TryReadHeader();

        var result = reader.Result;
        var version = reader.Version;
        var sectionCount = reader.SectionCount;

        var sections = new SectionData[sectionCount];
        for (int i = 0; i < sectionCount; i++)
        {
            reader.TryReadNextSection(out ReproSection id, out ReadOnlySpan<byte> payload);
            byte[] payloadCopy = payload.ToArray();
            sections[i] = new SectionData(id, payloadCopy);
        }

        return new RoundTripData(result, version, sectionCount, sections);
    }

    private readonly struct SectionData
    {
        public readonly ReproSection Id;
        public readonly byte[] Payload;

        public SectionData(ReproSection id, byte[] payload)
        {
            Id = id;
            Payload = payload;
        }

        public int PayloadLength => Payload.Length;
    }

    private readonly struct RoundTripData
    {
        public readonly ReproReadResult Result;
        public readonly uint Version;
        public readonly int SectionCount;
        public readonly SectionData[] Sections;

        public RoundTripData(ReproReadResult result, uint version, int sectionCount, SectionData[] sections)
        {
            Result = result;
            Version = version;
            SectionCount = sectionCount;
            Sections = sections;
        }
    }
}