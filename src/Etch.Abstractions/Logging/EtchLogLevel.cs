namespace Etch;

/// <summary>
/// Log event severity levels. Errors at <see cref="Error"/> and above are routed through
/// <see cref="Panic"/> rather than <see cref="IEtchLogger"/> — panic codes carry the
/// machine-readable incident key that logs and crash dumps can correlate on.
/// </summary>
public enum EtchLogLevel
{
    Trace = 0,
    Debug = 1,
    Info = 2,
    Warn = 3,
    Error = 4,
}
