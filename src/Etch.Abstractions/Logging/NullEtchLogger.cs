using System;
using System.Collections.Generic;

namespace Etch;

/// <summary>
/// Default no-op logger. <see cref="IsEnabled"/> always returns <see langword="false"/>
/// and <see cref="Log"/> does nothing. AOT-clean, zero allocation, safe to use as a
/// compilation-unit-level singleton where no logging backend is wired up yet.
/// </summary>
public sealed class NullEtchLogger : IEtchLogger
{
    /// <summary>Shared singleton instance. Safe to use wherever a <see cref="IEtchLogger"/>
    /// is required but no logging backend has been configured yet.</summary>
    public static NullEtchLogger Instance { get; } = new();

    /// <inheritdoc/>
    public bool IsEnabled(EtchLogLevel level) => false;

    /// <inheritdoc/>
    public void Log(EtchLogLevel level, int eventId, string messageTemplate, ReadOnlySpan<KeyValuePair<string, object?>> args)
    {
        // No-op by design.
    }
}
