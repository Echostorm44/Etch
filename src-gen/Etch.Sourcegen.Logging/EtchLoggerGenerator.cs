using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Etch.Sourcegen.Logging;

#pragma warning disable CA1031 // Initialize catches all exceptions to surface nothing during generation
#pragma warning disable CA1305 // StringBuilder.Append with string literals is culture-invariant

[Generator]
public partial class EtchLoggerGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static outputContext =>
        {
            outputContext.AddSource(
                "EtchLogAttribute.g.cs",
                SourceText.From("""
                    namespace Etch;

                    [global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = false)]
                    public sealed class EtchLogAttribute : global::System.Attribute
                    {
                        public int EventId { get; init; }
                        public EtchLogLevel Level { get; init; }
                        public string Template { get; init; } = string.Empty;
                    }
                    """, encoding: Encoding.UTF8));
        });

        IncrementalValuesProvider<MethodDeclarationSyntax> methods = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => IsLogMethod(node),
                static (syntaxContext, _) => Transform(syntaxContext))
            .Where(static m => m is not null)!;

        context.RegisterSourceOutput(methods, static (sourceContext, method) =>
        {
            if (method is null) return;
            string source = GenerateMethod(method);
            sourceContext.AddSource(
                hintName: GetHintName(method),
                sourceText: SourceText.From(source, encoding: Encoding.UTF8));
        });
    }

    private static bool IsLogMethod(SyntaxNode node)
    {
        if (node is not MethodDeclarationSyntax method)
            return false;
        if (!method.Modifiers.Any(SyntaxKind.PartialKeyword))
            return false;
        if (!method.Modifiers.Any(SyntaxKind.StaticKeyword))
            return false;
        if (method.AttributeLists.Count == 0)
            return false;
        if (method.SyntaxTree.FilePath.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private static MethodDeclarationSyntax? Transform(GeneratorSyntaxContext ctx)
    {
        MethodDeclarationSyntax method = (MethodDeclarationSyntax)ctx.Node;
        foreach (AttributeSyntax attr in method.AttributeLists.SelectMany(al => al.Attributes))
        {
            if (ctx.SemanticModel.GetSymbolInfo(attr, default).Symbol is not IMethodSymbol attrCtor)
                continue;
            INamedTypeSymbol attrType = attrCtor.ContainingType;
            if (attrType.Name != "EtchLogAttribute" || attrType.ContainingNamespace.Name != "Etch")
                continue;
            return method;
        }
        return null;
    }

    private static string GetHintName(MethodDeclarationSyntax method)
    {
        string ns = GetAncestorNamespace(method);
        string typeName = GetAncestorClassName(method);
        return $"{ns}.{typeName}.{method.Identifier.Text}.g.cs";
    }

    private static string GetAncestorNamespace(MethodDeclarationSyntax method)
    {
        SyntaxNode? current = method.Parent;
        while (current is not null)
        {
            if (current is NamespaceDeclarationSyntax ns)
                return ns.Name.ToString();
            current = current.Parent;
        }
        return "global";
    }

    private static string GetAncestorClassName(MethodDeclarationSyntax method)
    {
        SyntaxNode? current = method.Parent;
        while (current is not null)
        {
            if (current is ClassDeclarationSyntax cd)
                return cd.Identifier.Text;
            current = current.Parent;
        }
        return "Unknown";
    }

    private static string GenerateMethod(MethodDeclarationSyntax method)
    {
        string className = GetAncestorClassName(method);
        string ns = GetAncestorNamespace(method);

        int eventId = 0;
        string levelName = "Info";
        string template = "";

        foreach (AttributeSyntax attr in method.AttributeLists.SelectMany(al => al.Attributes))
        {
            foreach (AttributeArgumentSyntax arg in attr.ArgumentList?.Arguments ?? default)
            {
                string? name = arg.NameColon?.Name.Identifier.Text;
                if (name is null && arg.NameEquals is { Name: { Identifier: { Text: var eqText } } })
                    name = eqText;
                if (name is null) continue;

                if (name == "EventId" && arg.Expression is LiteralExpressionSyntax lit)
                    eventId = (int)(lit.Token.Value ?? 0);
                else if (name == "Level" && arg.Expression is MemberAccessExpressionSyntax mem)
                    levelName = mem.Name.Identifier.Text;
                else if (name == "Template" && arg.Expression is LiteralExpressionSyntax tplLit)
                    template = tplLit.Token.ValueText ?? "";
            }
        }

        var paramList = method.ParameterList.Parameters;
        int userParamCount = paramList.Count > 0 ? paramList.Count - 1 : 0;

        var sb = new StringBuilder();
        sb.AppendLine("namespace " + ns + ";");
        sb.AppendLine("");
        sb.AppendLine("partial class " + className);
        sb.AppendLine("{");

        SyntaxTriviaList leading = method.GetLeadingTrivia();
        foreach (SyntaxTrivia t in leading)
        {
            if (t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                t.IsKind(SyntaxKind.MultiLineCommentTrivia))
            {
                sb.AppendLine(t.ToString());
                break;
            }
        }

        sb.AppendLine("    [global::System.CodeDom.Compiler.GeneratedCode(\"Etch.Sourcegen.Logging\", \"1.0\")]");
        sb.Append("    public static partial void ");
        sb.Append(method.Identifier.Text);
        sb.Append("(IEtchLogger logger");
        for (int i = 1; i < paramList.Count; i++)
        {
            sb.Append(", ");
            sb.Append(paramList[i].ToString());
        }
        sb.AppendLine(")");
        sb.AppendLine("    {");
        sb.AppendLine("        if (!logger.IsEnabled(EtchLogLevel." + levelName + ")) return;");

        if (userParamCount > 0)
        {
            sb.Append("        Span<KeyValuePair<string, object?>> kvps = stackalloc KeyValuePair<string, object?>[" + userParamCount + "];");
            sb.AppendLine();
            for (int i = 0; i < userParamCount; i++)
            {
                string paramName = paramList[i + 1].Identifier.Text;
                sb.Append("        kvps[" + i + "] = new KeyValuePair<string, object?>(\"" + paramName + "\", " + paramName + ");");
                sb.AppendLine();
            }
        }
        else
        {
            sb.AppendLine("        scoped ReadOnlySpan<KeyValuePair<string, object?>> kvps = [];");
        }

        string escaped = template.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        sb.Append("        logger.Log(EtchLogLevel." + levelName + ", " + eventId + ", \"" + escaped + "\", kvps");
        sb.AppendLine(");");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }
}
