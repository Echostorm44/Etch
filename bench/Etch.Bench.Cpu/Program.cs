using System;
using BenchmarkDotNet.Running;

namespace Etch.Bench.Cpu;

public static class Program
{
    public static void Main(string[] args)
    {
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
