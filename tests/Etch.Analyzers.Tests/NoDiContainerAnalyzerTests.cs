using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Etch.Analyzers.Tests;

/// <summary>
/// Verifies ET0103 (NoDiContainer). Bans Microsoft.Extensions.DependencyInjection
/// and friends inside <c>src/</c>.
/// </summary>
internal sealed class NoDiContainerAnalyzerTests
{
    private const string DiUsingSource = """
        using Microsoft.Extensions.DependencyInjection;
        namespace Sample;
        internal static class Probe { public static int Value = 1; }
        """;

    private const string ServiceProviderReferenceSource = """
        namespace Sample;
        internal static class Probe
        {
            public static IServiceProvider? Provider;
        }
        internal interface IServiceProvider { }
        """;

    [Test]
    public async Task FlagsDiUsingInsideSrc()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoDiContainerAnalyzer(),
            DiUsingSource,
            assemblyName: "Etch.Sample",
            filePath: "/repo/src/Etch.Sample/Probe.cs");

        bool found = diagnostics.Any(d => d.Id == "ET0103");
        await Assert.That(found).IsTrue();
    }

    [Test]
    public async Task FlagsServiceProviderIdentifierInsideSrc()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoDiContainerAnalyzer(),
            ServiceProviderReferenceSource,
            assemblyName: "Etch.Sample",
            filePath: "/repo/src/Etch.Sample/Probe.cs");

        bool found = diagnostics.Any(d => d.Id == "ET0103");
        await Assert.That(found).IsTrue();
    }

    [Test]
    public async Task IgnoresDiUsingInTestsFolder()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoDiContainerAnalyzer(),
            DiUsingSource,
            assemblyName: "Etch.Sample.Tests",
            filePath: "/repo/tests/Etch.Sample.Tests/Probe.cs");

        bool found = diagnostics.Any(d => d.Id == "ET0103");
        await Assert.That(found).IsFalse();
    }
}
