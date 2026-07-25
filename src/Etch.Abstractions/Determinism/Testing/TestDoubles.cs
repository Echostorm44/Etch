using System.ComponentModel;

namespace Etch.Abstractions.Determinism.Testing;

/// <summary>
/// Test double for <see cref="IFrameClock"/> — fixed time and frame counter for deterministic replay.
/// </summary>
/// <remarks>This type is compiled into the main assembly but is for test use only.</remarks>
[Browsable(false)]
public sealed class FixedFrameClock : IFrameClock
{
    private long _frame;
    private long _nanos;

    public FixedFrameClock() : this(0L, 0L) { }

    public FixedFrameClock(long initialNanos, long initialFrame)
    {
        _nanos = initialNanos;
        _frame = initialFrame;
    }

    public long NowNanos() => _nanos;

    public long FrameCounter => _frame;

    public void Advance(long nanosPerFrame)
    {
        _frame++;
        _nanos += nanosPerFrame;
    }
}

/// <summary>
/// Test double for <see cref="IEntropySource"/> — deterministic Xorshift64 for reproducible output.
/// </summary>
/// <remarks>This type is compiled into the main assembly but is for test use only.</remarks>
[Browsable(false)]
public sealed class DeterministicEntropySource : IEntropySource
{
    private ulong _state;

    public DeterministicEntropySource() : this(0xDEADBEEFUL) { }

    public DeterministicEntropySource(ulong seed) => _state = seed;

    public void Fill(Span<byte> bytes)
    {
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)NextUInt32();
        }
    }

    public ulong NextUInt64()
    {
        _state ^= _state >> 12;
        _state ^= _state << 25;
        _state ^= _state >> 27;
        return _state * 0x2545_5F49_7C3A_1D1FUL;
    }

    public uint NextUInt32() => (uint)(NextUInt64() >> 32);
}

/// <summary>
/// Test double for <see cref="ITileScheduler"/> — deterministic parallel execution with ordered reduction.
/// </summary>
/// <remarks>
/// This type is compiled into the main assembly but is for test use only.
/// Results are invariant under thread count per D-014 parallel reduction contract.
/// </remarks>
[Browsable(false)]
public sealed class DeterministicParallelTileScheduler : ITileScheduler
{
    private readonly int _maxDegreeOfParallelism;

    public DeterministicParallelTileScheduler() : this(Environment.ProcessorCount) { }

    public DeterministicParallelTileScheduler(int maxDegreeOfParallelism)
    {
        _maxDegreeOfParallelism = maxDegreeOfParallelism;
    }

    public void ForEachTile<TState>(int tileCount, TState state, TileBody<TState> body)
    {
        if (tileCount <= 1 || _maxDegreeOfParallelism <= 1)
        {
            for (int i = 0; i < tileCount; i++)
            {
                body(i, state);
            }
            return;
        }

        int chunkSize = Math.Max(1, tileCount / _maxDegreeOfParallelism);
        int start = 0;

        using var barrier = new System.Threading.ManualResetEvent(false);
        int activeCount = 0;
        object lockObj = new();

        TState capturedState = state;

        for (int thread = 0; thread < _maxDegreeOfParallelism; thread++)
        {
            int threadStart = start;
            int threadEnd = Math.Min(start + chunkSize, tileCount);
            start = threadEnd;

            if (threadStart >= tileCount)
                break;

            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                for (int i = threadStart; i < threadEnd; i++)
                {
                    body(i, capturedState);
                }

                lock (lockObj)
                {
                    activeCount--;
                    if (activeCount == 0)
                    {
                        barrier.Set();
                    }
                }
            });

            lock (lockObj)
            {
                activeCount++;
            }
        }

        barrier.WaitOne();
    }
}

/// <summary>
/// Test double for <see cref="IShaderSource"/> — in-memory shader storage for deterministic testing.
/// </summary>
/// <remarks>This type is compiled into the main assembly but is for test use only.</remarks>
[Browsable(false)]
public sealed class InMemoryShaderSource : IShaderSource
{
    private readonly Dictionary<ShaderId, (byte[] Spirv, byte[] Wgsl)> _shaders = new();

    public void Register(ShaderId id, byte[] spirv, byte[] wgsl)
    {
        _shaders[id] = (spirv, wgsl);
    }

    public ReadOnlySpan<byte> GetSpirv(ShaderId id)
    {
        return _shaders.TryGetValue(id, out var shader) ? shader.Spirv : ReadOnlySpan<byte>.Empty;
    }

    public ReadOnlySpan<byte> GetWgsl(ShaderId id)
    {
        return _shaders.TryGetValue(id, out var shader) ? shader.Wgsl : ReadOnlySpan<byte>.Empty;
    }
}

/// <summary>
/// Test double for <see cref="IFileSystem"/> — in-memory file storage for deterministic testing.
/// </summary>
/// <remarks>This type is compiled into the main assembly but is for test use only.</remarks>
[Browsable(false)]
public sealed class InMemoryFileSystem : IFileSystem
{
    private readonly Dictionary<string, byte[]> _files = new();

    public void WriteAllBytes(string path, byte[] data)
    {
        _files[path] = data;
    }

    public void WriteAllText(string path, string content)
    {
        _files[path] = System.Text.Encoding.UTF8.GetBytes(content);
    }

    public byte[] ReadAllBytes(string path)
    {
        return _files.TryGetValue(path, out var data) ? data : Array.Empty<byte>();
    }

    public string ReadAllText(string path)
    {
        return _files.TryGetValue(path, out var data) ? System.Text.Encoding.UTF8.GetString(data) : string.Empty;
    }
}