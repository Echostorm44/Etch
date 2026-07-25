using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Etch.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoBoxingSceneCommandAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "ET0401";
    private const string Title = "No boxing of SceneCommand";
    private const string MessageFormat = "API returns IEnumerable<SceneCommand> which boxes the readonly struct";
    private const string Description = "SceneCommand is a readonly struct. Returning it via IEnumerable<T> or ICollection<T> causes boxing. Use ReadOnlySpan<T> or a custom enumerator instead.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Etch.Rules",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterSymbolAction(static symbolContext =>
        {
            var methodSymbol = symbolContext.Symbol as IMethodSymbol;
            if (methodSymbol == null)
                return;

            if (!IsInProductionCode(methodSymbol))
                return;

            var returnType = methodSymbol.ReturnType;
            if (returnType == null)
                return;

            if (IsBoxingCollection(returnType, "SceneCommand"))
            {
                var location = methodSymbol.Locations.FirstOrDefault(l => l.IsInSource);
                if (location != null)
                {
                    symbolContext.ReportDiagnostic(Diagnostic.Create(Rule, location, methodSymbol.Name));
                }
            }
        }, SymbolKind.Method);
    }

    private static bool IsInProductionCode(ISymbol symbol)
    {
        var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        if (location == null)
            return false;

        var filePath = location.SourceTree?.FilePath ?? string.Empty;
        return filePath.Contains("/src/", StringComparison.Ordinal) &&
               !filePath.Contains("/tests/", StringComparison.Ordinal) &&
               !filePath.Contains("\\tests\\", StringComparison.Ordinal) &&
               !filePath.Contains("/src-gen/", StringComparison.Ordinal) &&
               !filePath.Contains("\\src-gen\\", StringComparison.Ordinal) &&
               !filePath.Contains("/tools/", StringComparison.Ordinal) &&
               !filePath.Contains("\\tools\\", StringComparison.Ordinal);
    }

    private static bool IsBoxingCollection(ITypeSymbol type, string typeName)
    {
        if (type is INamedTypeSymbol namedType && namedType.IsGenericType)
        {
            var name = namedType.ConstructedFrom.Name;
            if (name == "IEnumerable" || name == "ICollection" || name == "IList" || name == "IReadOnlyList" || name == "IReadOnlyCollection")
            {
                var args = namedType.TypeArguments;
                if (args.Length == 1 && args[0].Name == typeName)
                    return true;
            }
        }
        return false;
    }
}
