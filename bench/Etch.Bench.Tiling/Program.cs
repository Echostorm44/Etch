using BenchmarkDotNet.Running;

namespace Etch.Bench.Tiling;

public static class Program
{
    public static void Main(string[] args)
    {
        BenchmarkRunner.Run<TileSizeBenchmark>(config: null, args: args);
        BenchmarkRunner.Run<ParallelClassificationBenchmark>(config: null, args: args);
    }
}
