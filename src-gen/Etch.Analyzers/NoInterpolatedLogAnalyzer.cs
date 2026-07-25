using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Etch.Analyzers;

/// <summary>
/// ET0106 — forbids passing an interpolated string (<c>$"..."</c>) as the
/// <c>template</c> argument to any <c>IEtchLogger.Log</c> overload, or to any
/// extension method on <c>IEtchLogger</c> that accepts a string-like handler.
/// Structured logging's zero-allocation guarantee depends on passing a literal
/// template plus a <c>stackalloc</c>ed KVP span; interpolated strings allocate
/// on every call and defeat that contract.
/// </summary>
/// <remarks>
/// <para>
/// <b>Scope.</b> Only syntax trees whose file path contains a <c>/src/</c> segment,
/// excluding <c>/src-gen/</c>, <c>/tests/</c>, <c>/tools/</c>. Applies only to
/// assemblies whose name begins with <c>Etch.</c> and is not a test assembly.
/// </para>
/// <para>
/// <b>Detection.</b> Roslyn marks an <c>$"..."</c> literal as
/// <c>InterpolatedStringExpressionSyntax</c>. We scan <c>InvocationExpressionSyntax</c>
/// nodes whose called method is named <c>Log</c> and inspect the <c>template</c>
/// positional or named argument for that type.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
#pragma warning disable RS1025
public sealed class NoInterpolatedLogAnalyzer : DiagnosticAnalyzer
#pragma warning restore RS1025
{
    public static readonly DiagnosticDescriptor Descriptor = new(
        "ET0106",
        "Do not use interpolated strings in IEtchLogger.Log calls",
        "Pass a literal template string and a stackalloc'd ReadOnlySpan<KeyValuePair<string, object?>> args instead of an interpolated string ($'...').",
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

                    // File-path gate.
                    string filePath = nodeContext.Node.SyntaxTree.FilePath ?? string.Empty;
                    if (!IsProductionSourcePath(filePath))
                    {
                        return;
                    }

                    // Called method must be named "Log" and be on IEtchLogger (either directly
                    // or via an extension method). We accept both: a direct call on an
                    // IEtchLogger-typed expression and any extension-method call whose first
                    // argument is IEtchLogger.
                    if (!IsLoggerLogCall(invocation, nodeContext.SemanticModel, nodeContext.CancellationToken))
                    {
                        return;
                    }

                    // Find the "template" argument — either the 3rd positional argument
                    // (index 2, after level and eventId) or a named "template:" argument.
                    ExpressionSyntax? templateArg = FindTemplateArgument(invocation);
                    if (templateArg is null)
                    {
                        return;
                    }

                    // An InterpolatedStringExpressionSyntax is the syntactic representation
                    // of $"frame {n}" — it always allocates and violates the zero-alloc contract.
                    if (templateArg is not InterpolatedStringExpressionSyntax)
                    {
                        return;
                    }

                    nodeContext.ReportDiagnostic(Diagnostic.Create(
                        Descriptor,
                        templateArg.GetLocation()));
                },
                SyntaxKind.InvocationExpression);
        });
    }

    private static bool IsLoggerLogCall(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        // Check both: a direct call (logger.Log(...)) and an extension call (Log(logger, ...)).
        // For direct calls, the expression being invoked is a MemberAccessExpressionSyntax.
        // For extension calls, the first argument is IEtchLogger.
        ExpressionSyntax called = invocation.Expression;

        if (called is MemberAccessExpressionSyntax memberAccess)
        {
            // Direct call: logger.Log(...)
            if (memberAccess.Name.Identifier.Text != "Log")
            {
                return false;
            }

            // Optionally verify the receiver type is IEtchLogger — but that requires a
            // namespace-resolved symbol lookup. We just check the name "Log" for now since
            // any production code calling .Log(...) on an IEtchLogger is subject to the rule.
            return true;
        }

        if (called is IdentifierNameSyntax identifier && identifier.Identifier.Text == "Log")
        {
            // Extension-method call: Log(logger, template, args)
            // The first argument must be of type IEtchLogger.
            if (invocation.ArgumentList.Arguments.Count < 1)
            {
                return false;
            }
            ArgumentSyntax firstArg = invocation.ArgumentList.Arguments[0];
            ITypeSymbol? firstArgType = semanticModel.GetTypeInfo(firstArg.Expression, cancellationToken).Type;
            if (firstArgType is INamedTypeSymbol named && named.Name == "IEtchLogger")
            {
                return true;
            }
        }

        return false;
    }

    private static ExpressionSyntax? FindTemplateArgument(InvocationExpressionSyntax invocation)
    {
        // Try named argument first: template: $"..."
        foreach (ArgumentSyntax arg in invocation.ArgumentList.Arguments)
        {
            if (arg.NameColon is NameColonSyntax { Name.Identifier.Text: "template" })
            {
                return arg.Expression;
            }
        }

        // Try positional argument at index 2 (0=level, 1=eventId, 2=template)
        SeparatedSyntaxList<ArgumentSyntax> positionalArgs = invocation.ArgumentList.Arguments;
        if (positionalArgs.Count >= 3)
        {
            // The first two arguments are level and eventId; the third is template.
            return positionalArgs[2].Expression;
        }

        return null;
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
