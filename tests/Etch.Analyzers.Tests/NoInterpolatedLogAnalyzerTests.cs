using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Etch.Analyzers.Tests;

/// <summary>
/// Verifies ET0106 (NoInterpolatedLogAnalyzer). The rule must fire when an
/// interpolated string is passed as the template argument to IEtchLogger.Log in
/// production code, and must stay silent for non-Etch assemblies, test code, and
/// legitimate non-interpolated string calls.
/// </summary>
internal sealed class NoInterpolatedLogAnalyzerTests
{
    private const string InterpolatedLogSource = """
        using System;
        namespace Etch.Sample;
        internal static class Probe
        {
            public static void Do(IEtchLogger logger, int n)
            {
                logger.Log(EtchLogLevel.Info, 1, $"frame {n}", []);
            }
        }
        """;

    private const string LiteralLogSource = """
        using System;
        namespace Etch.Sample;
        internal static class Probe
        {
            public static void Do(IEtchLogger logger, int n)
            {
                logger.Log(EtchLogLevel.Info, 1, "frame " + n, []);
            }
        }
        """;

    private const string InterpolatedLogNamedArgSource = """
        using System;
        namespace Etch.Sample;
        internal static class Probe
        {
            public static void Do(IEtchLogger logger, int n)
            {
                logger.Log(EtchLogLevel.Info, 1, messageTemplate: $"frame {n}");
            }
        }
        """;

    [Test]
    public async Task FlagsInterpolatedStringInEtchProductionCode()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoInterpolatedLogAnalyzer(),
            InterpolatedLogSource,
            assemblyName: "Etch.Abstractions",
            filePath: "src/Etch.Abstractions/Probe.cs");

        bool found = diagnostics.Any(d => d.Id == "ET0106");
        await Assert.That(found).IsTrue();
    }

    [Test]
    public async Task FlagsInterpolatedStringWithNamedTemplateArgument()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoInterpolatedLogAnalyzer(),
            InterpolatedLogNamedArgSource,
            assemblyName: "Etch.Abstractions",
            filePath: "src/Etch.Abstractions/Probe.cs");

        bool found = diagnostics.Any(d => d.Id == "ET0106");
        await Assert.That(found).IsTrue();
    }

    [Test]
    public async Task AllowsLiteralStringConcatenation()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoInterpolatedLogAnalyzer(),
            LiteralLogSource,
            assemblyName: "Etch.Abstractions",
            filePath: "src/Etch.Abstractions/Probe.cs");

        bool anyEt0106 = diagnostics.Any(d => d.Id == "ET0106");
        await Assert.That(anyEt0106).IsFalse();
    }

    [Test]
    public async Task IgnoresInterpolatedLogInNonEtchAssembly()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoInterpolatedLogAnalyzer(),
            InterpolatedLogSource,
            assemblyName: "ConsumerApp",
            filePath: "src/ConsumerApp/Probe.cs");

        bool anyEt0106 = diagnostics.Any(d => d.Id == "ET0106");
        await Assert.That(anyEt0106).IsFalse();
    }

    [Test]
    public async Task IgnoresInterpolatedLogInTestAssembly()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoInterpolatedLogAnalyzer(),
            InterpolatedLogSource,
            assemblyName: "Etch.Abstractions.Tests",
            filePath: "tests/Etch.Abstractions.Tests/Probe.cs");

        bool anyEt0106 = diagnostics.Any(d => d.Id == "ET0106");
        await Assert.That(anyEt0106).IsFalse();
    }

    [Test]
    public async Task IgnoresInterpolatedLogInSrcGen()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoInterpolatedLogAnalyzer(),
            InterpolatedLogSource,
            assemblyName: "Etch.Abstractions",
            filePath: "src-gen/Etch.Abstractions/Probe.g.cs");

        bool anyEt0106 = diagnostics.Any(d => d.Id == "ET0106");
        await Assert.That(anyEt0106).IsFalse();
    }
}
