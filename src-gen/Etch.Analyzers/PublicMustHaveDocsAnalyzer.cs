using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Etch.Analyzers;

/// <summary>
/// ET1501 — every public API in a shipping assembly must have an XML documentation comment.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PublicMustHaveDocsAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "ET1501";
    private const string Title = "Public API must have XML documentation";
    private const string MessageFormat = "Public type or member '{0}' is missing XML documentation";
    private const string Description = "All public APIs in shipping assemblies must have a <summary> XML doc comment.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Etch.Rules",
        DiagnosticSeverity.Hidden,
        isEnabledByDefault: true,
        description: Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

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
                return;

            compilationContext.RegisterSyntaxTreeAction(static treeContext =>
            {
                string filePath = treeContext.Tree.FilePath ?? string.Empty;
                if (!IsProductionSourcePath(filePath))
                    return;

                var root = treeContext.Tree.GetRoot(treeContext.CancellationToken);
                foreach (var node in root.DescendantNodes())
                {
                    if (node is not MemberDeclarationSyntax member)
                        continue;

                    if (!IsPubliclyAccessible(member))
                        continue;

                    if (HasXmlDocTrivia(member))
                        continue;

                    string name = GetNodeName(node);
                    treeContext.ReportDiagnostic(Diagnostic.Create(Rule, node.GetLocation(), name));
                }
            });
        });
    }

    private static bool IsProductionSourcePath(string filePath)
    {
        bool inSrc = (filePath.Contains("/src/", StringComparison.Ordinal) || filePath.Contains("\\src\\", StringComparison.Ordinal)) ||
                     filePath.Contains("\\src\\", StringComparison.Ordinal);
        bool inTests = filePath.Contains("/tests/", StringComparison.Ordinal) ||
                       filePath.Contains("\\tests\\", StringComparison.Ordinal);
        bool inTools = filePath.Contains("/tools/", StringComparison.Ordinal) ||
                       filePath.Contains("\\tools\\", StringComparison.Ordinal);
        bool inSamples = filePath.Contains("/samples/", StringComparison.Ordinal) ||
                         filePath.Contains("\\samples\\", StringComparison.Ordinal);
        bool inSrcGen = filePath.Contains("/src-gen/", StringComparison.Ordinal) ||
                        filePath.Contains("\\src-gen\\", StringComparison.Ordinal);

        return inSrc && !inTests && !inTools && !inSamples && !inSrcGen;
    }

    private static bool IsPubliclyAccessible(MemberDeclarationSyntax member)
    {
        var modifiers = member switch
        {
            BaseTypeDeclarationSyntax t => t.Modifiers,
            MethodDeclarationSyntax m => m.Modifiers,
            PropertyDeclarationSyntax p => p.Modifiers,
            IndexerDeclarationSyntax i => i.Modifiers,
            EventDeclarationSyntax e => e.Modifiers,
            EventFieldDeclarationSyntax ef => ef.Modifiers,
            FieldDeclarationSyntax f => f.Modifiers,
            ConstructorDeclarationSyntax c => c.Modifiers,
            DelegateDeclarationSyntax d => d.Modifiers,
            _ => default,
        };

        if (modifiers.Count == 0)
            return false;

        bool isPublic = modifiers.Any(SyntaxKind.PublicKeyword);
        bool isProtected = modifiers.Any(SyntaxKind.ProtectedKeyword);

        if (!isPublic && !isProtected)
            return false;

        if (member is FieldDeclarationSyntax fieldDecl && fieldDecl.Parent is EnumDeclarationSyntax)
            return false;

        return true;
    }

    private static bool HasXmlDocTrivia(SyntaxNode node)
    {
        foreach (var trivia in node.GetLeadingTrivia())
        {
            if (trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
            {
                string text = trivia.ToFullString();
                if (text.Contains("<summary>", StringComparison.Ordinal))
                    return true;
            }
        }
        return false;
    }

    private static string GetNodeName(SyntaxNode node)
    {
        return node switch
        {
            BaseTypeDeclarationSyntax t => t.Identifier.Text,
            MethodDeclarationSyntax m => m.Identifier.Text,
            PropertyDeclarationSyntax p => p.Identifier.Text,
            IndexerDeclarationSyntax => "this[]",
            EventDeclarationSyntax e => e.Identifier.Text,
            EventFieldDeclarationSyntax ef => ef.Declaration.Variables.First().Identifier.Text,
            FieldDeclarationSyntax f => f.Declaration.Variables.First().Identifier.Text,
            ConstructorDeclarationSyntax c => c.Identifier.Text,
            DelegateDeclarationSyntax d => d.Identifier.Text,
            _ => node.Kind().ToString(),
        };
    }
}
