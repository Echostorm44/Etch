using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Etch.Analyzers.Tests;

/// <summary>
/// Verifies ET0101 (NoReflection). The rule scopes by file path — it fires inside
/// <c>src/</c> but stays silent inside <c>tests/</c>, <c>src-gen/</c>, and <c>tools/</c>.
/// </summary>
internal sealed class NoReflectionAnalyzerTests
{
    private const string ReflectionUsingSource = """
        using System.Reflection;
        namespace Sample;
        internal static class Probe { public static int Value = 1; }
        """;

    private const string ActivatorSource = """
        using System;
        namespace Sample;
        internal static class Probe
        {
            public static object? Make() => Activator.CreateInstance(typeof(Probe));
        }
        """;

    [Test]
    public async Task FlagsReflectionUsingInsideSrc()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoReflectionAnalyzer(),
            ReflectionUsingSource,
            assemblyName: "Etch.Sample",
            filePath: "/repo/src/Etch.Sample/Probe.cs");

        bool found = diagnostics.Any(d => d.Id == "ET0101");
        await Assert.That(found).IsTrue();
    }

    [Test]
    public async Task FlagsActivatorCreateInstanceInsideSrc()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoReflectionAnalyzer(),
            ActivatorSource,
            assemblyName: "Etch.Sample",
            filePath: "/repo/src/Etch.Sample/Probe.cs");

        bool found = diagnostics.Any(d => d.Id == "ET0101");
        await Assert.That(found).IsTrue();
    }

    [Test]
    public async Task IgnoresReflectionInTestsFolder()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoReflectionAnalyzer(),
            ReflectionUsingSource,
            assemblyName: "Etch.Sample.Tests",
            filePath: "/repo/tests/Etch.Sample.Tests/Probe.cs");

        bool found = diagnostics.Any(d => d.Id == "ET0101");
        await Assert.That(found).IsFalse();
    }

    [Test]
    public async Task IgnoresReflectionInSrcGenFolder()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoReflectionAnalyzer(),
            ReflectionUsingSource,
            assemblyName: "Etch.Analyzers",
            filePath: "/repo/src-gen/Etch.Analyzers/Probe.cs");

        bool found = diagnostics.Any(d => d.Id == "ET0101");
        await Assert.That(found).IsFalse();
    }
}
