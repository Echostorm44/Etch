namespace Etch.Abstractions.Determinism;

/// <summary>
/// Delegate for tile body iteration.
/// </summary>
public delegate void TileBody<TState>(int tileIndex, TState state);

/// <summary>
/// Schedules tile processing for deterministic parallel execution.
/// All non-deterministic task scheduling must route through this seam.
/// </summary>
/// <remarks>
/// Failure to use this seam causes pixel-identical output guarantees to be violated,
/// as thread ordering varies across machines and runs. Implementations may be sequential
/// or deterministic-parallel, but must guarantee ordered reduction per D-014.
/// </remarks>
public interface ITileScheduler
{
    /// <summary>
    /// Iterates over each tile, invoking <paramref name="body"/> with the tile index and state.
    /// Implementations choose sequential or deterministic-parallel execution.
    /// </summary>
    void ForEachTile<TState>(int tileCount, TState state, TileBody<TState> body);
}