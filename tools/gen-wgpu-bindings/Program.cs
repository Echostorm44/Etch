using System;
using System.IO;
using System.Threading.Tasks;

namespace Etch.BindingsGen;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
        {
            PrintUsage();
            return 0;
        }

        if (args[0] != "generate")
        {
            Console.Error.WriteLine($"Error: Unknown command '{args[0]}'");
            PrintUsage();
            return 1;
        }

        string outputDir = "src/Etch.Gpu.Native/Generated";
        string ns = "Etch.Gpu.Native";
        string className = "WebGPU";
        string headerDir = "native/wgpu-native/include";

        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--output" && i + 1 < args.Length)
                outputDir = args[++i];
            else if (args[i] == "--namespace" && i + 1 < args.Length)
                ns = args[++i];
            else if (args[i] == "--class" && i + 1 < args.Length)
                className = args[++i];
            else if (args[i] == "--header-dir" && i + 1 < args.Length)
                headerDir = args[++i];
        }

        return GenerateBindingsAsync(outputDir, ns, className, headerDir).GetAwaiter().GetResult();
    }

    private static void PrintUsage()
    {
        Console.WriteLine(@"
Etch.BindingsGen - Generate C# P/Invoke bindings from wgpu-native C headers

Usage:
  etch-bindingsgen generate [options]

Options:
  --output <dir>       Output directory for generated files (default: src/Etch.Gpu.Native/Generated)
  --namespace <ns>    C# namespace for generated code (default: Etch.Gpu.Native)
  --class <name>      Class name for generated methods (default: WebGPU)
  --header-dir <dir>  Directory containing C headers (default: native/wgpu-native/include)

Examples:
  etch-bindingsgen generate
  etch-bindingsgen generate --output src/Etch.Gpu.Native/Generated
");
    }

    private static async Task<int> GenerateBindingsAsync(string outputDir, string ns, string className, string headerDir)
    {
        try
        {
            string repoRoot = FindRepoRoot(Directory.GetCurrentDirectory());
            string headerPath = Path.Combine(repoRoot, headerDir);
            string outputPath = Path.Combine(repoRoot, outputDir);

            if (!Directory.Exists(headerPath))
            {
                Console.Error.WriteLine($"Error: Header directory not found: {headerPath}");
                return 1;
            }

            Directory.CreateDirectory(outputPath);

            string webgpuH = Path.Combine(headerPath, "webgpu.h");
            string wgpuH = Path.Combine(headerPath, "wgpu.h");

            if (!File.Exists(webgpuH))
            {
                Console.Error.WriteLine($"Error: webgpu.h not found at {webgpuH}");
                return 1;
            }

            if (!File.Exists(wgpuH))
            {
                Console.Error.WriteLine($"Error: wgpu.h not found at {wgpuH}");
                return 1;
            }

            Console.WriteLine($"Generating bindings from:");
            Console.WriteLine($"  {webgpuH}");
            Console.WriteLine($"  {wgpuH}");
            Console.WriteLine($"Output: {outputPath}");

            var generator = new BindingsGenerator(outputPath);
            await generator.GenerateAsync();

            Console.WriteLine("Bindings generated successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error generating bindings: {ex.Message}");
            return 1;
        }
    }

    private static string FindRepoRoot(string startDir)
    {
        var dir = startDir;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "Etch.sln")))
                return dir;
            var parent = Directory.GetParent(dir);
            dir = parent?.FullName;
        }
        return startDir;
    }
}