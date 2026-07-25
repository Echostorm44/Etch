using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Etch.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
#pragma warning disable RS1025
public sealed class NoRawWgpuHandleEscapeAnalyzer : DiagnosticAnalyzer
#pragma warning restore RS1025
{
    public static readonly DiagnosticDescriptor Descriptor = new(
        "ET0201",
        "Raw wgpu handle exposed in public API",
        "Type '{0}' is a raw wgpu handle from Etch.Gpu.Native and must not escape as a return type or parameter of a public API outside Etch.Gpu. Use the ergonomic wrapper types from Etch.Gpu instead.",
        "Etch.Rules",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Descriptor);

    private static readonly ImmutableHashSet<string> WgpuNativeNamespaceTypes = ImmutableHashSet.Create<string>(
        System.StringComparer.Ordinal,
        "InstanceHandle",
        "AdapterHandle",
        "DeviceHandle",
        "QueueHandle",
        "ShaderModuleHandle",
        "BufferHandle",
        "TextureHandle",
        "TextureViewHandle",
        "SamplerHandle",
        "BindGroupHandle",
        "BindGroupLayoutHandle",
        "PipelineLayoutHandle",
        "RenderPipelineHandle",
        "CommandEncoderHandle",
        "CommandBufferHandle",
        "RenderPassEncoderHandle"
    );

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(static compilationContext =>
        {
            INamedTypeSymbol? wgpuNativeType = compilationContext.Compilation.GetTypeByMetadataName("Etch.Gpu.Native.WebGPU");
            if (wgpuNativeType is null)
            {
                return;
            }

            INamespaceSymbol wgpuNativeNs = wgpuNativeType.ContainingNamespace;
            if (wgpuNativeNs is null)
            {
                return;
            }

            compilationContext.RegisterSyntaxNodeAction(
                nodeContext =>
                {
                    var symbol = nodeContext.SemanticModel.GetSymbolInfo(nodeContext.Node, nodeContext.CancellationToken).Symbol;
                    if (symbol is not INamedTypeSymbol typeSymbol)
                    {
                        return;
                    }

                    string typeName = typeSymbol.Name;
                    if (!WgpuNativeNamespaceTypes.Contains(typeName))
                    {
                        return;
                    }

                    string filePath = nodeContext.Node.SyntaxTree.FilePath ?? string.Empty;
                    if (!IsProductionSourcePath(filePath))
                    {
                        return;
                    }

                    string assemblyName = compilationContext.Compilation.Assembly.Name ?? "";
                    bool isEtchGpuAssembly = assemblyName == "Etch.Gpu";
                    bool isEtchGpuNativeAssembly = assemblyName == "Etch.Gpu.Native";
                    bool isInternalAnalyzerAssembly = assemblyName.StartsWith("Etch.Analyzers", System.StringComparison.Ordinal);

                    if (isEtchGpuAssembly || isEtchGpuNativeAssembly || isInternalAnalyzerAssembly)
                    {
                        return;
                    }

                    nodeContext.ReportDiagnostic(Diagnostic.Create(
                        Descriptor,
                        nodeContext.Node.GetLocation(),
                        typeName));
                },
                SyntaxKind.IdentifierName);
        });
    }

    private static bool IsProductionSourcePath(string filePath)
    {
        string normalized = "/" + filePath.Replace('\\', '/').TrimStart('/');

        if (normalized.Contains("/src-gen/", System.StringComparison.Ordinal))
        {
            return false;
        }
        if (normalized.Contains("/tests/", System.StringComparison.Ordinal))
        {
            return false;
        }
        if (normalized.Contains("/tools/", System.StringComparison.Ordinal))
        {
            return false;
        }
        return normalized.Contains("/src/", System.StringComparison.Ordinal);
    }
}