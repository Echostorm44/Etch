using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Etch.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoReflectionAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "ET0101";
    private const string Title = "No reflection allowed";
    private const string MessageFormat = "Reflection API '{0}' is not permitted in Etch production code";
    private const string Description = "Reflection violates the no-reflection design rule";

    private static readonly ImmutableArray<string> ForbiddenPrefixes = ImmutableArray.Create(
        "System.Reflection",
        "System.Reflection.Emit");

    private static readonly ImmutableHashSet<string> ForbiddenMethods = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "Activator.CreateInstance",
        "Activator.CreateInstanceAsync",
        "Type.GetType",
        "Type.GetTypeFromHandle");

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Etch.Rules",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: "https://github.com/echostorm/Etch/blob/trunk/docs/00-overview/design-decisions.md#d-004");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterSyntaxTreeAction(static treeContext =>
        {
            if (!IsProductionTree(treeContext.Tree.FilePath))
            {
                return;
            }

            var root = treeContext.Tree.GetRoot(treeContext.CancellationToken);

            foreach (var usingDirective in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
            {
                var name = usingDirective.Name?.ToString() ?? string.Empty;
                if (ForbiddenPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal) || name == prefix))
                {
                    treeContext.ReportDiagnostic(Diagnostic.Create(Rule, usingDirective.GetLocation(), name));
                }
            }

            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var methodName = GetFullyQualifiedMethodName(invocation);
                if (methodName != null && ForbiddenMethods.Contains(methodName))
                {
                    treeContext.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation(), methodName));
                }
            }
        });
    }

    private static bool IsProductionTree(string filePath)
    {
        return !filePath.Contains("/tests/", StringComparison.Ordinal) &&
               !filePath.Contains("\\tests\\", StringComparison.Ordinal) &&
               !filePath.Contains("/src-gen/", StringComparison.Ordinal) &&
               !filePath.Contains("\\src-gen\\", StringComparison.Ordinal) &&
               !filePath.Contains("/tools/", StringComparison.Ordinal) &&
               !filePath.Contains("\\tools\\", StringComparison.Ordinal) &&
               (filePath.Contains("/src/", StringComparison.Ordinal) || filePath.Contains("\\src\\", StringComparison.Ordinal));
    }

    private static string? GetFullyQualifiedMethodName(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var name = memberAccess.Name.Identifier.Text;
            var expr = memberAccess.Expression;
            while (expr is MemberAccessExpressionSyntax nested)
            {
                name = nested.Name.Identifier.Text + "." + name;
                expr = nested.Expression;
            }
            if (expr is IdentifierNameSyntax identifier)
            {
                return identifier.Identifier.Text + "." + name;
            }
            return name;
        }
        return null;
    }
}