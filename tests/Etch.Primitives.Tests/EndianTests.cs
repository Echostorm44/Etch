using Etch.Primitives;

namespace Etch.Primitives.Tests;

internal sealed class EndianTests
{
    [Test]
    public async Task ReadWriteU16LE_RoundTrips()
    {
        byte[] buffer = new byte[2];
        Endian.WriteU16LE(buffer, 0x1234);
        await Assert.That(Endian.ReadU16LE(buffer) == 0x1234).IsTrue();
    }

    [Test]
    public async Task ReadWriteU32LE_RoundTrips()
    {
        byte[] buffer = new byte[4];
        Endian.WriteU32LE(buffer, 0x12345678);
        await Assert.That(Endian.ReadU32LE(buffer) == 0x12345678).IsTrue();
    }

    [Test]
    public async Task ReadWriteU64LE_RoundTrips()
    {
        byte[] buffer = new byte[8];
        Endian.WriteU64LE(buffer, 0x123456789ABCDEF0);
        await Assert.That(Endian.ReadU64LE(buffer) == 0x123456789ABCDEF0).IsTrue();
    }
}