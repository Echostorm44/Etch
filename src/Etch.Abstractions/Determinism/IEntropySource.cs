namespace Etch.Abstractions.Determinism;

/// <summary>
/// Provides cryptographically-safe entropy for deterministic rendering.
/// All non-deterministic RNG must route through this seam.
/// </summary>
/// <remarks>
/// Failure to use this seam causes pixel-identical output guarantees to be violated,
/// as RNG state varies across machines and runs.
/// </remarks>
public interface IEntropySource
{
    /// <summary>
    /// Fills the specified span with entropy bytes.
    /// </summary>
    void Fill(Span<byte> bytes);

    /// <summary>
    /// Returns the next 64-bit unsigned integer.
    /// </summary>
    ulong NextUInt64();

    /// <summary>
    /// Returns the next 32-bit unsigned integer.
    /// </summary>
    uint NextUInt32();
}