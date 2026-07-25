using BenchmarkDotNet.Running;
using Etch.Bench.Text;

namespace Etch.Bench.Text;

public static class Program
{
    public static void Main(string[] args)
    {
        BenchmarkRunner.Run(typeof(Program).Assembly, args: args);
    }
}