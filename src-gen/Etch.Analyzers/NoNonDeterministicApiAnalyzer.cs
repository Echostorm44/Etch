using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Etch.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
#pragma warning disable RS1025 // Configure generated code analysis
public sealed class NoNonDeterministicApiAnalyzer : DiagnosticAnalyzer
#pragma warning restore RS1025
{
    public static readonly DiagnosticDescriptor Descriptor = new(
        "ET0105",
        "Do not use non-deterministic API",
        "'{0}' is non-deterministic and must be accessed through a determinism seam instead of directly.",
        "Etch.Determinism",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Descriptor);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static compilationContext =>
        {
            if (compilationContext.Compilation.AssemblyName is not { } assemblyName || !assemblyName.StartsWith("Etch.", System.StringComparison.Ordinal))
            {
                return;
            }

            compilationContext.RegisterSyntaxNodeAction(
                static nodeContext =>
                {
                    if (nodeContext.Node is not IdentifierNameSyntax identifierNode)
                        return;

                    // Determinism is a production render-engine concern. Scope to src/ like the
                    // other Etch analyzers (IsProductionTree) so tools/tests/generated code — which
                    // legitimately touch these APIs — are not flagged. (This analyzer filters by
                    // assembly name Etch.*, which also matches dev tools like Etch.FlakeDetector.)
                    if (!IsProductionTree(identifierNode.SyntaxTree.FilePath))
                        return;

                    var symbolInfo = nodeContext.SemanticModel.GetSymbolInfo(identifierNode, nodeContext.CancellationToken);
                    if (symbolInfo.Symbol is not ISymbol symbol)
                        return;

                    // Build "Namespace.Type.Member" for members (properties, methods, fields)
                    // and "Namespace.Type" for types. FullyQualifiedFormat alone collapses member
                    // names to their bare identifier, which is useless for rule matching.
                    string symbolDisplay = BuildLookupName(symbol);

                    if (!IsBannedApi(symbolDisplay))
                    {
                        return;
                    }

                    // Whitelist: the five seam defaults and their test-double counterparts are
                    // the ONLY types allowed to touch these APIs directly. Everyone else must
                    // route through the seam interface. We determine the enclosing type by
                    // walking up from the semantic model's enclosing symbol — cheaper than
                    // parsing the syntax tree and works uniformly whether the call site sits
                    // in a method body, a field initializer, or a property accessor.
                    ISymbol? enclosing = nodeContext.ContainingSymbol;
                    while (enclosing is not null)
                    {
                        if (enclosing is INamedTypeSymbol namedType && IsWhitelistedSeamType(namedType.Name))
                        {
                            return;
                        }
                        enclosing = enclosing.ContainingSymbol;
                    }

                    nodeContext.ReportDiagnostic(Diagnostic.Create(
                        Descriptor,
                        identifierNode.GetLocation(),
                        symbolDisplay));
                },
                SyntaxKind.IdentifierName);
        });
    }

    private static bool IsProductionTree(string filePath)
    {
        // Normalize separators so the checks work for Windows and Unix paths alike.
        string normalized = "/" + filePath.Replace('\\', '/').TrimStart('/');
        if (normalized.Contains("/src-gen/", System.StringComparison.Ordinal))
            return false;
        if (normalized.Contains("/tests/", System.StringComparison.Ordinal))
            return false;
        if (normalized.Contains("/tools/", System.StringComparison.Ordinal))
            return false;
        return normalized.Contains("/src/", System.StringComparison.Ordinal);
    }

    private static string BuildLookupName(ISymbol symbol)
    {
        // For types, fully-qualify directly. For members (property/method/field/event),
        // qualify by the containing type so we get "System.DateTime.Now" rather than "Now".
        const string GlobalPrefix = "global::";

        string fullyQualified = symbol is ITypeSymbol or INamespaceSymbol
            ? symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            : symbol.ContainingType is { } container
                ? container.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + symbol.Name
                : symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        return fullyQualified.StartsWith(GlobalPrefix, System.StringComparison.Ordinal)
            ? fullyQualified.Substring(GlobalPrefix.Length)
            : fullyQualified;
    }

    private static bool IsWhitelistedSeamType(string typeName)
    {
        // The five production defaults and their deterministic test-double counterparts.
        // These types are the single sanctioned home for each banned API — wrapping it
        // is their entire reason to exist. D-005 forbids any other type from doing so.
        return typeName is
            "DefaultFrameClock" or
            "DefaultEntropySource" or
            "SingleThreadTileScheduler" or
            "EmbeddedShaderSource" or
            "PhysicalFileSystem" or
            "FixedFrameClock" or
            "DeterministicEntropySource" or
            "DeterministicParallelTileScheduler" or
            "InMemoryShaderSource" or
            "InMemoryFileSystem";
    }

    private static bool IsBannedApi(string symbolDisplay)
    {
        return symbolDisplay is
            "System.DateTime.Now" or
            "System.DateTime.UtcNow" or
            "System.DateTimeOffset.Now" or
            "System.Diagnostics.Stopwatch.GetTimestamp" or
            "System.Random" or
            "System.Security.Cryptography.RandomNumberGenerator.GetBytes" or
            "System.Guid.NewGuid" or
            "System.Threading.Tasks.Task.Run" or
            "System.Threading.Tasks.Parallel.For" or
            "System.Threading.Tasks.Parallel.ForEach" or
            "System.Threading.ThreadPool.QueueUserWorkItem" or
            "System.IO.File.ReadAllBytes" or
            "System.IO.File.ReadAllText" or
            "System.IO.File.WriteAllBytes" or
            "System.IO.File.WriteAllText" or
            "System.IO.File.Delete" or
            "System.IO.File.Exists";
    }
}