using System;
using System.IO;
using Etch.Scene;
using Etch.Scene.Serialization;
using Etch.SkiaRef;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: Etch.GoldenRegen <corpus-dir> [--dry-run]");
    return 1;
}

string corpusDir = args[0];
bool dryRun = args.Length > 1 && args[1] == "--dry-run";

if (!Directory.Exists(corpusDir))
{
    Console.Error.WriteLine($"Error: corpus directory not found: {corpusDir}");
    return 1;
}

int regenCount = 0;
foreach (var sceneFile in Directory.GetFiles(corpusDir, "*.etsc", SearchOption.AllDirectories))
{
    string pngPath = Path.ChangeExtension(sceneFile, ".png");

    if (dryRun)
    {
        Console.WriteLine($"  [dry-run] {sceneFile} -> {pngPath}");
        regenCount++;
        continue;
    }

    try
    {
        byte[] sceneBytes = File.ReadAllBytes(sceneFile);
        using var scene = SceneReader.Read(sceneBytes);

        byte[] png = SkiaSceneRenderer.Render(scene, 256, 256);
        File.WriteAllBytes(pngPath, png);
        regenCount++;
        Console.WriteLine($"  Regenerated: {pngPath}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"  Error processing {sceneFile}: {ex.Message}");
    }
}

Console.WriteLine($"{(dryRun ? "Would regenerate" : "Regenerated")} {regenCount} golden images.");
return 0;
