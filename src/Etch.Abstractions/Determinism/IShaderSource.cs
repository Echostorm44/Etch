namespace Etch.Abstractions.Determinism;

/// <summary>
/// Provides shader source code for deterministic rendering.
/// All shader lookups must route through this seam.
/// </summary>
/// <remarks>
/// Failure to use this seam causes pixel-identical output guarantees to be violated,
/// as shader file lookups vary across machines and runs. No disk I/O is permitted;
/// all shaders are embedded resources identified by <see cref="ShaderId"/>.
/// </remarks>
public interface IShaderSource
{
    /// <summary>
    /// Returns the SPIR-V bytecode for the specified shader.
    /// </summary>
    ReadOnlySpan<byte> GetSpirv(ShaderId id);

    /// <summary>
    /// Returns the WGSL source for the specified shader.
    /// </summary>
    ReadOnlySpan<byte> GetWgsl(ShaderId id);
}

/// <summary>
/// Identifies a shader for lookup via <see cref="IShaderSource"/>.
/// </summary>
#pragma warning disable CA1815 // Override equality for struct used as key in Determinism.Seams
public readonly struct ShaderId : IEquatable<ShaderId>
{
    public static ShaderId Create(string name) => new(0u, name);
    public static ShaderId Create(uint id, string name) => new(id, name);

    private ShaderId(uint id, string name)
    {
        Id = id;
        Name = name;
    }

    public uint Id { get; }
    public string Name { get; }

    public bool Equals(ShaderId other) => other.Id == Id && other.Name == Name;
    public override bool Equals(object? obj) => obj is ShaderId other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Id, Name);
}
#pragma warning restore CA1815