using System;
using System.IO;
using Etch.Abstractions.Diagnostics;
using Etch.Gpu.Diagnostics;

namespace Etch.Repro;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 0;
        }

        string command = args[0].ToLowerInvariant();

        switch (command)
        {
            case "dump":
                return Dump(args);
            case "validate":
                return Validate(args);
            case "replay":
                return Replay(args);
            default:
                Console.Error.WriteLine($"Unknown command: {command}");
                PrintUsage();
                return 1;
        }
    }

    private static int Dump(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: etch-repro dump <file.etrp>");
            return 1;
        }

        string path = args[1];
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"File not found: {path}");
            return 1;
        }

        var data = File.ReadAllBytes(path);
        var reader = new SceneReproReader(data);

        if (!reader.TryReadHeader())
        {
            Console.Error.WriteLine($"Failed to read header: {reader.Result}");
            return 1;
        }

        Console.WriteLine($"Magic: ETRP");
        Console.WriteLine($"Version: {reader.Version}");
        Console.WriteLine($"Sections: {reader.SectionCount}");
        Console.WriteLine();

        int sectionIndex = 0;
        while (reader.TryReadNextSection(out var sectionId, out var payload))
        {
            Console.WriteLine($"Section {sectionIndex}: {sectionId} ({payload.Length} bytes)");
            Console.Write($"  First 16 bytes (hex): ");
            int previewLen = Math.Min(16, payload.Length);
            for (int i = 0; i < previewLen; i++)
            {
                Console.Write($"{payload[i]:X2} ");
            }
            Console.WriteLine();
            sectionIndex++;
        }

        return 0;
    }

    private static int Validate(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: etch-repro validate <file.etrp> [--validate-gpu]");
            return 1;
        }

        string path = args[1];
        bool validateGpu = false;
        for (int i = 2; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--validate-gpu", StringComparison.OrdinalIgnoreCase))
            {
                validateGpu = true;
            }
        }

        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"File not found: {path}");
            return 1;
        }

        var data = File.ReadAllBytes(path);
        var reader = new SceneReproReader(data);

        if (!reader.TryReadHeader())
        {
            Console.Error.WriteLine($"[FAIL] Header validation failed: {reader.Result}");
            return 1;
        }

        Console.WriteLine($"[PASS] Valid envelope: version={reader.Version}, sections={reader.SectionCount}");

        if (validateGpu)
        {
            return PrintGpuSections(reader);
        }

        return 0;
    }

    // Walks the envelope's sections and pretty-prints the three GPU-diagnostic sections
    // (validation log, adapter info, surface config). Non-GPU sections are skipped with
    // a short summary line so the caller still sees a whole-file shape.
    private static int PrintGpuSections(SceneReproReader reader)
    {
        Console.WriteLine();
        Console.WriteLine("GPU diagnostic sections:");
        Console.WriteLine("────────────────────────");

        bool anyGpuSection = false;

        while (reader.TryReadNextSection(out var sectionId, out var payload))
        {
            switch (sectionId)
            {
                case ReproSection.GpuAdapterInfo:
                    anyGpuSection = true;
                    PrintAdapterInfo(payload);
                    break;
                case ReproSection.GpuSurfaceConfig:
                    anyGpuSection = true;
                    PrintSurfaceConfig(payload);
                    break;
                case ReproSection.GpuValidationLog:
                    anyGpuSection = true;
                    PrintValidationLog(payload);
                    break;
                default:
                    Console.WriteLine($"  (skipping {sectionId}, {payload.Length} bytes)");
                    break;
            }
        }

        if (!anyGpuSection)
        {
            Console.WriteLine("  [WARN] No GPU sections found in this envelope.");
            return 1;
        }

        return 0;
    }

    private static void PrintAdapterInfo(ReadOnlySpan<byte> payload)
    {
        if (!AdapterInfo.TryDecode(payload, out var info))
        {
            Console.WriteLine("  [FAIL] GpuAdapterInfo section is truncated or malformed.");
            return;
        }

        Console.WriteLine("  GpuAdapterInfo:");
        Console.WriteLine($"    Backend:     {info.BackendName} (type={info.BackendType})");
        Console.WriteLine($"    AdapterType: {info.AdapterType}");
        Console.WriteLine($"    VendorId:    0x{info.VendorId:X8}");
        Console.WriteLine($"    DeviceId:    0x{info.DeviceId:X8}");
        Console.WriteLine($"    Features:    0x{info.FeaturesBitmask:X16}");
        Console.WriteLine($"    DeviceName:  {info.DeviceName}");
        Console.WriteLine($"    Driver:      {info.DriverDescription}");
    }

    private static void PrintSurfaceConfig(ReadOnlySpan<byte> payload)
    {
        if (!SurfaceConfigInfo.TryDecode(payload, out var info))
        {
            Console.WriteLine("  [FAIL] GpuSurfaceConfig section is truncated or malformed.");
            return;
        }

        Console.WriteLine("  GpuSurfaceConfig:");
        Console.WriteLine($"    Format:      {info.Format}");
        Console.WriteLine($"    Size:        {info.Width} x {info.Height}");
        Console.WriteLine($"    PresentMode: {info.PresentMode}");
        Console.WriteLine($"    AlphaMode:   {info.AlphaMode}");
        Console.WriteLine($"    Usage:       0x{info.Usage:X8}");
    }

    private static void PrintValidationLog(ReadOnlySpan<byte> payload)
    {
        if (!ValidationLogRing.TryDecode(payload, out var snapshot))
        {
            Console.WriteLine("  [FAIL] GpuValidationLog section is truncated or malformed.");
            return;
        }

        Console.WriteLine($"  GpuValidationLog: {snapshot.Count} entries");
        for (int i = 0; i < snapshot.Count; i++)
        {
            var entry = snapshot[i];
            Console.WriteLine($"    [{i:D3}] type={entry.ErrorType} ticks={entry.TimestampTicks}");
            Console.WriteLine($"         {entry.Message}");
        }
    }

    private static int Replay(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: etch-repro replay <file.etrp>");
            return 1;
        }

        Console.WriteLine("Replay is not yet implemented. See COR-011 for full replay support.");
        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine(@"
Etch.Repro — Scene reproducer CLI

Usage:
  etch-repro dump <file.etrp>                     Dump sections and preview bytes
  etch-repro validate <file.etrp> [--validate-gpu]  Check envelope validity;
                                                    --validate-gpu pretty-prints
                                                    adapter info, surface config,
                                                    and recent validation messages.
  etch-repro replay <file.etrp>                   Replay the reproducer (not yet implemented)

Examples:
  etch-repro dump crash.etrp
  etch-repro validate scene.etrp --validate-gpu
");
    }
}
