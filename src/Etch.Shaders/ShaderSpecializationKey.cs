using System;
using System.Collections.Generic;

namespace Etch.Shaders;

public readonly struct ShaderSpecializationKey<T> : IEquatable<ShaderSpecializationKey<T>>, IShaderSpecKey
    where T : struct, IShaderSpecKey
{
    public ShaderSpecializationKey(T key)
    {
        Key = key;
    }

    public T Key { get; }

    public int Hash => Key.Hash;

    public ReadOnlySpan<ConstantEntry> ToEntries() => Key.ToEntries();

    public override bool Equals(object? obj) => obj is ShaderSpecializationKey<T> other && Equals(other);
    public bool Equals(ShaderSpecializationKey<T> other) => EqualityComparer<T>.Default.Equals(Key, other.Key);
    public override int GetHashCode() => Hash;
    public static bool operator ==(ShaderSpecializationKey<T> left, ShaderSpecializationKey<T> right) => left.Equals(right);
    public static bool operator !=(ShaderSpecializationKey<T> left, ShaderSpecializationKey<T> right) => !left.Equals(right);
}