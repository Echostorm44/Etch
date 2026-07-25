using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Etch.TaskVerifier;

public static partial class DirectiveParser
{
    private const string VerifyPattern = @"<!--\s*verify:\s*([\w][\w-]*)\s*([\s\S]*?)\s*-->";

    [GeneratedRegex(VerifyPattern)]
    private static partial Regex VerifyRegex();

    public static IEnumerable<Directive> ParseFile(string path)
    {
        if (!File.Exists(path))
            yield break;

        var lines = File.ReadAllLines(path);
        for (int i = 0; i < lines.Length; i++)
        {
            foreach (var directive in ParseLine(lines[i], i + 1))
                yield return directive;
        }
    }

    public static IEnumerable<Directive> ParseLine(string line, int lineNumber)
    {
        var regex = VerifyRegex();
        var matches = regex.Matches(line);

        foreach (Match match in matches)
        {
            string verb = match.Groups[1].Value;
            string argsText = match.Groups[2].Value;

            var args = ParseArgs(argsText);
            string raw = match.Value;

            yield return new Directive(verb, args, lineNumber, raw);
        }
    }

    private static Dictionary<string, string> ParseArgs(string argsText)
    {
        var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        int pos = 0;
        while (pos < argsText.Length)
        {
            pos = SkipWhitespace(argsText, pos);

            if (pos >= argsText.Length)
                break;

            int keyStart = pos;
            while (pos < argsText.Length && IsKeyChar(argsText[pos]))
                pos++;

            if (pos <= keyStart)
                break;

            string key = argsText.Substring(keyStart, pos - keyStart).Trim();
            pos = SkipWhitespace(argsText, pos);

            if (pos >= argsText.Length || argsText[pos] != '=')
                break;

            pos++;
            pos = SkipWhitespace(argsText, pos);

            string value;
            if (pos >= argsText.Length)
            {
                value = string.Empty;
            }
            else if (argsText[pos] == '"')
            {
                pos++;
                int valueStart = pos;
                while (pos < argsText.Length && argsText[pos] != '"')
                    pos++;
                value = argsText.Substring(valueStart, pos - valueStart);
                if (pos < argsText.Length && argsText[pos] == '"')
                    pos++;
            }
            else
            {
                int valueStart = pos;
                while (pos < argsText.Length && !char.IsWhiteSpace(argsText[pos]))
                    pos++;
                value = argsText.Substring(valueStart, pos - valueStart);
            }

            if (!string.IsNullOrEmpty(key))
                args[key] = value;

            pos = SkipWhitespace(argsText, pos);
        }

        return args;
    }

    private static int SkipWhitespace(string s, int pos)
    {
        while (pos < s.Length && char.IsWhiteSpace(s[pos]))
            pos++;
        return pos;
    }

    private static bool IsKeyChar(char c)
    {
        return char.IsLetterOrDigit(c) || c == '-' || c == '_';
    }
}
