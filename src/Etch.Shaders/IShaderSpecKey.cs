using System;

namespace Etch.Shaders;

public readonly struct ConstantEntry : IEquatable<ConstantEntry>
{
    public ConstantEntry(string name, uint value)
    {
        Name = name;
        Value = value;
    }

    public string Name { get; }
    public uint Value { get; }

    public override bool Equals(object? obj) => obj is ConstantEntry other && Equals(other);
    public bool Equals(ConstantEntry other) => Name == other.Name && Value == other.Value;
    public override int GetHashCode() => HashCode.Combine(Name, Value);
    public static bool operator ==(ConstantEntry left, ConstantEntry right) => left.Equals(right);
    public static bool operator !=(ConstantEntry left, ConstantEntry right) => !left.Equals(right);
}

public interface IShaderSpecKey
{
    int Hash { get; }
    ReadOnlySpan<ConstantEntry> ToEntries();
}