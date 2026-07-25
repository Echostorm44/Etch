using BenchmarkDotNet.Running;

namespace Etch.Scene.Bench;

public static class Program
{
    public static int Main(string[] args)
    {
        var summary = BenchmarkRunner.Run<SerializationBench>();
        summary = BenchmarkRunner.Run<SceneBuilderBench>();
        summary = BenchmarkRunner.Run<DamageDiffBench>();
        return 0;
    }
}