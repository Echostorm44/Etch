using System;
using System.Globalization;
using System.Text;

namespace Etch.Shaders;

#pragma warning disable CA1711 // Name ends in Exception but type is not an exception — follows spec naming convention
#pragma warning disable CA1062 // Validate arguments before use — wgpuError is validated in Parse() before calling constructor
#pragma warning disable CA1032 // Standard constructors added for Exception compatibility
public sealed class ShaderCompileException
#pragma warning restore CA1032
#pragma warning restore CA1062
#pragma warning restore CA1711
{
    public static readonly PanicCode Code = PanicCodes.ShaderCompileError;
    public string ShaderName { get; }
    public string? SourcePath { get; }
    public int Line { get; }
    public int Column { get; }
    public string ErrorMessage { get; }
    public string ContextSnippet { get; }
    public string? RawWgpuText { get; }

    public ShaderCompileException(
        string shaderName,
        string? sourcePath,
        int line,
        int column,
        string message,
        string contextSnippet,
        string? rawWgpuText = null,
        string? callSite = null)
    {
        ShaderName = shaderName;
        SourcePath = sourcePath;
        Line = line;
        Column = column;
        ErrorMessage = message;
        ContextSnippet = contextSnippet;
        RawWgpuText = rawWgpuText;
    }

    public ShaderCompileException(string message)
        : this("unknown", null, 0, 0, message, "<source unavailable>", null, null)
    {
    }

    public ShaderCompileException(string message, Exception innerException)
        : this("unknown", null, 0, 0, message, "<source unavailable>", null, null)
    {
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine("ShaderCompileException " + PanicCodes.ShaderCompileError.Value + " ShaderCompileError:");
        sb.AppendLine("  shader: " + ShaderName);
        if (SourcePath != null)
        {
            sb.AppendLine("  at: " + SourcePath + ":" + Line + ":" + Column);
        }
        else
        {
            sb.AppendLine("  at: line " + Line + ", column " + Column);
        }
        sb.AppendLine();
        sb.AppendLine(ContextSnippet);
        if (!string.IsNullOrEmpty(RawWgpuText))
        {
            sb.AppendLine();
            sb.AppendLine("  wgpu: " + RawWgpuText);
        }
        if (!string.IsNullOrEmpty(ErrorMessage))
        {
            sb.AppendLine();
            sb.AppendLine("  error: " + ErrorMessage);
        }
        return sb.ToString();
    }

    public EtchException ToEtchException(string? callSite = null)
    {
        return new EtchException(PanicCodes.ShaderCompileError, ToString(), callSite);
    }
}

public static class ShaderErrorParser
{
    public static ShaderCompileException Parse(
        string wgpuError,
        string shaderName,
        string? sourcePath = null,
        string? shaderSource = null,
        string? callSite = null)
    {
        if (string.IsNullOrEmpty(wgpuError))
        {
            return new ShaderCompileException(
                shaderName,
                sourcePath,
                line: 0,
                column: 0,
                message: "<empty error>",
                contextSnippet: "<source unavailable>",
                rawWgpuText: wgpuError,
                callSite: callSite);
        }

        if (TryParseLineColumn(wgpuError, out int line, out int column, out string? extractedMessage))
        {
            string message = extractedMessage ?? wgpuError;
            string snippet = BuildContextSnippet(shaderSource, line, column);
            return new ShaderCompileException(shaderName, sourcePath, line, column, message, snippet, wgpuError, callSite);
        }

        return new ShaderCompileException(
            shaderName,
            sourcePath,
            line: 0,
            column: 0,
            message: wgpuError,
            contextSnippet: "<source unavailable>",
            rawWgpuText: wgpuError,
            callSite: callSite);
    }

    private static bool TryParseLineColumn(string error, out int line, out int column, out string? message)
    {
        line = 0;
        column = 0;
        message = null;

        int lineIdx = error.LastIndexOf(": line ", StringComparison.Ordinal);
        if (lineIdx < 0)
        {
            lineIdx = error.LastIndexOf(":line ", StringComparison.OrdinalIgnoreCase);
        }

        if (lineIdx >= 0)
        {
            int numStart = lineIdx + 7;
            while (numStart < error.Length && error[numStart] == ' ')
            {
                numStart++;
            }

            int numEnd = numStart;
            while (numEnd < error.Length && char.IsAsciiDigit(error[numEnd]))
            {
                numEnd++;
            }

            if (numEnd > numStart)
            {
                line = int.Parse(error.AsSpan(numStart, numEnd - numStart), CultureInfo.InvariantCulture);

                int columnIdx = error.IndexOf(" column ", numEnd, StringComparison.Ordinal);
                if (columnIdx >= 0 && columnIdx + 9 < error.Length)
                {
                    int colStart = columnIdx + 8;
                    while (colStart < error.Length && (error[colStart] == ' ' || error[colStart] == ':'))
                    {
                        colStart++;
                    }

                    int colEnd = colStart;
                    while (colEnd < error.Length && char.IsAsciiDigit(error[colEnd]))
                    {
                        colEnd++;
                    }

                    if (colEnd > colStart)
                    {
                        column = int.Parse(error.AsSpan(colStart, colEnd - colStart), CultureInfo.InvariantCulture);
                    }

                    int msgStart = colEnd;
                    while (msgStart < error.Length && (error[msgStart] == ' ' || error[msgStart] == ':'))
                    {
                        msgStart++;
                    }

                    if (msgStart < error.Length)
                    {
                        message = error.Substring(msgStart);
                    }

                    return line > 0;
                }
            }
        }

        int lastColon = error.LastIndexOf(':');
        if (lastColon > 0 && lastColon + 1 < error.Length && char.IsAsciiDigit(error[lastColon + 1]))
        {
            int colEnd = lastColon + 2;
            while (colEnd < error.Length && char.IsAsciiDigit(error[colEnd]))
            {
                colEnd++;
            }

            if (colEnd > lastColon + 1)
            {
                int lineColonIdx = error.LastIndexOf(':', lastColon - 1);
                if (lineColonIdx >= 0)
                {
                    int lineStart = lineColonIdx + 1;
                    while (lineStart < lastColon && (error[lineStart] == ' ' || error[lineStart] == ':'))
                    {
                        lineStart++;
                    }

                    int lineEnd = lineStart;
                    while (lineEnd < lastColon && char.IsAsciiDigit(error[lineEnd]))
                    {
                        lineEnd++;
                    }

                    if (lineEnd > lineStart)
                    {
                        line = int.Parse(error.AsSpan(lineStart, lineEnd - lineStart), CultureInfo.InvariantCulture);
                        column = int.Parse(error.AsSpan(lastColon + 1, colEnd - lastColon - 1), CultureInfo.InvariantCulture);
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static string BuildContextSnippet(string? shaderSource, int errorLine, int errorColumn)
    {
        if (string.IsNullOrEmpty(shaderSource))
        {
            return "<source unavailable>";
        }

        string[] lines = shaderSource.Split('\n');

        int startLine = Math.Max(0, errorLine - 2);
        int endLine = Math.Min(lines.Length - 1, errorLine + 1);

        var sb = new StringBuilder();
        for (int i = startLine; i <= endLine; i++)
        {
            int displayLine = i + 1;
            string lineText = i < lines.Length ? lines[i].TrimEnd('\r') : "";

            if (i == errorLine - 1)
            {
                sb.Append(displayLine.ToString("D4", CultureInfo.InvariantCulture));
                sb.Append(" | ");
                sb.AppendLine(lineText);
                int caretPos = Math.Min(errorColumn - 1, lineText.Length);
                sb.Append(new string(' ', 6 + caretPos));
                sb.AppendLine("^");
            }
            else
            {
                sb.Append(displayLine.ToString("D4", CultureInfo.InvariantCulture));
                sb.Append(" | ");
                sb.AppendLine(lineText);
            }
        }

        return sb.ToString();
    }
}

public static class NagaInProcValidator
{
    [System.Diagnostics.Conditional("DEBUG")]
#pragma warning disable ET0105 // DEBUG-only helper - non-determinism is acceptable
    public static void Validate(string wgslContent, string shaderName)
    {
        string tempDir = System.IO.Path.GetTempPath();
        string tempFileName = "etch_shader_" + shaderName + "_" + System.Environment.TickCount + ".wgsl";
        string tempPath = System.IO.Path.Combine(tempDir, tempFileName);

        System.IO.File.WriteAllText(tempPath, wgslContent);
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "naga",
                Arguments = "\"" + tempPath + "\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null)
            {
                return;
            }

            process.WaitForExit(5000);
            if (process.ExitCode != 0)
            {
                string error = process.StandardError.ReadToEnd();
                EtchException ex = ShaderErrorParser.Parse(error, shaderName, tempPath, wgslContent).ToEtchException();
                throw ex;
            }
        }
        finally
        {
            CleanupTempFile(tempPath);
        }
    }
#pragma warning restore ET0105

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Etch.Rules", "ET0105")]
    private static void CleanupTempFile(string path)
    {
        try
        {
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
        }
        catch (System.IO.IOException)
        {
        }
    }
}