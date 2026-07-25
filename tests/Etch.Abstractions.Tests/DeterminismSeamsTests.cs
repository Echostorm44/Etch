using Etch.Abstractions.Determinism;
using Etch.Abstractions.Determinism.Testing;

namespace Etch.Tests.Determinism;

internal sealed class DeterminismSeamsTests
{
    [Test]
    public async Task FixedFrameClockAdvanceIncrementsFrameAndNanos()
    {
        var clock = new FixedFrameClock(0L, 0L);
        long frameBefore = clock.FrameCounter;
        long nanosBefore = clock.NowNanos();

        clock.Advance(16_666_667L);

        long frameAfter = clock.FrameCounter;
        long nanosAfter = clock.NowNanos();

        await Assert.That(frameAfter - frameBefore == 1).IsTrue();
        await Assert.That(nanosAfter - nanosBefore == 16_666_667L).IsTrue();
    }

    [Test]
    public async Task DeterministicEntropySourceNextUInt64IsDeterministic()
    {
        var entropy1 = new DeterministicEntropySource(12345UL);
        var entropy2 = new DeterministicEntropySource(12345UL);

        ulong val1 = entropy1.NextUInt64();
        ulong val2 = entropy2.NextUInt64();

        await Assert.That(val1 == val2).IsTrue();
    }

    [Test]
    public async Task DeterministicEntropySourceFillMatchesNextUInt64Sequence()
    {
        var entropy = new DeterministicEntropySource(999UL);
        Span<byte> bytes = stackalloc byte[32];
        entropy.Fill(bytes);

        var sequential = new DeterministicEntropySource(999UL);
        byte[] localBytes = bytes.ToArray();
        for (int i = 0; i < 32; i++)
        {
            byte expected = (byte)sequential.NextUInt32();
            await Assert.That(localBytes[i] == expected).IsTrue();
        }
    }

    [Test]
    public async Task DeterministicParallelTileSchedulerProducesDeterministicOutput()
    {
        var scheduler = new DeterministicParallelTileScheduler(4);
        int[] results = new int[100];

        scheduler.ForEachTile(100, results, static (tileIndex, state) =>
        {
            state[tileIndex] = tileIndex * 2 + 1;
        });

        int expected = 0;
        bool allMatch = true;
        for (int i = 0; i < 100; i++)
        {
            expected = i * 2 + 1;
            if (results[i] != expected)
            {
                allMatch = false;
                break;
            }
        }

        await Assert.That(allMatch).IsTrue();
    }

    [Test]
    public async Task InMemoryShaderSourceRegisterAndRetrieve()
    {
        var source = new InMemoryShaderSource();
        var id = ShaderId.Create("test-shader");
        byte[] spirv = [1, 2, 3];
        byte[] wgsl = [4, 5, 6];

        source.Register(id, spirv, wgsl);

        ReadOnlySpan<byte> retrievedSpirv = source.GetSpirv(id);
        ReadOnlySpan<byte> retrievedWgsl = source.GetWgsl(id);
        bool spirvEqual = retrievedSpirv.SequenceEqual(spirv);
        bool wgslEqual = retrievedWgsl.SequenceEqual(wgsl);

        await Assert.That(spirvEqual).IsTrue();
        await Assert.That(wgslEqual).IsTrue();
    }

    [Test]
    public async Task InMemoryFileSystemWriteAndReadRoundTrip()
    {
        var fs = new InMemoryFileSystem();
        string path = "/test/file.txt";
        string content = "Hello, Determinism!";

        fs.WriteAllText(path, content);
        string readBack = fs.ReadAllText(path);

        await Assert.That(readBack == content).IsTrue();
    }

    [Test]
    public async Task ByteIdenticalUnderFakesRunsTwiceProducesIdenticalOutput()
    {
        var clock = new FixedFrameClock(0L, 0L);
        var entropy = new DeterministicEntropySource(42UL);
        var scheduler = new DeterministicParallelTileScheduler(4);
        var shaderSource = new InMemoryShaderSource();
        var fileSystem = new InMemoryFileSystem();

        byte[] run1 = RunSyntheticWorkload(clock, entropy, scheduler, shaderSource, fileSystem);
        clock = new FixedFrameClock(0L, 0L);
        entropy = new DeterministicEntropySource(42UL);
        scheduler = new DeterministicParallelTileScheduler(4);
        shaderSource = new InMemoryShaderSource();
        fileSystem = new InMemoryFileSystem();
        byte[] run2 = RunSyntheticWorkload(clock, entropy, scheduler, shaderSource, fileSystem);

        await Assert.That(run1.Length == run2.Length).IsTrue();
        for (int i = 0; i < run1.Length; i++)
        {
            await Assert.That(run1[i] == run2[i]).IsTrue();
        }
    }

    private static byte[] RunSyntheticWorkload(
        FixedFrameClock clock,
        DeterministicEntropySource entropy,
        DeterministicParallelTileScheduler scheduler,
        InMemoryShaderSource shaderSource,
        InMemoryFileSystem fileSystem)
    {
        int result = 0;
        int tileCount = 64;

        scheduler.ForEachTile(tileCount, result, static (tileIndex, state) =>
        {
            state += tileIndex;
        });

        Span<byte> buffer = stackalloc byte[256];
        entropy.Fill(buffer);

        for (int i = 0; i < 16; i++)
        {
            buffer[i] ^= (byte)(entropy.NextUInt32() & 0xFF);
        }

        using var stream = new System.IO.MemoryStream();
        foreach (byte b in buffer)
        {
            stream.WriteByte(b);
        }

        return stream.ToArray();
    }
}