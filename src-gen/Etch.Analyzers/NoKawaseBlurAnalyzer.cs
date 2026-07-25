using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Etch.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoKawaseBlurAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "ET1201";
    private const string Title = "Do not use Kawase blur";
    private const string MessageFormat = "Kawase blur is not permitted; use dual-filter Bjørge blur (DualFilterBlur)";
    private const string Description = "Kawase blur is explicitly banned by D-XXX. Dual-filter Bjørge is strictly better at equivalent radius.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Etch.Rules",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(static compilationContext =>
        {
            bool isEtchEffectsAssembly = compilationContext.Compilation.AssemblyName is not null
                && (compilationContext.Compilation.AssemblyName.StartsWith("Etch.Effects", StringComparison.Ordinal)
                    || compilationContext.Compilation.AssemblyName.StartsWith("Etch.Raster.Cpu", StringComparison.Ordinal)
                    || compilationContext.Compilation.AssemblyName.StartsWith("Etch.Gpu", StringComparison.Ordinal));

            if (!isEtchEffectsAssembly)
            {
                return;
            }

            compilationContext.RegisterSyntaxNodeAction(
                nodeContext =>
                {
                    var method = nodeContext.Node as MethodDeclarationSyntax;
                    if (method is null)
                    {
                        return;
                    }

                    string methodName = method.Identifier.ValueText ?? string.Empty;
                    if (methodName.Contains("Kawase", StringComparison.OrdinalIgnoreCase))
                    {
                        nodeContext.ReportDiagnostic(Diagnostic.Create(
                            Rule,
                            method.Identifier.GetLocation()));
                    }
                },
                SyntaxKind.MethodDeclaration);
        });
    }
}
