using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Etch.PixelParity.Tests;

internal sealed class AdapterMatrix
{
    private readonly Dictionary<string, AdapterResult> _results = new();

    public void Record(string corpusEntry, string adapterName, bool passed, string? notes = null)
    {
        var key = $"{corpusEntry}|{adapterName}";
        _results[key] = new AdapterResult
        {
            Passed = passed,
            Notes = notes,
            Timestamp = DateTime.UtcNow
        };
    }

    public void DumpToFile(string outputPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# GPU Adapter Compatibility Matrix");
        sb.AppendLine();
        sb.AppendLine($"**Generated:** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();
        sb.AppendLine("| Corpus Entry | Adapter | Status | Notes |");
        sb.AppendLine("|-------------|---------|--------|-------|");

        foreach (var kvp in _results)
        {
            var parts = kvp.Key.Split('|');
            var entry = parts[0];
            var adapter = parts[1];
            var result = kvp.Value;

            var status = result.Passed ? "PASS" : "FAIL";
            var notes = result.Notes ?? "-";

            sb.AppendLine($"| {entry} | {adapter} | {status} | {notes} |");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        File.WriteAllText(outputPath, sb.ToString());
    }

    private struct AdapterResult
    {
        public bool Passed;
        public string? Notes;
        public DateTime Timestamp;
    }
}
