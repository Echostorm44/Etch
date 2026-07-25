using System;
using System.Diagnostics.CodeAnalysis;

namespace Etch;

/// <summary>
/// The only exception type thrown by Etch. Carries a stable <see cref="PanicCode"/> and
/// an optional <see cref="CallSite"/> so logs and crash dumps can correlate an incident
/// to the exact source location that raised it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Shape contract.</b> Sealed; no serialization constructor; no reflection-dependent
/// behavior; a single inner-exception link at most (panic codes carry context; stacks do
/// the rest). The type is AOT-clean and safe to throw from trimmed / Native AOT builds.
/// </para>
/// <para>
/// <b>When to throw directly.</b> Almost never. Prefer <see cref="Panic.Invariant"/> and
/// its siblings — they are <c>[DoesNotReturn]</c>, capture caller file and line, and keep
/// the throw shape consistent. ET0108 (<c>NoRawThrowAnalyzer</c>) forbids raw
/// <c>throw new EtchException(...)</c> outside the <c>Panic</c> helpers themselves.
/// </para>
/// </remarks>
[SuppressMessage(
    "Design",
    "CA1032:Implement standard exception constructors",
    Justification =
        "D-006 and FND-010 restrict EtchException to code-bearing constructors only: every throw " +
        "must carry a stable PanicCode, with no anonymous fallback. Adding the standard ctors " +
        "would defeat the invariant.")]
public sealed class EtchException : Exception
{
    /// <summary>The stable <c>ET-P-####</c> identifier for the panic class.</summary>
    public PanicCode Code { get; }

    /// <summary>
    /// The source file and line where the panic originated, formatted as
    /// <c>&lt;path&gt;:&lt;line&gt;</c>. <see langword="null"/> when the call site was
    /// not captured (e.g. exception constructed manually by a non-<see cref="Panic"/>
    /// caller).
    /// </summary>
    public string? CallSite { get; }

    /// <summary>Creates an <see cref="EtchException"/> with the given code, message, and optional call site.</summary>
    public EtchException(PanicCode code, string message, string? callSite = null)
        : base(message)
    {
        Code = code;
        CallSite = callSite;
    }

    /// <summary>
    /// Creates an <see cref="EtchException"/> wrapping a single inner exception. Use only
    /// at the boundary where native or third-party code has already produced the inner
    /// exception — internal Etch code does not chain.
    /// </summary>
    public EtchException(PanicCode code, string message, Exception innerException, string? callSite = null)
        : base(message, innerException)
    {
        Code = code;
        CallSite = callSite;
    }
}
