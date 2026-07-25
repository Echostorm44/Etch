using System;
using System.IO;
using System.Text.Json;

if (args.Length < 1)
{
    Console.WriteLine("Usage: Etch.SoakReport <soak-report.json>");
    Console.WriteLine("Reads a soak-test report and prints a summary.");
    return 1;
}

string path = args[0];
if (!File.Exists(path))
{
    Console.Error.WriteLine($"File not found: {path}");
    return 1;
}

string json = File.ReadAllText(path);
using var doc = JsonDocument.Parse(json);
var root = doc.RootElement;

Console.WriteLine("=== Soak Test Report ===");
Console.WriteLine();

if (root.TryGetProperty("startTime", out var startTime))
    Console.WriteLine($"Start time:     {startTime.GetString()}");
if (root.TryGetProperty("durationHours", out var dur))
    Console.WriteLine($"Duration:       {dur.GetDouble():F1} hours");
if (root.TryGetProperty("frameCount", out var fc))
    Console.WriteLine($"Frames:         {fc.GetInt32()}");
if (root.TryGetProperty("panics", out var panics))
    Console.WriteLine($"Panics:         {panics.GetInt32()}");
Console.WriteLine();
Console.WriteLine("--- Frame Times ---");
if (root.TryGetProperty("p50FrameMs", out var p50))
    Console.WriteLine($"  p50:          {p50.GetDouble():F2} ms");
if (root.TryGetProperty("p95FrameMs", out var p95))
    Console.WriteLine($"  p95:          {p95.GetDouble():F2} ms");
if (root.TryGetProperty("p99FrameMs", out var p99))
    Console.WriteLine($"  p99:          {p99.GetDouble():F2} ms");
if (root.TryGetProperty("p999FrameMs", out var p999))
    Console.WriteLine($"  p99.9:        {p999.GetDouble():F2} ms");
Console.WriteLine();
Console.WriteLine("--- Memory ---");
if (root.TryGetProperty("gcSpikes", out var gc))
    Console.WriteLine($"  GC spikes:    {gc.GetInt32()}");
if (root.TryGetProperty("totalAllocBytes", out var alloc))
    Console.WriteLine($"  Total alloc:  {alloc.GetInt64() / (1024 * 1024)} MB");
if (root.TryGetProperty("rssEndBytes", out var rss))
    Console.WriteLine($"  RSS end:      {rss.GetInt64() / (1024 * 1024)} MB");
if (root.TryGetProperty("rssGrowthPercent", out var growth))
    Console.WriteLine($"  RSS growth:   {growth.GetDouble():F1}%");
Console.WriteLine();

if (root.TryGetProperty("gcPausesMs", out var pauses) && pauses.GetArrayLength() > 0)
{
    Console.WriteLine("--- GC Pauses ---");
    foreach (var p in pauses.EnumerateArray())
        Console.WriteLine($"  {p.GetDouble():F1} ms");
}

Console.WriteLine();
bool passed = panics.GetInt32() == 0
    && (root.TryGetProperty("gcSpikes", out var gcs) && gcs.GetInt32() == 0)
    && (root.TryGetProperty("rssGrowthPercent", out var g) && g.GetDouble() <= 20.0);

Console.WriteLine(passed ? "PASS" : "FAIL");
return passed ? 0 : 1;
