using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Etch.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoSingleImplInterfaceAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "ET0107";
    private const string Title = "No single-implementer interface";
    private const string MessageFormat = "Interface '{0}' has exactly one implementer and no [EtchExtensionPoint] attribute";
    private const string Description = "Interfaces should have multiple implementations or be marked as extension points";

    private static readonly ImmutableHashSet<string> WhitelistedInterfaces = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "IFrameClock",
        "IEntropySource",
        "ITileScheduler",
        "IShaderSource",
        "IFileSystem");

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

        context.RegisterSymbolAction(static symbolContext =>
        {
            var interfaceSymbol = symbolContext.Symbol as INamedTypeSymbol;
            if (interfaceSymbol?.TypeKind != TypeKind.Interface)
            {
                return;
            }

            if (!interfaceSymbol.Name.StartsWith('I'))
            {
                return;
            }

            if (!IsInProductionCode(interfaceSymbol))
            {
                return;
            }

            if (WhitelistedInterfaces.Contains(interfaceSymbol.Name))
            {
                return;
            }

            if (interfaceSymbol.GetAttributes().Any(a => a.AttributeClass?.Name == "EtchExtensionPointAttribute"))
            {
                return;
            }

            var implementingTypes = GetImplementingTypes(symbolContext.Compilation, interfaceSymbol);
            if (implementingTypes.Count == 1)
            {
                var location = interfaceSymbol.Locations.FirstOrDefault(l => l.IsInSource);
                if (location != null)
                {
                    symbolContext.ReportDiagnostic(Diagnostic.Create(Rule, location, interfaceSymbol.Name));
                }
            }
        }, SymbolKind.NamedType);
    }

    private static bool IsInProductionCode(INamedTypeSymbol symbol)
    {
        var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        if (location == null)
        {
            return false;
        }

        var filePath = location.SourceTree?.FilePath ?? string.Empty;
        return (filePath.Contains("/src/", StringComparison.Ordinal) || filePath.Contains("\\src\\", StringComparison.Ordinal)) &&
               !filePath.Contains("/tests/", StringComparison.Ordinal) &&
               !filePath.Contains("\\tests\\", StringComparison.Ordinal) &&
               !filePath.Contains("/src-gen/", StringComparison.Ordinal) &&
               !filePath.Contains("\\src-gen\\", StringComparison.Ordinal) &&
               !filePath.Contains("/tools/", StringComparison.Ordinal) &&
               !filePath.Contains("\\tools\\", StringComparison.Ordinal);
    }

    private static ImmutableHashSet<INamedTypeSymbol> GetImplementingTypes(Compilation compilation, INamedTypeSymbol interfaceSymbol)
    {
        var builder = ImmutableHashSet.CreateBuilder<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        var globalNamespace = compilation.GlobalNamespace;
        var stack = new Stack<INamespaceSymbol>();
        stack.Push(globalNamespace);

        while (stack.Count > 0)
        {
            var ns = stack.Pop();
            foreach (var member in ns.GetMembers())
            {
                if (member is INamedTypeSymbol namedType)
                {
                    if (namedType.TypeKind == TypeKind.Class || namedType.TypeKind == TypeKind.Struct)
                    {
                        foreach (var iface in namedType.AllInterfaces)
                        {
                            if (SymbolEqualityComparer.Default.Equals(iface, interfaceSymbol))
                            {
                                builder.Add(namedType);
                                break;
                            }
                        }
                    }
                }
                else if (member is INamespaceSymbol childNs)
                {
                    stack.Push(childNs);
                }
            }
        }

        return builder.ToImmutable();
    }
}