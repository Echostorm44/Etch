using System.Diagnostics;
using BenchmarkDotNet.Running;

namespace Etch.Bench.Compose;

public static class Program
{
    /// <summary>
    /// BenchmarkDotNet by default; <c>--quick [threads] [frames]</c> prints median frame times and
    /// bytes allocated per frame with a plain stopwatch (for iteration, not for the record);
    /// <c>ETCH_BENCH_SKIP_GLYPHS=1</c> leaves text out, to split its cost from geometry's.
    /// </summary>
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--quick")
        {
            int threads = args.Length > 1 ? int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : -1;
            int frames = args.Length > 2 ? int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 60;
            Quick(threads, frames);
            return;
        }
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }

    private static void Quick(int threads, int frames)
    {
        using var harness = new ComposeHarness(1920, 1080, 1f);
        harness.Composer.MaxDegreeOfParallelism = threads;
        harness.Composer.SkipGlyphs = Environment.GetEnvironmentVariable("ETCH_BENCH_SKIP_GLYPHS") == "1";
        for (int i = 0; i < 10; i++)
        {
            harness.Build();
            harness.Render();
        }
        if (Environment.GetEnvironmentVariable("ETCH_BENCH_BLINK_FIRST") == "1")
        {
            var early = new double[frames];
            for (int i = 0; i < frames; i++)
            {
                long e0 = Stopwatch.GetTimestamp();
                harness.BlinkFrame();
                early[i] = Stopwatch.GetElapsedTime(e0).TotalMilliseconds;
            }
            Array.Sort(early);
            Console.WriteLine($"caret blink (before timed renders): median {early[frames / 2]:F3} ms");
        }
        var build = new double[frames];
        var render = new double[frames];
        long allocBefore = GC.GetTotalAllocatedBytes(true);
        for (int i = 0; i < frames; i++)
        {
            long t0 = Stopwatch.GetTimestamp();
            harness.Build();
            long t1 = Stopwatch.GetTimestamp();
            harness.Render();
            long t2 = Stopwatch.GetTimestamp();
            build[i] = Stopwatch.GetElapsedTime(t0, t1).TotalMilliseconds;
            render[i] = Stopwatch.GetElapsedTime(t1, t2).TotalMilliseconds;
        }
        long allocated = GC.GetTotalAllocatedBytes(true) - allocBefore;
        Array.Sort(build);
        Array.Sort(render);
        var l = harness.List;
        var blinks = new double[frames];
        long damaged = 0;
        for (int i = 0; i < frames; i++)
        {
            long b0 = Stopwatch.GetTimestamp();
            damaged = harness.BlinkFrame();
            blinks[i] = Stopwatch.GetElapsedTime(b0).TotalMilliseconds;
        }
        Array.Sort(blinks);
        Console.WriteLine($"caret blink: median {blinks[frames / 2]:F3} ms (min {blinks[0]:F3}), {damaged} px damaged");
        Console.WriteLine($"threads {threads}: render median {render[frames / 2]:F2} ms (min {render[0]:F2}), build median {build[frames / 2]:F2} ms, allocated {allocated / frames} B/frame; shapes {l.OrderedShapes.Count} glyphs {l.OrderedGlyphs.Count} images {l.OrderedImages.Count} batches {l.Batches.Length}");
    }
}
