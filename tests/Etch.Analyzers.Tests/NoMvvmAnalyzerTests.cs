using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Etch.Analyzers.Tests;

/// <summary>
/// Verifies ET0104 (NoMvvm). Bans INotifyPropertyChanged, CommunityToolkit.Mvvm usings,
/// and the related event-arg types inside <c>src/</c>.
/// </summary>
internal sealed class NoMvvmAnalyzerTests
{
    private const string INotifyPropertyChangedSource = """
        using System.ComponentModel;
        namespace Sample;
        internal sealed class ViewModel : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler? PropertyChanged;
        }
        """;

    private const string CommunityToolkitUsingSource = """
        using CommunityToolkit.Mvvm.ComponentModel;
        namespace Sample;
        internal static class Probe { public static int Value = 1; }
        """;

    [Test]
    public async Task FlagsINotifyPropertyChangedInsideSrc()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoMvvmAnalyzer(),
            INotifyPropertyChangedSource,
            assemblyName: "Etch.Sample",
            filePath: "/repo/src/Etch.Sample/ViewModel.cs");

        bool found = diagnostics.Any(d => d.Id == "ET0104");
        await Assert.That(found).IsTrue();
    }

    [Test]
    public async Task FlagsCommunityToolkitUsingInsideSrc()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoMvvmAnalyzer(),
            CommunityToolkitUsingSource,
            assemblyName: "Etch.Sample",
            filePath: "/repo/src/Etch.Sample/Probe.cs");

        bool found = diagnostics.Any(d => d.Id == "ET0104");
        await Assert.That(found).IsTrue();
    }

    [Test]
    public async Task IgnoresINotifyPropertyChangedInTestsFolder()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoMvvmAnalyzer(),
            INotifyPropertyChangedSource,
            assemblyName: "Etch.Sample.Tests",
            filePath: "/repo/tests/Etch.Sample.Tests/ViewModel.cs");

        bool found = diagnostics.Any(d => d.Id == "ET0104");
        await Assert.That(found).IsFalse();
    }
}
