using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Etch;

/// <summary>
/// Static entry points that throw <see cref="EtchException"/> with a captured call site.
/// Every internal Etch failure path funnels through one of these methods so that logs,
/// crash dumps, and the verifier see a consistent shape.
/// </summary>
/// <remarks>
/// <para>
/// All methods are <see cref="DoesNotReturnAttribute"/>-annotated. The C# compiler uses
/// this to treat code after a <c>Panic.*</c> call as unreachable, so a method that ends
/// with <c>Panic.Invariant(...)</c> and no <c>return</c> statement still type-checks.
/// </para>
/// <para>
/// The <c>file</c> and <c>line</c> parameters are populated by the compiler via
/// <see cref="CallerFilePathAttribute"/> and <see cref="CallerLineNumberAttribute"/>; callers
/// must not pass them explicitly.
/// </para>
/// </remarks>
public static class Panic
{
    /// <summary>
    /// Raises a generic invariant violation with the supplied <paramref name="code"/> and
    /// <paramref name="message"/>. Captures the caller's file path and line number into
    /// <see cref="EtchException.CallSite"/>.
    /// </summary>
    [DoesNotReturn]
    public static void Invariant(
        PanicCode code,
        string message,
        [CallerFilePath] string? file = null,
        [CallerLineNumber] int line = 0)
    {
        throw new EtchException(code, message, callSite: FormatCallSite(file, line));
    }

    /// <summary>Raises <see cref="PanicCodes.ArgumentNull"/> naming <paramref name="paramName"/>.</summary>
    [DoesNotReturn]
    public static void ArgumentNull(
        string paramName,
        [CallerFilePath] string? file = null,
        [CallerLineNumber] int line = 0)
    {
        throw new EtchException(
            PanicCodes.ArgumentNull,
            $"{paramName} must not be null.",
            callSite: FormatCallSite(file, line));
    }

    /// <summary>
    /// Raises <see cref="PanicCodes.ArgumentOutOfRange"/> naming <paramref name="paramName"/>
    /// and optionally including an extra <paramref name="message"/> explaining the valid range.
    /// </summary>
    [DoesNotReturn]
    public static void ArgumentOutOfRange(
        string paramName,
        string? message = null,
        [CallerFilePath] string? file = null,
        [CallerLineNumber] int line = 0)
    {
        string fullMessage = message is null
            ? $"{paramName} is out of range."
            : $"{paramName} is out of range: {message}";
        throw new EtchException(
            PanicCodes.ArgumentOutOfRange,
            fullMessage,
            callSite: FormatCallSite(file, line));
    }

    /// <summary>
    /// Raises <see cref="PanicCodes.NotImplemented"/> naming the <paramref name="feature"/>
    /// that was invoked but is not wired up on this build or platform.
    /// </summary>
    [DoesNotReturn]
    public static void NotImplemented(
        string feature,
        [CallerFilePath] string? file = null,
        [CallerLineNumber] int line = 0)
    {
        throw new EtchException(
            PanicCodes.NotImplemented,
            $"{feature} is not implemented.",
            callSite: FormatCallSite(file, line));
    }

    // Keep call-site formatting in one place so log-scraping tooling can rely on the
    // exact shape: "<path>:<line>". Separator chosen to match IDE "go to" conventions.
    private static string? FormatCallSite(string? file, int line)
    {
        return file is null ? null : file + ":" + line.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
