using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Etch.TaskVerifier.Reporting;

public sealed class JsonReport
{
    private static readonly JsonSerializerOptions s_serializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string ToolVersion { get; init; } = "1.0.0";
    public string Timestamp { get; init; } = "";
    public string Command { get; init; } = "";
    public int TotalChecks { get; init; }
    public int Passed { get; init; }
    public int Failed { get; init; }
    public int Skipped { get; init; }
    public int Errors { get; init; }
    public List<CheckResultJson> Checks { get; init; } = new();

    public static JsonReport FromResults(string command, IEnumerable<CheckResult> results)
    {
        var resultsList = results.ToList();
        int passed = 0, failed = 0, skipped = 0, errors = 0;
        var checks = new List<CheckResultJson>();

        foreach (var result in resultsList)
        {
            var json = new CheckResultJson
            {
                Verb = result.Verb,
                Args = new Dictionary<string, string>(result.Args),
                Status = result.Status.ToString().ToUpperInvariant(),
                Detail = result.Detail,
                DurationMs = result.DurationMs,
                TaskFile = result.TaskFile,
                LineNumber = result.LineNumber
            };
            checks.Add(json);

            switch (result.Status)
            {
                case CheckStatus.Pass: passed++; break;
                case CheckStatus.Fail: failed++; break;
                case CheckStatus.Skipped: skipped++; break;
                case CheckStatus.Error: errors++; break;
            }
        }

        return new JsonReport
        {
            Command = command,
            Timestamp = DateTime.UtcNow.ToString("O"),
            TotalChecks = resultsList.Count,
            Passed = passed,
            Failed = failed,
            Skipped = skipped,
            Errors = errors,
            Checks = checks
        };
    }

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, s_serializerOptions);
    }
}

public sealed class CheckResultJson
{
    public string Verb { get; init; } = "";
    public Dictionary<string, string> Args { get; init; } = new();
    public string Status { get; init; } = "";
    public string Detail { get; init; } = "";
    public long DurationMs { get; init; }
    public string TaskFile { get; init; } = "";
    public int LineNumber { get; init; }
}
