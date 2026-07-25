using System;
using System.Collections.Generic;
using System.Linq;

namespace Etch.TaskVerifier;

public abstract class Check
{
    public abstract string Verb { get; }
    public abstract CheckResult Run(string verb, Dictionary<string, string> args, string taskFile, int lineNumber);
}

public static class CheckRegistry
{
    private static readonly Dictionary<string, Check> _checks = new(StringComparer.OrdinalIgnoreCase);

    public static void Register(Check check)
    {
        _checks[check.Verb] = check;
    }

    public static Check? Get(string verb)
    {
        return _checks.TryGetValue(verb, out var check) ? check : null;
    }

    public static IEnumerable<string> AllVerbs => _checks.Keys;

    public static int Count => _checks.Count;

    static CheckRegistry()
    {
        Register(new FileExistsCheck());
        Register(new AotPublishCheck());
        Register(new TUnitCheck());
        Register(new SymbolAbsentCheck());
        Register(new SymbolShapeCheck());
        Register(new TrimWarningCountCheck());
        Register(new BenchRunCheck());
        Register(new BenchAllocCheck());
    }
}
