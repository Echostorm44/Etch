using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Etch.Analyzers;

/// <summary>
/// ET0108 — forbids raw <c>throw new &lt;Exception-derived&gt;(...)</c> inside the Etch
/// production assemblies (<c>Etch.*</c> under <c>src/</c>). D-006 mandates that every
/// internal failure path funnels through <c>Panic.Invariant</c> / <c>Panic.ArgumentNull</c>
/// / etc., which in turn construct <c>EtchException</c> with a stable <c>ET-P-####</c>
/// panic code. Raw throws slip past that contract and produce incident reports with no
/// machine-readable code, so the rule is an error, not a warning.
/// </summary>
/// <remarks>
/// <para><b>Scope.</b> Only syntax trees whose file path contains a <c>/src/</c> (or
/// <c>\src\</c>) segment. Files under <c>/src-gen/</c>, <c>/tests/</c>, and <c>/tools/</c>
/// are exempt: generators produce wrapper code, tests exercise the real BCL, and tooling
/// lives outside the production contract.</para>
/// <para><b>Allow-listed throw targets.</b></para>
/// <list type="bullet">
/// <item><description><c>EtchException</c> itself — the <c>Panic</c> helpers construct it directly.</description></item>
/// <item><description><c>OperationCanceledException</c> — cooperative cancellation signal; framework convention.</description></item>
/// <item><description><c>ObjectDisposedException</c> — framework convention for post-dispose use.</description></item>
/// </list>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
#pragma warning disable RS1025 // Configure generated code analysis — not relevant; analyzer is syntax-only and has no generated-file shortcut.
public sealed class NoRawThrowAnalyzer : DiagnosticAnalyzer
#pragma warning restore RS1025
{
    public static readonly DiagnosticDescriptor Descriptor = new(
        "ET0108",
        "Do not raw-throw in Etch production code",
        "Raw 'throw new {0}(...)' is forbidden in Etch production code; route through Panic.Invariant/ArgumentNull/... so a stable ET-P-#### code is attached.",
        "Etch.Rules",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Descriptor);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(static compilationContext =>
        {
            // Assembly-scope gate: the rule only applies to first-party production assemblies
            // (Etch.Abstractions, Etch.Primitives, Etch.Gpu, …). Consumer apps that reference
            // Etch must be free to throw whatever they like; tests likewise. We capture the
            // decision once per compilation and close over it so the per-node callback stays
            // branchless on the hot path.
            string? assemblyName = compilationContext.Compilation.AssemblyName;
            bool isProductionEtchAssembly =
                assemblyName is not null
                && assemblyName.StartsWith("Etch.", System.StringComparison.Ordinal)
                && !assemblyName.EndsWith(".Tests", System.StringComparison.Ordinal)
                && !assemblyName.StartsWith("Etch.Analyzers", System.StringComparison.Ordinal);

            compilationContext.RegisterSyntaxNodeAction(
                nodeContext =>
                {
                    if (!isProductionEtchAssembly)
                    {
                        return;
                    }

                    // Both "throw new Foo()" statements and "throw new Foo()" expressions
                    // parse to ThrowStatementSyntax / ThrowExpressionSyntax respectively,
                    // each wrapping an ObjectCreationExpressionSyntax. We handle both.
                    ExpressionSyntax? thrown = nodeContext.Node switch
                    {
                        ThrowStatementSyntax stmt => stmt.Expression,
                        ThrowExpressionSyntax expr => expr.Expression,
                        _ => null,
                    };

                    if (thrown is not ObjectCreationExpressionSyntax creation)
                    {
                        // Re-throws (`throw;`) and `throw someVariable;` are deliberately
                        // allowed — they preserve an existing exception chain that has
                        // already passed through the Panic funnel at some point upstream.
                        return;
                    }

                    // File-path exemption: src-gen/, tests/, tools/ are out of scope. Any
                    // production source path must include "/src/" and not "/src-gen/".
                    string filePath = nodeContext.Node.SyntaxTree.FilePath ?? string.Empty;
                    if (!IsProductionSourcePath(filePath))
                    {
                        return;
                    }

                    SymbolInfo typeSymbolInfo = nodeContext.SemanticModel.GetSymbolInfo(
                        creation.Type, nodeContext.CancellationToken);
                    ISymbol? resolved = typeSymbolInfo.Symbol ?? typeSymbolInfo.CandidateSymbols.FirstOrDefault();
                    if (resolved is not INamedTypeSymbol typeSymbol)
                    {
                        // If we can't resolve the type we don't want to issue false positives.
                        // The compiler will raise its own error on unresolved types anyway.
                        return;
                    }

                    string typeName = typeSymbol.Name;
                    if (IsAllowListed(typeName))
                    {
                        return;
                    }

                    nodeContext.ReportDiagnostic(Diagnostic.Create(
                        Descriptor,
                        creation.GetLocation(),
                        typeName));
                },
                SyntaxKind.ThrowStatement,
                SyntaxKind.ThrowExpression);
        });
    }

    private static bool IsProductionSourcePath(string filePath)
    {
        // Normalize so Windows ("\") and Unix ("/") paths both work. We bracket the
        // normalized path with separators so the containment checks work for both rooted
        // ("C:/repo/src/...") and relative ("src/...") inputs — the file path fed in by
        // Roslyn depends on how the compilation was set up.
        string normalized = "/" + filePath.Replace('\\', '/').TrimStart('/');

        if (normalized.Contains("/src-gen/", System.StringComparison.Ordinal))
        {
            return false;
        }
        if (normalized.Contains("/tests/", System.StringComparison.Ordinal))
        {
            return false;
        }
        if (normalized.Contains("/tools/", System.StringComparison.Ordinal))
        {
            return false;
        }
        return normalized.Contains("/src/", System.StringComparison.Ordinal);
    }

    private static bool IsAllowListed(string typeName)
    {
        // Bare type-name match is sufficient: the combination of an inherited System.Exception
        // ancestry plus an exact name match is vanishingly unlikely to collide with a
        // first-party Etch type that happens to share the name.
        return typeName is
            "EtchException" or
            "OperationCanceledException" or
            "ObjectDisposedException";
    }
}
