using System;
using System.Buffers.Binary;

namespace Etch.Abstractions.Diagnostics;

public enum ReproSection
{
    Invalid = 0,
    Seams = 1,
    Scene = 2,
    GpuFeatures = 3,
    BuildInfo = 4,
    GpuValidationLog = 5,
    GpuAdapterInfo = 6,
    GpuSurfaceConfig = 7,
    Custom = 100,
}

public static class SceneReproFormat
{
    public const uint CurrentVersion = 1;
    public const string Magic = "ETRP";
    public const int HeaderSize = 12;
    public const int SectionHeaderSize = 8;

    public static ReadOnlySpan<byte> MagicBytes => new byte[] { (byte)'E', (byte)'T', (byte)'R', (byte)'P' };

    public static uint ReadMagic(ReadOnlySpan<byte> header)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(header);
    }

    public static uint ReadVersion(ReadOnlySpan<byte> header)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(4, 4));
    }

    public static uint ReadSectionCount(ReadOnlySpan<byte> header)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(8, 4));
    }
}
