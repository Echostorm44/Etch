using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Etch.Analyzers.Tests;

/// <summary>
/// Verifies ET0105 (NoNonDeterministicApi). The rule must fire on banned APIs
/// inside assemblies whose name begins with <c>Etch.</c>, and must stay silent
/// everywhere else (tests, tools, downstream consumer code, etc.).
/// </summary>
internal sealed class NoNonDeterministicApiAnalyzerTests
{
    // Source files under test follow the shape the ET0105 analyzer expects: they compile
    // standalone, use a banned symbol, and make that symbol the identifier the analyzer sees.
    // We deliberately expose the banned call via a static field so the `IdentifierNameSyntax`
    // visit fires on the bare symbol name.

    private const string DateTimeNowSource = """
        using System;
        namespace Sample;
        internal static class Probe
        {
            public static DateTime Snapshot = DateTime.Now;
        }
        """;

    private const string GuidNewGuidSource = """
        using System;
        namespace Sample;
        internal static class Probe
        {
            public static Guid Make() => Guid.NewGuid();
        }
        """;

    private const string BenignSource = """
        namespace Sample;
        internal static class Probe
        {
            public static int Value = 42;
        }
        """;

    [Test]
    public async Task FlagsDateTimeNowInsideEtchAssembly()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoNonDeterministicApiAnalyzer(),
            DateTimeNowSource,
            assemblyName: "Etch.Abstractions",
            filePath: "src/Etch.Abstractions/Probe.cs");

        bool found = diagnostics.Any(d => d.Id == "ET0105");
        await Assert.That(found).IsTrue();
    }

    [Test]
    public async Task FlagsGuidNewGuidInsideEtchAssembly()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoNonDeterministicApiAnalyzer(),
            GuidNewGuidSource,
            assemblyName: "Etch.Primitives",
            filePath: "src/Etch.Primitives/Probe.cs");

        bool found = diagnostics.Any(d => d.Id == "ET0105");
        await Assert.That(found).IsTrue();
    }

    [Test]
    public async Task IgnoresDateTimeNowInNonEtchAssembly()
    {
        // A downstream consumer — e.g. someone's app that references Etch — must
        // not have their own DateTime.Now flagged.
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoNonDeterministicApiAnalyzer(),
            DateTimeNowSource,
            assemblyName: "ConsumerApp",
            filePath: "src/ConsumerApp/Probe.cs");

        bool found = diagnostics.Any(d => d.Id == "ET0105");
        await Assert.That(found).IsFalse();
    }

    [Test]
    public async Task IgnoresDateTimeNowInTestAssembly()
    {
        // Test assemblies and tooling still need DateTime.Now for, e.g., timing setup.
        // The analyzer scopes itself to Etch.* production assemblies only.
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoNonDeterministicApiAnalyzer(),
            DateTimeNowSource,
            assemblyName: "SomeCompany.Tests",
            filePath: "tests/SomeCompany.Tests/Probe.cs");

        bool found = diagnostics.Any(d => d.Id == "ET0105");
        await Assert.That(found).IsFalse();
    }

    [Test]
    public async Task ProducesNoDiagnosticOnBenignSource()
    {
        // Sanity: the analyzer does not spuriously flag innocent code.
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoNonDeterministicApiAnalyzer(),
            BenignSource,
            assemblyName: "Etch.Abstractions",
            filePath: "src/Etch.Abstractions/Probe.cs");

        bool anyEt0105 = diagnostics.Any(d => d.Id == "ET0105");
        await Assert.That(anyEt0105).IsFalse();
    }
}
