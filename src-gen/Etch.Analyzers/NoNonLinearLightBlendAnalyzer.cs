using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Etch.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoNonLinearLightBlendAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "ET0901";
    private const string Title = "Do not blend sRGB channels in linear-light context";
    private const string MessageFormat = "Arithmetic on sRGB-typed channel '{0}' in linear-light context produces incorrect results; blend in linear-light space";
    private const string Description = "Blend arithmetic in Etch.ClipBlendGradient must use linear-light values. D-012 mandates linear-light compositing.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Etch.Rules",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: "https://github.com/echostorm/Etch/blob/trunk/docs/00-overview/design-decisions.md#d-012");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(static compilationContext =>
        {
            bool isClipBlendGradientAssembly = compilationContext.Compilation.AssemblyName is not null
                && compilationContext.Compilation.AssemblyName.StartsWith("Etch.ClipBlendGradient", StringComparison.Ordinal);

            if (!isClipBlendGradientAssembly)
            {
                return;
            }

            compilationContext.RegisterSyntaxNodeAction(
                nodeContext =>
                {
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

                    string? byteVarName = GetByteArrayName(binary.Left, model, cancellationToken)
                        ?? GetByteArrayName(binary.Right, model, cancellationToken);

                    if (byteVarName is not null)
                    {
                        nodeContext.ReportDiagnostic(Diagnostic.Create(
                            Rule,
                            binary.GetLocation(),
                            byteVarName));
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

    private static string? GetByteArrayName(ExpressionSyntax expression, SemanticModel model, CancellationToken cancellationToken)
    {
        switch (expression)
        {
            case IdentifierNameSyntax identifier:
                {
                    var symbol = model.GetSymbolInfo(identifier, cancellationToken).Symbol;
                    if (symbol is ILocalSymbol local)
                    {
                        if (local.Type is IArrayTypeSymbol arrayType && arrayType.ElementType.Name == "Byte")
                        {
                            return identifier.Identifier.Text;
                        }
                    }
                    break;
                }
            case ElementAccessExpressionSyntax elementAccess:
                {
                    var type = model.GetTypeInfo(elementAccess.Expression, cancellationToken).Type;
                    if (type is IArrayTypeSymbol arrayType && arrayType.ElementType.Name == "Byte")
                    {
                        return elementAccess.Expression.GetText().ToString();
                    }
                    break;
                }
        }

        return null;
    }
}
