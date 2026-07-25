using System;
using System.Buffers.Binary;

namespace Etch.Abstractions.Diagnostics;

public static class SceneReproWriter
{
    public static int CalculateEnvelopeSize(int sectionCount, ReadOnlySpan<int> sectionSizes)
    {
        int total = SceneReproFormat.HeaderSize;
        for (int i = 0; i < sectionCount; i++)
        {
            total += SceneReproFormat.SectionHeaderSize;
            total += sectionSizes[i];
        }
        return total;
    }

    public static bool TryWriteEnvelope(
        Span<byte> destination,
        uint version,
        ReadOnlySpan<ReproSection> sectionIds,
        byte[][] sectionPayloads,
        out int bytesWritten)
    {
        if (destination.Length < SceneReproFormat.HeaderSize)
        {
            bytesWritten = 0;
            return false;
        }

        int position = 0;

        destination[position++] = (byte)'E';
        destination[position++] = (byte)'T';
        destination[position++] = (byte)'R';
        destination[position++] = (byte)'P';
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(position, 4), version);
        position += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(position, 4), (uint)sectionIds.Length);
        position += 4;

        for (int i = 0; i < sectionIds.Length; i++)
        {
            if (position + SceneReproFormat.SectionHeaderSize > destination.Length)
            {
                bytesWritten = 0;
                return false;
            }

            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(position, 4), (uint)sectionIds[i]);
            position += 4;

            int payloadLength = sectionPayloads[i].Length;
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(position, 4), (uint)payloadLength);
            position += 4;

            if (position + payloadLength > destination.Length)
            {
                bytesWritten = 0;
                return false;
            }

            var payloadSpan = sectionPayloads[i].AsSpan();
            payloadSpan.CopyTo(destination.Slice(position));
            position += payloadLength;
        }

        bytesWritten = position;
        return true;
    }
}
