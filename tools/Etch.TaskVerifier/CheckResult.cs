using System;

namespace Etch.TaskVerifier;

public enum CheckStatus
{
    Pass,
    Fail,
    Skipped,
    Error
}

public sealed class CheckResult
{
    public string Verb { get; }
    public IReadOnlyDictionary<string, string> Args { get; }
    public CheckStatus Status { get; }
    public string Detail { get; }
    public long DurationMs { get; }
    public string TaskFile { get; }
    public int LineNumber { get; }

    public CheckResult(
        string verb,
        IReadOnlyDictionary<string, string> args,
        CheckStatus status,
        string detail,
        long durationMs,
        string taskFile,
        int lineNumber)
    {
        Verb = verb;
        Args = args;
        Status = status;
        Detail = detail;
        DurationMs = durationMs;
        TaskFile = taskFile;
        LineNumber = lineNumber;
    }

    public static CheckResult Pass(string verb, Dictionary<string, string> args, string detail, long ms, string file, int line)
        => new(verb, args, CheckStatus.Pass, detail, ms, file, line);

    public static CheckResult Fail(string verb, Dictionary<string, string> args, string detail, long ms, string file, int line)
        => new(verb, args, CheckStatus.Fail, detail, ms, file, line);

    public static CheckResult Skipped(string verb, Dictionary<string, string> args, string detail, long ms, string file, int line)
        => new(verb, args, CheckStatus.Skipped, detail, ms, file, line);

    public static CheckResult Error(string verb, Dictionary<string, string> args, string detail, long ms, string file, int line)
        => new(verb, args, CheckStatus.Error, detail, ms, file, line);
}
