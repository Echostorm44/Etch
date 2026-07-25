namespace Etch.Abstractions.Determinism;

/// <summary>
/// Provides monotonic time and frame sequencing for deterministic rendering.
/// All non-deterministic wall-clock access must route through this seam.
/// </summary>
/// <remarks>
/// Failure to use this seam causes pixel-identical output guarantees to be violated,
/// as wall-clock time varies across machines and runs.
/// </remarks>
public interface IFrameClock
{
    /// <summary>
    /// Returns monotonic time in nanoseconds.
    /// </summary>
    long NowNanos();

    /// <summary>
    /// Returns the current frame counter. Implementations must guarantee
    /// FrameCounter increases by exactly 1 per frame.
    /// </summary>
    long FrameCounter { get; }
}