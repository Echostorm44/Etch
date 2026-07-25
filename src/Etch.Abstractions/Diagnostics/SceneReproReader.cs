using System;
using System.Buffers.Binary;
using Etch;

namespace Etch.Abstractions.Diagnostics;

public enum ReproReadResult
{
    Success,
    Truncated,
    InvalidMagic,
    UnsupportedVersion,
}

public ref struct SceneReproReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _position;

    public uint Version { get; private set; }
    public int SectionCount { get; private set; }
    public ReproReadResult Result { get; private set; }

    public SceneReproReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _position = 0;
        Version = 0;
        SectionCount = 0;
        Result = ReproReadResult.Truncated;
    }

    public bool TryReadHeader()
    {
        if (_data.Length < SceneReproFormat.HeaderSize)
        {
            Result = ReproReadResult.Truncated;
            return false;
        }

        if (_data[0] != 'E' || _data[1] != 'T' || _data[2] != 'R' || _data[3] != 'P')
        {
            Result = ReproReadResult.InvalidMagic;
            return false;
        }

        Version = BinaryPrimitives.ReadUInt32LittleEndian(_data.Slice(4, 4));
        if (Version > SceneReproFormat.CurrentVersion)
        {
            Result = ReproReadResult.UnsupportedVersion;
            return false;
        }

        SectionCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(_data.Slice(8, 4));
        _position = SceneReproFormat.HeaderSize;
        Result = ReproReadResult.Success;
        return true;
    }

    public bool TryReadNextSection(out ReproSection sectionId, out ReadOnlySpan<byte> payload)
    {
        sectionId = ReproSection.Invalid;
        payload = default;

        if (Result != ReproReadResult.Success)
            return false;

        if (_position + SceneReproFormat.SectionHeaderSize > _data.Length)
        {
            Result = ReproReadResult.Truncated;
            return false;
        }

        uint id = BinaryPrimitives.ReadUInt32LittleEndian(_data.Slice(_position, 4));
        _position += 4;

        uint payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(_data.Slice(_position, 4));
        _position += 4;

        if (_position + (int)payloadLength > _data.Length)
        {
            Result = ReproReadResult.Truncated;
            return false;
        }

        sectionId = (ReproSection)id;
        payload = _data.Slice(_position, (int)payloadLength);
        _position += (int)payloadLength;
        return true;
    }

    public static SceneReproReader CreateAndReadHeader(ReadOnlySpan<byte> data)
    {
        var reader = new SceneReproReader(data);
        reader.TryReadHeader();
        return reader;
    }
}
