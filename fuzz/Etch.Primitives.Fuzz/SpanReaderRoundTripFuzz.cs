using System;
using System.IO;
using Etch.Fuzz.Shared;
using Etch.Primitives;
using SharpFuzz;

namespace Etch.Primitives.Fuzz;

public static class Program
{
    public static void Main(string[] args)
        => Fuzzer.Run(Fuzz);

    private static void Fuzz(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var bytes = ms.ToArray();

        FuzzGuard.Run(bytes, static input =>
        {
            var r = new SpanReader(input);

            while (r.Remaining >= 4)
            {
                switch (input[r.Position & 0b11])
                {
                    case 0:
                        _ = r.ReadU32LE();
                        break;
                    case 1:
                        _ = r.ReadI32LE();
                        break;
                    case 2:
                        _ = r.ReadF32LE();
                        break;
                    case 3 when r.Remaining >= 8:
                        _ = r.ReadU64LE();
                        break;
                }
            }

            while (r.Remaining > 0)
            {
                switch (input[r.Position & 0b11])
                {
                    case 0:
                    case 1:
                    case 2:
                        _ = r.ReadByte();
                        break;
                    case 3:
                        if (r.Remaining >= 2)
                            _ = r.ReadVarInt();
                        else
                            _ = r.ReadByte();
                        break;
                }
            }
        });
    }
}
