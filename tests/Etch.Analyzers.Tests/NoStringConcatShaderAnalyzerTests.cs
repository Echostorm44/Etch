using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Etch.Analyzers.Tests;

internal sealed class NoStringConcatShaderAnalyzerTests
{
    private const string StringConcatShaderSource = """
        using System;
        namespace Etch.Gpu;
        internal static class Probe
        {
            public static void Do(Device device)
            {
                device.CreateShaderModule("struct X { }" + " ");
            }
        }
        """;

    private const string StringFormatShaderSource = """
        using System;
        namespace Etch.Gpu;
        internal static class Probe
        {
            public static void Do(Device device)
            {
                device.CreateShaderModule(string.Format("struct {0} {{ }}", "X"));
            }
        }
        """;

    private const string LiteralShaderSource = """
        using System;
        namespace Etch.Gpu;
        internal static class Probe
        {
            public static void Do(Device device)
            {
                device.CreateShaderModule("struct X { }");
            }
        }
        """;

    private const string AccessorShaderSource = """
        using System;
        namespace Etch.Gpu;
        internal static class Probe
        {
            public static void Do(Device device)
            {
                device.CreateShaderModule(Shaders.test);
            }
        }
        """;

    [Test]
    public async Task FlagsStringConcatInShaderModuleCall()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoStringConcatShaderAnalyzer(),
            StringConcatShaderSource,
            assemblyName: "Etch.Gpu",
            filePath: "src/Etch.Gpu/Probe.cs");

        bool found = diagnostics.Any(d => d.Id == "ET0601");
        await Assert.That(found).IsTrue();
    }

    [Test]
    public async Task FlagsStringFormatInShaderModuleCall()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoStringConcatShaderAnalyzer(),
            StringFormatShaderSource,
            assemblyName: "Etch.Gpu",
            filePath: "src/Etch.Gpu/Probe.cs");

        bool found = diagnostics.Any(d => d.Id == "ET0601");
        await Assert.That(found).IsTrue();
    }

    [Test]
    public async Task AllowsLiteralStringInShaderModuleCall()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoStringConcatShaderAnalyzer(),
            LiteralShaderSource,
            assemblyName: "Etch.Gpu",
            filePath: "src/Etch.Gpu/Probe.cs");

        bool anyET0601 = diagnostics.Any(d => d.Id == "ET0601");
        await Assert.That(anyET0601).IsFalse();
    }

    [Test]
    public async Task FlagsStringBuilderChainInShaderModuleCall()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoStringConcatShaderAnalyzer(),
            """
            using System;
            using System.Text;
            namespace Etch.Gpu;
            internal static class Probe
            {
                public static void Do(Device device)
                {
                    device.CreateShaderModule(new StringBuilder("struct X { ").Append("}").ToString());
                }
            }
            """,
            assemblyName: "Etch.Gpu",
            filePath: "src/Etch.Gpu/Probe.cs");

        bool found = diagnostics.Any(d => d.Id == "ET0601");
        await Assert.That(found).IsTrue();
    }

    [Test]
    public async Task AllowsShadersAccessorInShaderModuleCall()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoStringConcatShaderAnalyzer(),
            AccessorShaderSource,
            assemblyName: "Etch.Gpu",
            filePath: "src/Etch.Gpu/Probe.cs");

        bool anyET0601 = diagnostics.Any(d => d.Id == "ET0601");
        await Assert.That(anyET0601).IsFalse();
    }

    [Test]
    public async Task IgnoresStringConcatInNonEtchAssembly()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoStringConcatShaderAnalyzer(),
            StringConcatShaderSource,
            assemblyName: "ConsumerApp",
            filePath: "src/ConsumerApp/Probe.cs");

        bool anyET0601 = diagnostics.Any(d => d.Id == "ET0601");
        await Assert.That(anyET0601).IsFalse();
    }

    [Test]
    public async Task IgnoresStringConcatInTestAssembly()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoStringConcatShaderAnalyzer(),
            StringConcatShaderSource,
            assemblyName: "Etch.Gpu.Tests",
            filePath: "tests/Etch.Gpu.Tests/Probe.cs");

        bool anyET0601 = diagnostics.Any(d => d.Id == "ET0601");
        await Assert.That(anyET0601).IsFalse();
    }

    [Test]
    public async Task IgnoresStringConcatInSrcGen()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzerHarness.Run(
            new NoStringConcatShaderAnalyzer(),
            StringConcatShaderSource,
            assemblyName: "Etch.Gpu",
            filePath: "src-gen/Etch.Gpu/Probe.g.cs");

        bool anyET0601 = diagnostics.Any(d => d.Id == "ET0601");
        await Assert.That(anyET0601).IsFalse();
    }
}