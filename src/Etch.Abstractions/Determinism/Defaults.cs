using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Etch.Abstractions.Determinism;

/// <summary>Production default for <see cref="IFrameClock"/> — uses <see cref="System.Diagnostics.Stopwatch"/> for monotonic time.</summary>
[Obsolete("Placeholder default — connect when owning track wires the seam.", true)]
internal sealed class DefaultFrameClock : IFrameClock
{
    private long _frameCounter;

    public long NowNanos() => Stopwatch.GetTimestamp() * NanosecondsPerTick;

    public long FrameCounter => _frameCounter;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void AdvanceFrame() => Interlocked.Increment(ref _frameCounter);

    private static readonly long NanosecondsPerTick = 1_000_000_000L / Stopwatch.Frequency;
}

/// <summary>Production default for <see cref="IEntropySource"/> — uses <see cref="System.Security.Cryptography.RandomNumberGenerator"/>.</summary>
[Obsolete("Placeholder default — connect when owning track wires the seam.", true)]
internal sealed class DefaultEntropySource : IEntropySource
{
    public void Fill(Span<byte> bytes) => RandomNumberGenerator.Fill(bytes);

    public ulong NextUInt64() => BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8));

    public uint NextUInt32() => BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4));
}

/// <summary>Production default for <see cref="ITileScheduler"/> — sequential, single-threaded execution.</summary>
[Obsolete("Placeholder default — connect when owning track wires the seam.", true)]
internal sealed class SingleThreadTileScheduler : ITileScheduler
{
    public void ForEachTile<TState>(int tileCount, TState state, TileBody<TState> body)
    {
        for (int i = 0; i < tileCount; i++)
        {
            body(i, state);
        }
    }
}

/// <summary>Production default for <see cref="IShaderSource"/> — looks up embedded resources.</summary>
[Obsolete("Placeholder default — connect when owning track wires the seam.", true)]
internal sealed class EmbeddedShaderSource : IShaderSource
{
    // Value-returning methods can't funnel through Panic.NotImplemented directly — the
    // compiler's CS0161 check runs independently of [DoesNotReturn]. We throw the Panic
    // helpers' canonical exception shape by hand instead. EtchException is allow-listed
    // by ET0108 precisely for this case.
    public ReadOnlySpan<byte> GetSpirv(ShaderId id) =>
        throw new EtchException(PanicCodes.NotImplemented, "EmbeddedShaderSource.GetSpirv is not implemented.");

    public ReadOnlySpan<byte> GetWgsl(ShaderId id) =>
        throw new EtchException(PanicCodes.NotImplemented, "EmbeddedShaderSource.GetWgsl is not implemented.");
}

/// <summary>Production default for <see cref="IFileSystem"/> — maps directly to <see cref="System.IO.File"/>.</summary>
[Obsolete("Placeholder default — connect when owning track wires the seam.", true)]
internal sealed class PhysicalFileSystem : IFileSystem
{
    public byte[] ReadAllBytes(string path) => System.IO.File.ReadAllBytes(path);
    public string ReadAllText(string path) => System.IO.File.ReadAllText(path);
    public void WriteAllBytes(string path, byte[] data) => System.IO.File.WriteAllBytes(path, data);
    public void WriteAllText(string path, string content) => System.IO.File.WriteAllText(path, content);
}