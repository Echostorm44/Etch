using System.IO;
using Etch;
using Etch.Raster.Cpu;
using Etch.Scene;
using Etch.Scene.Serialization;
using Etch.Tiling;
using Etch.Tiling.Classify;
using Etch.Tiling.Strips;

namespace Etch.SceneReplayer.Commands;

public static class RenderCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: etch-replay render <scene.etsc> --backend=cpu|gpu --out=<png> [--width W --height H] [--scale S]");
            return 1;
        }

        string scenePath = args[0];
        string backend = "cpu";
        string output = "";
        int width = 1920;
        int height = 1080;
        double scale = 1.0;

        for (int i = 1; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg.StartsWith("--backend=", StringComparison.Ordinal) || arg.StartsWith("-b=", StringComparison.Ordinal))
                backend = arg.Split('=')[1];
            else if (arg.StartsWith("--out=", StringComparison.Ordinal) || arg.StartsWith("-o=", StringComparison.Ordinal))
                output = arg.Split('=')[1];
            else if (arg.StartsWith("--width=", StringComparison.Ordinal) || arg.StartsWith("-w=", StringComparison.Ordinal))
                width = int.Parse(arg.Split('=')[1]);
            else if (arg.StartsWith("--height=", StringComparison.Ordinal) || arg.StartsWith("-h=", StringComparison.Ordinal))
                height = int.Parse(arg.Split('=')[1]);
            else if (arg.StartsWith("--scale=", StringComparison.Ordinal) || arg.StartsWith("-s=", StringComparison.Ordinal))
                scale = double.Parse(arg.Split('=')[1]);
        }

        if (!File.Exists(scenePath))
        {
            Console.Error.WriteLine($"Error: scene file not found: {scenePath}");
            return 1;
        }

        Console.WriteLine($"Rendering {scenePath} via {backend} backend to {output} ({width}x{height})");

        byte[] sceneBytes = File.ReadAllBytes(scenePath);
        var scene = SceneReader.Read(sceneBytes);

        byte[] pixels;
        if (backend == "gpu")
        {
            try
            {
                pixels = Etch.Testing.SceneGpuRenderer.RenderToRgba8(scene, width, height);
            }
            catch (EtchException)
            {
                Console.WriteLine("GPU unavailable — falling back to CPU.");
                pixels = RenderCpu(scene, width, height);
            }
        }
        else
        {
            pixels = RenderCpu(scene, width, height);
        }

        if (!string.IsNullOrEmpty(output))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            WritePng(output, pixels, width, height);
            Console.WriteLine($"Written to {output}");
        }
        else
        {
            Console.WriteLine($"Rendered {pixels.Length} bytes (no --out specified, output skipped).");
        }

        return 0;
    }

    private static byte[] RenderCpu(SceneBuffer scene, int width, int height)
    {
        var grid = new TileGrid<TTile8>(width, height);
        var accum = new ClassificationAccumulator(4096);
        BBoxClassifier.Classify(scene, grid, ref accum);
        var entries = accum.Finish().ToArray();
        var classified = ClassificationMerge.Merge([entries], grid);

        var fbBuffer = new Rgba16f[width * height];
        for (int i = 0; i < fbBuffer.Length; i++)
            fbBuffer[i] = Rgba16f.From(0, 0, 0, 0);
        var fb = new Framebuffer(width, height, width, fbBuffer);

        var commands = scene.Commands;
        for (int i = 0; i < commands.Length; i++)
        {
            ref readonly var cmd = ref commands[i];
            if (cmd.Op == SceneOpcode.FillRect || cmd.Op == SceneOpcode.FillPath || cmd.Op == SceneOpcode.StrokePath)
            {
                var filtered = FilterClassifiedByCommandOrder(classified, i);
                var strips = StripEmitter.Emit(scene, filtered, grid);
                StripRenderer.Render(scene, strips, grid, fb);
            }
        }

        var rgba8 = new byte[width * height * 4];
        Srgb.EncodeLinearF16ToRgba8(fbBuffer, rgba8);
        return rgba8;
    }

    private static void WritePng(string path, byte[] rgba8, int w, int h)
    {
        Etch.Testing.ImageWriter.WriteRgbaToPng(path, rgba8, w, h);
    }

    private static ClassifiedScene FilterClassifiedByCommandOrder(ClassifiedScene source, int commandOrder)
    {
        var all = source.AllEntries;
        int count = 0;
        for (int i = 0; i < all.Length; i++)
            if (all[i].CommandOrder == commandOrder)
                count++;

        if (count == 0)
            return new ClassifiedScene([], new int[source.TileCount + 1], source.TileCount);

        var filtered = new ClassificationEntry[count];
        var offsets = new int[source.TileCount + 1];
        int writeIdx = 0;
        for (int t = 0; t < source.TileCount; t++)
        {
            offsets[t] = writeIdx;
            var tileEntries = source.Entries(t);
            for (int e = 0; e < tileEntries.Length; e++)
                if (tileEntries[e].CommandOrder == commandOrder)
                    filtered[writeIdx++] = tileEntries[e];
        }
        offsets[source.TileCount] = writeIdx;
        return new ClassifiedScene(filtered, offsets, source.TileCount);
    }
}
