using Etch.Primitives;

namespace Etch.Primitives.Tests;

internal sealed class SpansTests
{
    [Test]
    public async Task WriteAndRead_I32LE_RoundTrips()
    {
        byte[] buffer = new byte[4];
        var writer = new SpanWriter(buffer);
        writer.WriteI32LE(0x12345678);
        int position = writer.Position;

        var reader = new SpanReader(buffer);
        int value = reader.ReadI32LE();

        await Assert.That(position == 4).IsTrue();
        await Assert.That(value == 0x12345678).IsTrue();
    }

    [Test]
    public async Task WriteAndRead_VarInt_RoundTrips()
    {
        byte[] buffer = new byte[16];
        ulong[] values = { 0, 1, 127, 128, 16383, 16384, 0x7FFF };

        var writer = new SpanWriter(buffer);
        foreach (var v in values)
        {
            writer.WriteVarInt(v);
        }

        var reader = new SpanReader(buffer);
        ulong[] actuals = new ulong[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            actuals[i] = reader.ReadVarInt();
        }

        for (int i = 0; i < values.Length; i++)
        {
            await Assert.That(actuals[i] == values[i]).IsTrue();
        }
    }

    [Test]
    public async Task WriteVarInt_OverflowThrows()
    {
        byte[] buffer = new byte[2];
        var writer = new SpanWriter(buffer);
        writer.WriteVarInt(0x80);
        bool threw = false;
        try
        {
            writer.WriteVarInt(1);
        }
        catch (EtchException)
        {
            threw = true;
        }
        await Assert.That(threw).IsTrue();
    }

    [Test]
    public async Task StackBuffer64_AsSpan_ReturnsCorrectLength()
    {
        var stack = new StackBuffer64<uint>();
        int length = ((Span<uint>)stack).Length;
        await Assert.That(length == 64).IsTrue();
    }

    [Test]
    public async Task WriteByte_OverflowThrows()
    {
        byte[] buffer = new byte[1];
        var writer = new SpanWriter(buffer);
        writer.WriteByte(1);
        bool threw = false;
        try
        {
            writer.WriteByte(2);
        }
        catch (EtchException)
        {
            threw = true;
        }
        await Assert.That(threw).IsTrue();
    }
}