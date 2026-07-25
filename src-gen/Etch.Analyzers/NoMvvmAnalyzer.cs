using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Etch.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoMvvmAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "ET0104";
    private const string Title = "No MVVM allowed";
    private const string MessageFormat = "MVVM type '{0}' is not permitted in Etch production code";
    private const string Description = "MVVM patterns violate the simple-object design rule";

    private static readonly ImmutableArray<string> ForbiddenPrefixes = ImmutableArray.Create(
        "CommunityToolkit.Mvvm",
        "CommunityToolkit.Mvvm.ComponentModel",
        "CommunityToolkit.Mvvm.Input",
        "Microsoft.Toolkit.Mvvm",
        "Microsoft.Toolkit.Mvvm.ComponentModel",
        "Microsoft.Toolkit.Mvvm.Input");

    private static readonly ImmutableHashSet<string> ForbiddenTypes = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "INotifyPropertyChanged",
        "INotifyPropertyChanging",
        "PropertyChangedEventHandler",
        "PropertyChangedEventArgs",
        "PropertyChangingEventHandler",
        "PropertyChangingEventArgs");

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
                if (usingDirective.Alias != null)
                {
                    continue;
                }

                var name = usingDirective.Name?.ToString() ?? string.Empty;
                if (ForbiddenPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal) || name == prefix))
                {
                    treeContext.ReportDiagnostic(Diagnostic.Create(Rule, usingDirective.GetLocation(), name));
                }
            }

            foreach (var identifier in root.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                var name = identifier.Identifier.Text;
                if (ForbiddenTypes.Contains(name))
                {
                    treeContext.ReportDiagnostic(Diagnostic.Create(Rule, identifier.GetLocation(), name));
                }
            }

            foreach (var baseType in root.DescendantNodes().OfType<BaseTypeSyntax>())
            {
                var typeName = baseType.Type.ToString();
                if (ForbiddenTypes.Contains(typeName))
                {
                    treeContext.ReportDiagnostic(Diagnostic.Create(Rule, baseType.GetLocation(), typeName));
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
               filePath.Contains("/src/", StringComparison.Ordinal);
    }
}