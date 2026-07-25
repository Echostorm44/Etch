using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Etch.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
#pragma warning disable RS1025
public sealed class NoStringConcatShaderAnalyzer : DiagnosticAnalyzer
#pragma warning restore RS1025
{
    public static readonly DiagnosticDescriptor Descriptor = new(
        "ET0601",
        "Do not build WGSL shader source via string concatenation",
        "WGSL shader source must come from a typed accessor (Etch.Shaders.<Name>) or a string literal. String concatenation, interpolation, StringBuilder, or string.Format produce runtime allocations and bypass the build-time validation gate. Use Shaders.<Name> or a string literal instead.",
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
            string? assemblyName = compilationContext.Compilation.AssemblyName;
            bool isProductionEtchAssembly =
                assemblyName is not null
                && assemblyName.StartsWith("Etch.", System.StringComparison.Ordinal)
                && !assemblyName.EndsWith(".Tests", System.StringComparison.Ordinal)
                && !assemblyName.StartsWith("Etch.Analyzers", System.StringComparison.Ordinal);

            if (!isProductionEtchAssembly)
            {
                return;
            }

            compilationContext.RegisterSyntaxNodeAction(
                nodeContext =>
                {
                    if (nodeContext.Node is not InvocationExpressionSyntax invocation)
                    {
                        return;
                    }

                    string filePath = nodeContext.Node.SyntaxTree.FilePath ?? string.Empty;
                    if (!IsProductionSourcePath(filePath))
                    {
                        return;
                    }

                    if (!IsShaderModuleCall(invocation, nodeContext.SemanticModel, nodeContext.CancellationToken))
                    {
                        return;
                    }

                    if (invocation.ArgumentList.Arguments.Count == 0)
                    {
                        return;
                    }

                    ExpressionSyntax firstArg = invocation.ArgumentList.Arguments[0].Expression;

                    if (firstArg is InterpolatedStringExpressionSyntax)
                    {
                        nodeContext.ReportDiagnostic(Diagnostic.Create(
                            Descriptor,
                            firstArg.GetLocation()));
                        return;
                    }

                    if (firstArg is BinaryExpressionSyntax binary && binary.OperatorToken.IsKind(SyntaxKind.PlusToken))
                    {
                        nodeContext.ReportDiagnostic(Diagnostic.Create(
                            Descriptor,
                            firstArg.GetLocation()));
                        return;
                    }

                    if (firstArg is ObjectCreationExpressionSyntax objCreation)
                    {
                        string typeName = objCreation.Type.ToString();
                        if (typeName == "StringBuilder" || typeName == "System.Text.StringBuilder")
                        {
                            nodeContext.ReportDiagnostic(Diagnostic.Create(
                                Descriptor,
                                firstArg.GetLocation()));
                            return;
                        }
                    }

                    if (firstArg is InvocationExpressionSyntax invocationExpr)
                    {
                        string name = GetMethodName(invocationExpr);
                        if (name == "Concat" || name == "Format" || name == "Join")
                        {
                            nodeContext.ReportDiagnostic(Diagnostic.Create(
                                Descriptor,
                                firstArg.GetLocation()));
                            return;
                        }

                        if (name == "ToString" && IsStringBuilderChain(invocationExpr.Expression))
                        {
                            nodeContext.ReportDiagnostic(Diagnostic.Create(
                                Descriptor,
                                firstArg.GetLocation()));
                            return;
                        }
                    }
                },
                SyntaxKind.InvocationExpression);
        });
    }

    private static bool IsShaderModuleCall(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return false;
        }

        string methodName = memberAccess.Name.Identifier.Text;
        if (methodName != "CreateShaderModule" && methodName != "CreateShaderModuleWgsl")
        {
            return false;
        }

        return true;
    }

    private static string GetMethodName(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax member)
        {
            return member.Name.Identifier.Text;
        }
        if (invocation.Expression is IdentifierNameSyntax identifier)
        {
            return identifier.Identifier.Text;
        }
        return string.Empty;
    }

    private static bool IsStringBuilderChain(ExpressionSyntax? expr)
    {
        if (expr is null)
            return false;

        if (expr is ObjectCreationExpressionSyntax objCreation)
        {
            string typeName = objCreation.Type.ToString();
            return typeName == "StringBuilder" || typeName == "System.Text.StringBuilder";
        }

        if (expr is IdentifierNameSyntax identifier)
        {
            string name = identifier.Identifier.Text;
            if (name == "sb" || name.EndsWith("Builder", System.StringComparison.Ordinal))
            {
                return true;
            }
        }

        if (expr is InvocationExpressionSyntax invocation)
        {
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                string methodName = memberAccess.Name.Identifier.Text;
                if (methodName == "Append" || methodName == "Insert" || methodName == "Remove" ||
                    methodName == "Replace" || methodName == "AppendLine" || methodName == "Clear" ||
                    methodName == "ToString")
                {
                    return IsStringBuilderChain(memberAccess.Expression);
                }
            }
        }

        return false;
    }

    private static bool IsProductionSourcePath(string filePath)
    {
        string normalized = "/" + filePath.Replace('\\', '/').TrimStart('/');

        if (normalized.Contains("/src-gen/", System.StringComparison.Ordinal))
            return false;
        if (normalized.Contains("/tests/", System.StringComparison.Ordinal))
            return false;
        if (normalized.Contains("/tools/", System.StringComparison.Ordinal))
            return false;
        return normalized.Contains("/src/", System.StringComparison.Ordinal);
    }
}