using System;
using System.Collections.Generic;

namespace Etch.TaskVerifier;

public readonly struct Directive
{
    public string Verb { get; }
    public Dictionary<string, string> Args { get; }
    public int LineNumber { get; }
    public string RawText { get; }

    public Directive(string verb, Dictionary<string, string> args, int lineNumber, string rawText)
    {
        Verb = verb;
        Args = args;
        LineNumber = lineNumber;
        RawText = rawText;
    }
}
