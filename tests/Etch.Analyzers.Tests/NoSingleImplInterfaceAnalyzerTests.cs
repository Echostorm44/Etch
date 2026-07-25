using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Etch.Analyzers.Tests;

/// <summary>
/// Verifies ET0107 (NoSingleImplInterface). An interface with exactly one implementer
/// is flagged inside <c>src/</c> unless it is on the whitelist (the five determinism
/// seams) or marked with <c>[EtchExtensionPoint]</c>.
/// </summary>
internal sealed class NoSingleImplInterfaceAnalyzerTests
{
    private const string SingleImplSource = """
        namespace Sample;
        internal interface IWidget { void Poke(); }
        internal sealed class RealWidget : IWidget { public void Poke() { } }
        """;

    private const string MultipleImplSource = """
        namespace Sample;
        internal interface IWidget { void Poke(); }
        internal sealed class RealWidget : IWidget { public void Poke() { } }
        internal sealed class OtherWidget : IWidget { public void Poke() { } }
        """;

    private const string WhitelistedSingleImplSource = """
        namespace Sample;
        internal interface IFrameClock { long NowNanos(); }
        internal sealed class DefaultFrameClock : IFrameClock { public long NowNanos() => 0; }
        """;

    [Test]
    public async Task FlagsSingleImplInterfaceInsideSrc()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoSingleImplInterfaceAnalyzer(),
            SingleImplSource,
            assemblyName: "Etch.Sample",
            filePath: "/repo/src/Etch.Sample/Widget.cs");

        bool found = diagnostics.Any(d => d.Id == "ET0107");
        await Assert.That(found).IsTrue();
    }

    [Test]
    public async Task IgnoresMultipleImplInterfaceInsideSrc()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoSingleImplInterfaceAnalyzer(),
            MultipleImplSource,
            assemblyName: "Etch.Sample",
            filePath: "/repo/src/Etch.Sample/Widget.cs");

        bool found = diagnostics.Any(d => d.Id == "ET0107");
        await Assert.That(found).IsFalse();
    }

    [Test]
    public async Task IgnoresWhitelistedSeamEvenWithSingleImpl()
    {
        // IFrameClock, IEntropySource, ITileScheduler, IShaderSource, IFileSystem
        // are the five sanctioned D-005 seams — they always have one production impl
        // plus a test fake (still only two, but the whitelist protects them regardless).
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoSingleImplInterfaceAnalyzer(),
            WhitelistedSingleImplSource,
            assemblyName: "Etch.Abstractions",
            filePath: "/repo/src/Etch.Abstractions/Determinism/IFrameClock.cs");

        bool found = diagnostics.Any(d => d.Id == "ET0107");
        await Assert.That(found).IsFalse();
    }

    [Test]
    public async Task IgnoresSingleImplInTestsFolder()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoSingleImplInterfaceAnalyzer(),
            SingleImplSource,
            assemblyName: "Etch.Sample.Tests",
            filePath: "/repo/tests/Etch.Sample.Tests/Widget.cs");

        bool found = diagnostics.Any(d => d.Id == "ET0107");
        await Assert.That(found).IsFalse();
    }
}
