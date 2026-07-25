using BenchmarkDotNet.Running;

namespace Etch.Bench.Blend;

public static class Program
{
    public static int Main(string[] args)
    {
        var summary = BenchmarkRunner.Run<FullStackBlendBenchmark>();
        return 0;
    }
}
