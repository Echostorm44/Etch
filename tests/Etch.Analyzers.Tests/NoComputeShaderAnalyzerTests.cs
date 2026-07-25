using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Etch.Analyzers.Tests;

internal sealed class NoComputeShaderAnalyzerTests
{
    private const string CreateComputePipelineSource = """
        namespace Etch.Gpu;
        internal static class DeviceExtensions
        {
            public static object CreateComputePipeline(this object device) => null!;
        }
        """;

    private const string CreateComputePipelineInCompositor = """
        namespace Etch.Gpu.Compositor;
        internal static class DeviceExtensions
        {
            public static object CreateComputePipeline(this object device) => null!;
        }
        """;

    [Test]
    public async Task FlagsCreateComputePipelineInsideEtchGpu()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoComputeShaderAnalyzer(),
            CreateComputePipelineSource,
            assemblyName: "Etch.Gpu",
            filePath: "/repo/src/Etch.Gpu/MyFile.cs");

        bool found = diagnostics.Any(d => d.Id == "ET0801");
        await Assert.That(found).IsTrue();
    }

    [Test]
    public async Task FlagsCreateComputePipelineInsideEtchGpuCompositor()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoComputeShaderAnalyzer(),
            CreateComputePipelineInCompositor,
            assemblyName: "Etch.Gpu.Compositor",
            filePath: "/repo/src/Etch.Gpu.Compositor/MyFile.cs");

        bool found = diagnostics.Any(d => d.Id == "ET0801");
        await Assert.That(found).IsTrue();
    }

    [Test]
    public async Task IgnoresCreateComputePipelineInOtherNamespace()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoComputeShaderAnalyzer(),
            CreateComputePipelineSource,
            assemblyName: "Etch.Other",
            filePath: "/repo/src/Etch.Other/MyFile.cs");

        bool found = diagnostics.Any(d => d.Id == "ET0801");
        await Assert.That(found).IsFalse();
    }

    [Test]
    public async Task IgnoresCreateComputePipelineInTestsFolder()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoComputeShaderAnalyzer(),
            CreateComputePipelineSource,
            assemblyName: "Etch.Gpu.Tests",
            filePath: "/repo/tests/Etch.Gpu.Tests/MyFile.cs");

        bool found = diagnostics.Any(d => d.Id == "ET0801");
        await Assert.That(found).IsFalse();
    }
}