using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Etch.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoGammaBlendingAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "ET0701";
    private const string Title = "Do not blend sRGB channels directly";
    private const string MessageFormat = "Arithmetic on sRGB-typed channel '{0}' produces incorrect results; convert to linear-light first";
    private const string Description = "sRGB color channels must be converted to linear-light before blending. D-012 mandates linear-light compositing.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Etch.Rules",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: "https://github.com/echostorm/Etch/blob/trunk/docs/00-overview/design-decisions.md#d-012");

    private static readonly DiagnosticDescriptor GammaSafeRule = new(
        "ET0701b",
        Title,
        MessageFormat,
        "Etch.Rules",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: false,
        description: "[GammaSafe] call site that may still be incorrect");

    private static readonly Regex SrgbChannelPattern = new(
        @"(srgb|gamma|colorChannel|channel)[a-zA-Z]*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule, GammaSafeRule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(static compilationContext =>
        {
            bool isRasterCpuAssembly = compilationContext.Compilation.AssemblyName is not null
                && (compilationContext.Compilation.AssemblyName == "Etch.Raster.Cpu"
                    || compilationContext.Compilation.AssemblyName.StartsWith("Etch.Raster.", StringComparison.Ordinal));

            INamedTypeSymbol? gammaSafeAttribute = null;
            if (isRasterCpuAssembly)
            {
                gammaSafeAttribute = compilationContext.Compilation.GetTypeByMetadataName("Etch.Raster.Cpu.GammaSafeAttribute");
            }

            compilationContext.RegisterSyntaxNodeAction(
                nodeContext =>
                {
                    if (!isRasterCpuAssembly)
                    {
                        return;
                    }

                    var expression = nodeContext.Node;
                    var model = nodeContext.SemanticModel;
                    var cancellationToken = nodeContext.CancellationToken;

                    if (expression is not BinaryExpressionSyntax binary)
                    {
                        return;
                    }

                    if (!IsBlendOperator(binary.OperatorToken.Kind()))
                    {
                        return;
                    }

                    if (IsGammaSafe(binary, model, gammaSafeAttribute, cancellationToken))
                    {
                        return;
                    }

                    string? srgbVarName = GetSrgbChannelName(binary.Left, model, cancellationToken)
                        ?? GetSrgbChannelName(binary.Right, model, cancellationToken);

                    if (srgbVarName is not null)
                    {
                        nodeContext.ReportDiagnostic(Diagnostic.Create(
                            Rule,
                            binary.GetLocation(),
                            srgbVarName));
                    }
                },
                SyntaxKind.AddExpression,
                SyntaxKind.SubtractExpression,
                SyntaxKind.MultiplyExpression,
                SyntaxKind.DivideExpression);
        });
    }

    private static bool IsBlendOperator(SyntaxKind kind)
    {
        return kind is SyntaxKind.AddExpression
            or SyntaxKind.SubtractExpression
            or SyntaxKind.MultiplyExpression
            or SyntaxKind.DivideExpression;
    }

    private static bool IsGammaSafe(ExpressionSyntax expression, SemanticModel model, INamedTypeSymbol? gammaSafeAttribute, CancellationToken cancellationToken)
    {
        if (gammaSafeAttribute is null)
        {
            return false;
        }

        var symbol = model.GetSymbolInfo(expression, cancellationToken).Symbol;
        if (symbol is null)
        {
            return false;
        }

        return symbol.GetAttributes().Any(a =>
            a.AttributeClass?.Equals(gammaSafeAttribute, SymbolEqualityComparer.Default) == true);
    }

    private static string? GetSrgbChannelName(ExpressionSyntax expression, SemanticModel model, CancellationToken cancellationToken)
    {
        switch (expression)
        {
            case IdentifierNameSyntax identifier:
                {
                    var symbol = model.GetSymbolInfo(identifier, cancellationToken).Symbol;
                    if (symbol is ILocalSymbol local && SrgbChannelPattern.IsMatch(local.Name))
                    {
                        return local.Name;
                    }
                    if (symbol is IParameterSymbol param && SrgbChannelPattern.IsMatch(param.Name))
                    {
                        return param.Name;
                    }
                    var type = model.GetTypeInfo(identifier, cancellationToken).Type;
                    if (type is not null && type.Name is "Byte" or "byte")
                    {
                        return identifier.Identifier.Text;
                    }
                    break;
                }
            case ElementAccessExpressionSyntax elementAccess:
                {
                    var type = model.GetTypeInfo(elementAccess, cancellationToken).Type;
                    if (type is not null && type.Name is "Byte" or "byte")
                    {
                        return elementAccess.Expression.GetText().ToString();
                    }
                    break;
                }
        }

        return null;
    }
}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field)]
public sealed class GammaSafeAttribute : Attribute
{
}