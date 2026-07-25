using System;

namespace Etch;

/// <summary>
/// Marks a static partial method as a structured-logging entry point. The
/// <c>EtchLoggerGenerator</c> source-generator emits a zero-allocation
/// implementation that checks IsEnabled, builds a stackalloc key/value span,
/// and forwards to IEtchLogger.Log.
/// </summary>
/// <remarks>
/// The attributed method must be static partial, take IEtchLogger as its first
/// parameter, and take additional parameters matching the placeholder names in
/// Template by index position. Placeholders use the {name} convention matching
/// .NET string interpolation but without the $ prefix.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class EtchLogAttribute : Attribute
{
    /// <summary>Application-defined event identifier passed through to IEtchLogger.Log.</summary>
    public int EventId { get; init; }

    /// <summary>The minimum EtchLogLevel at which this message should be emitted.</summary>
    public EtchLogLevel Level { get; init; }

    /// <summary>
    /// Message template with named placeholders, e.g.
    /// Tile {tileIndex} classified with {stripCount} strips.
    /// Placeholders map to method parameters by zero-based index position
    /// (excluding the leading IEtchLogger parameter).
    /// </summary>
    public string Template { get; init; } = string.Empty;
}
