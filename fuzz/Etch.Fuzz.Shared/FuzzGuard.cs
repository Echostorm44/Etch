using System;
using Etch;

namespace Etch.Fuzz.Shared;

public delegate void FuzzBody<T>(T input) where T : allows ref struct;

public static class FuzzGuard
{
    public static void Run(ReadOnlySpan<byte> input, FuzzBody<ReadOnlySpan<byte>> body)
    {
        try
        {
            body(input);
        }
        catch (EtchException)
        {
        }
    }
}
