using System;
using System.Runtime.CompilerServices;

namespace Etch.Text.Shape;

/// <summary>
/// Immutable key for the shaping cache.
/// </summary>
public readonly struct ShapingKey : IEquatable<ShapingKey>
{
    public ulong TextHash { get; }
    public int FaceId { get; }
    public ushort SizeUnits { get; }
    public byte Level { get; }
    public ushort ScriptTag { get; }

    public ShapingKey(ulong textHash, int faceId, ushort sizeUnits, byte level, ushort scriptTag)
    {
        TextHash = textHash;
        FaceId = faceId;
        SizeUnits = sizeUnits;
        Level = level;
        ScriptTag = scriptTag;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(ShapingKey other)
        => TextHash == other.TextHash
        && FaceId == other.FaceId
        && SizeUnits == other.SizeUnits
        && Level == other.Level
        && ScriptTag == other.ScriptTag;

    public override bool Equals(object? obj) => obj is ShapingKey other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(TextHash.GetHashCode(), FaceId, SizeUnits, Level, ScriptTag);

    public override string ToString()
        => $"Key(hash={TextHash:X16}, face={FaceId}, size={SizeUnits}, level={Level}, script={ScriptTag})";

    public static bool operator ==(ShapingKey left, ShapingKey right) => left.Equals(right);
    public static bool operator !=(ShapingKey left, ShapingKey right) => !left.Equals(right);
}
