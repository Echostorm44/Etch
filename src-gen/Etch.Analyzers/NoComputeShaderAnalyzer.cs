using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Etch.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoComputeShaderAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "ET0801";
    private const string Title = "No compute shaders";
    private const string MessageFormat = "Compute shaders are not permitted in Etch GPU code";
    private const string Description = "Etch architecture is fragment-only. Compute shaders are not supported.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Etch.Rules",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: "https://github.com/echostorm/Etch/blob/trunk/docs/00-overview/non-goals.md");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterSyntaxTreeAction(static treeContext =>
        {
            if (!IsGpuCode(treeContext.Tree.FilePath))
            {
                return;
            }

            var root = treeContext.Tree.GetRoot(treeContext.CancellationToken);

            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (!IsCreateComputePipelineCall(invocation))
                {
                    continue;
                }

                treeContext.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation()));
            }
        });
    }

    // Covers every GPU project under src/: Etch.Gpu itself plus Etch.Gpu.* siblings such as
    // Etch.Gpu.Compositor and Etch.Gpu.Native. Paths are normalized so Windows separators and
    // relative paths match too.
    private static bool IsGpuCode(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return false;
        }

        string normalized = "/" + filePath.Replace('\\', '/').TrimStart('/');
        return normalized.Contains("/src/Etch.Gpu/", StringComparison.Ordinal) ||
               normalized.Contains("/src/Etch.Gpu.", StringComparison.Ordinal);
    }

    private static bool IsCreateComputePipelineCall(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return false;
        }

        if (memberAccess.Name.Identifier.Text != "CreateComputePipeline")
        {
            return false;
        }

        return true;
    }
}