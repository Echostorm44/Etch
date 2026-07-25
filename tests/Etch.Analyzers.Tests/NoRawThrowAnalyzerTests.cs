using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Etch.Analyzers.Tests;

/// <summary>
/// Verifies ET0108 (NoRawThrowAnalyzer). The rule must fire on <c>throw new X(...)</c>
/// inside <c>Etch.*</c> production assemblies when <c>X</c> is not allow-listed, and
/// must stay silent on allow-listed types, non-Etch assemblies, and non-production
/// source paths (<c>tests/</c>, <c>src-gen/</c>, <c>tools/</c>).
/// </summary>
internal sealed class NoRawThrowAnalyzerTests
{
    private const string InvalidOperationSource = """
        using System;
        namespace Etch.Sample;
        internal static class Probe
        {
            public static void Do()
            {
                throw new InvalidOperationException("boom");
            }
        }
        """;

    private const string EtchExceptionSource = """
        using System;
        namespace Etch;
        public sealed class EtchException : Exception
        {
            public EtchException(string message) : base(message) { }
        }
        namespace Etch.Sample
        {
            internal static class Probe
            {
                public static void Do()
                {
                    throw new EtchException("allowed — this is how Panic funnels throws.");
                }
            }
        }
        """;

    private const string OperationCanceledSource = """
        using System;
        namespace Etch.Sample;
        internal static class Probe
        {
            public static void Do()
            {
                throw new OperationCanceledException();
            }
        }
        """;

    private const string ObjectDisposedSource = """
        using System;
        namespace Etch.Sample;
        internal static class Probe
        {
            public static void Do()
            {
                throw new ObjectDisposedException("thing");
            }
        }
        """;

    private const string RethrowSource = """
        using System;
        namespace Etch.Sample;
        internal static class Probe
        {
            public static void Do()
            {
                try { } catch (Exception) { throw; }
            }
        }
        """;

    [Test]
    public async Task FlagsRawInvalidOperationExceptionInEtchProductionCode()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoRawThrowAnalyzer(),
            InvalidOperationSource,
            assemblyName: "Etch.Abstractions",
            filePath: "src/Etch.Abstractions/Probe.cs");

        bool found = diagnostics.Any(d => d.Id == "ET0108");
        await Assert.That(found).IsTrue();
    }

    [Test]
    public async Task AllowsThrowOfEtchException()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoRawThrowAnalyzer(),
            EtchExceptionSource,
            assemblyName: "Etch.Abstractions",
            filePath: "src/Etch.Abstractions/Probe.cs");

        bool anyEt0108 = diagnostics.Any(d => d.Id == "ET0108");
        await Assert.That(anyEt0108).IsFalse();
    }

    [Test]
    public async Task AllowsThrowOfOperationCanceledException()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoRawThrowAnalyzer(),
            OperationCanceledSource,
            assemblyName: "Etch.Abstractions",
            filePath: "src/Etch.Abstractions/Probe.cs");

        bool anyEt0108 = diagnostics.Any(d => d.Id == "ET0108");
        await Assert.That(anyEt0108).IsFalse();
    }

    [Test]
    public async Task AllowsThrowOfObjectDisposedException()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoRawThrowAnalyzer(),
            ObjectDisposedSource,
            assemblyName: "Etch.Abstractions",
            filePath: "src/Etch.Abstractions/Probe.cs");

        bool anyEt0108 = diagnostics.Any(d => d.Id == "ET0108");
        await Assert.That(anyEt0108).IsFalse();
    }

    [Test]
    public async Task AllowsBareRethrow()
    {
        // `throw;` preserves the already-funnelled exception and never constructs a new one.
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoRawThrowAnalyzer(),
            RethrowSource,
            assemblyName: "Etch.Abstractions",
            filePath: "src/Etch.Abstractions/Probe.cs");

        bool anyEt0108 = diagnostics.Any(d => d.Id == "ET0108");
        await Assert.That(anyEt0108).IsFalse();
    }

    [Test]
    public async Task IgnoresRawThrowInNonEtchAssembly()
    {
        // Consumer apps must be free to throw whatever they like.
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoRawThrowAnalyzer(),
            InvalidOperationSource,
            assemblyName: "ConsumerApp",
            filePath: "src/ConsumerApp/Probe.cs");

        bool anyEt0108 = diagnostics.Any(d => d.Id == "ET0108");
        await Assert.That(anyEt0108).IsFalse();
    }

    [Test]
    public async Task IgnoresRawThrowInTestAssembly()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoRawThrowAnalyzer(),
            InvalidOperationSource,
            assemblyName: "Etch.Abstractions.Tests",
            filePath: "tests/Etch.Abstractions.Tests/Probe.cs");

        bool anyEt0108 = diagnostics.Any(d => d.Id == "ET0108");
        await Assert.That(anyEt0108).IsFalse();
    }

    [Test]
    public async Task IgnoresRawThrowInSrcGenPath()
    {
        // Source-generator-emitted wrapper code runs under the production assembly name
        // but sits under /src-gen/ by convention and is exempt.
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoRawThrowAnalyzer(),
            InvalidOperationSource,
            assemblyName: "Etch.Abstractions",
            filePath: "src-gen/Etch.Abstractions/Probe.g.cs");

        bool anyEt0108 = diagnostics.Any(d => d.Id == "ET0108");
        await Assert.That(anyEt0108).IsFalse();
    }
}
