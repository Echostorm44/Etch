using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Etch.Analyzers.Tests;

/// <summary>
/// In-memory Roslyn harness for exercising Etch analyzers against synthetic source.
/// Keeps the entire analyzer test surface on net10.0 with only Microsoft.CodeAnalysis.CSharp,
/// avoiding Microsoft.CodeAnalysis.Testing (which has no net10.0 target as of this writing).
/// </summary>
internal static class AnalyzerHarness
{
    /// <summary>
    /// Metadata references to the runtime BCL — required so symbol resolution works
    /// (e.g. <c>System.DateTime</c> resolves to a real symbol, not an error).
    /// Built once; the analyzer rules are assembly-scoped, so this cache is safe.
    /// </summary>
    private static readonly ImmutableArray<MetadataReference> BclReferences = BuildBclReferences();

    /// <summary>
    /// Compiles <paramref name="source"/> in-memory with the given <paramref name="assemblyName"/>
    /// and <paramref name="filePath"/>, then runs <paramref name="analyzer"/> and returns every
    /// diagnostic the analyzer produced (compiler-emitted diagnostics are filtered out).
    /// </summary>
    /// <remarks>
    /// <paramref name="filePath"/> matters because several analyzers key their "is this
    /// production code?" check off the syntax tree's file path (contains <c>/src/</c> vs
    /// <c>/tests/</c>). <paramref name="assemblyName"/> matters for ET0105, which restricts
    /// itself to assemblies whose name begins with <c>Etch.</c>.
    /// </remarks>
    public static ImmutableArray<Diagnostic> Run(
        DiagnosticAnalyzer analyzer,
        string source,
        string assemblyName,
        string filePath)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, path: filePath);

        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: assemblyName,
            syntaxTrees: new[] { tree },
            references: BclReferences,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        CompilationWithAnalyzers withAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create(analyzer));

        // GetAnalyzerDiagnosticsAsync returns only diagnostics produced by the supplied
        // analyzers — we aren't testing the C# compiler. If a synthetic fixture ever needs
        // to debug why an analyzer stayed silent, switch to GetAllDiagnosticsAsync locally
        // to surface compiler errors that might be blocking symbol resolution.
        return withAnalyzers.GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
    }

    private static ImmutableArray<MetadataReference> BuildBclReferences()
    {
        // TPA (Trusted Platform Assemblies) lists every runtime assembly loaded into the
        // current AppDomain — the simplest reliable way to cover System.Runtime,
        // System.Private.CoreLib, and friends without hard-coding paths. Always set for
        // a normal .NET host.
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is not string tpa || tpa.Length == 0)
        {
            throw new InvalidOperationException(
                "TRUSTED_PLATFORM_ASSEMBLIES is unset; the analyzer harness cannot build BCL references.");
        }

        char separator = tpa.Contains(';', StringComparison.Ordinal) ? ';' : ':';
        return tpa.Split(separator, StringSplitOptions.RemoveEmptyEntries)
            .Where(static path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToImmutableArray();
    }
}
