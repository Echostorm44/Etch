using System.IO;
using Etch.Scene;
using Etch.Scene.Serialization;

namespace Etch.SceneReplayer.Commands;

public static class DumpCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: etch-replay dump <scene.etsc>");
            return 1;
        }

        string scenePath = args[0];
        if (!File.Exists(scenePath))
        {
            Console.Error.WriteLine($"Error: scene file not found: {scenePath}");
            return 1;
        }

        byte[] sceneBytes = File.ReadAllBytes(scenePath);
        var scene = SceneReader.Read(sceneBytes);

        Console.WriteLine($"Scene: {scenePath} ({sceneBytes.Length} bytes)");
        Console.WriteLine($"Commands: {scene.Commands.Length}");
        Console.WriteLine($"Paints: {scene.PaintCount}");
        Console.WriteLine($"Paths: {scene.PathCount}");
        Console.WriteLine($"Transforms: {scene.TransformCount}");
        Console.WriteLine($"Rects: {scene.RectCount}");
        Console.WriteLine($"Gradient stops: {scene.GradientStopsCount}");
        Console.WriteLine();

        for (int i = 0; i < scene.Commands.Length; i++)
        {
            ref readonly var cmd = ref scene.Commands[i];
            Console.WriteLine($"  [{i}] {cmd.Op}");
        }

        return 0;
    }
}
