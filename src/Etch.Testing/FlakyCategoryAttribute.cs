using System;

namespace Etch.Testing;

/// <summary>
/// Marks a test as flaky so that it is excluded from the main CI gate
/// and routed to the dedicated flaky-watch lane instead.
/// </summary>
/// <remarks>
/// A flaky test must also carry <c>[Category("Flaky")]</c> so that
/// TUnit's filter pipeline routes it correctly; this attribute adds
/// metadata (quarantine date, tracking issue) consumed by the
/// flake detector.
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
public sealed class FlakyTestAttribute : Attribute
{
    /// <summary>
    /// The date (ISO-8601) when the test was quarantined. Used by the
    /// flake detector to enforce the 7-day fix deadline.
    /// </summary>
    public string? QuarantinedSince { get; init; }

    /// <summary>
    /// Optional tracking issue URL or ID for the root-cause fix.
    /// </summary>
    public string? TrackingIssue { get; init; }
}
