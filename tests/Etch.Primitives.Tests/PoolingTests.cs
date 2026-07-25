using Etch.Primitives;

namespace Etch.Primitives.Tests;

internal sealed class PoolingTests
{
    [Test]
    public async Task Rent_ReturnsBufferOfAtLeastRequestedLength()
    {
        var buf = PooledBuffer<byte>.Rent(256);
        int length = buf.Length;
        int spanLength = buf.Span.Length;
        buf.Dispose();

        await Assert.That(length >= 256).IsTrue();
        await Assert.That(spanLength >= 256).IsTrue();
    }

    [Test]
    public void Dispose_IsIdempotent()
    {
        var buf = PooledBuffer<byte>.Rent(64);
        buf.Dispose();
        buf.Dispose();
    }

    [Test]
    public async Task MemoryBudget_TryAllocate_TracksCorrectly()
    {
        var budget = new MemoryBudget(100);
        bool first = budget.TryAllocate(30);
        long remaining = budget.Remaining;
        bool second = budget.TryAllocate(70);
        bool exhausted = budget.IsExhausted;
        bool third = budget.TryAllocate(1);

        await Assert.That(first).IsTrue();
        await Assert.That(remaining == 70).IsTrue();
        await Assert.That(second).IsTrue();
        await Assert.That(exhausted).IsTrue();
        await Assert.That(third).IsFalse();
    }
}