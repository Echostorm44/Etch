using BenchmarkDotNet.Running;

namespace Etch.Primitives.Bench;

public static class Program
{
    public static void Main(string[] args)
    {
        BenchmarkRunner.Run<SpanWriterBench>(config: null, args: args);
    }
}
