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
foreach (var jsonFile in Directory.GetFiles(corpusDir, "*.json", SearchOption.AllDirectories))
{
    string pngPath = Path.ChangeExtension(jsonFile, ".png");

    if (dryRun)
    {
        Console.WriteLine($"  [dry-run] {jsonFile} -> {pngPath}");
        regenCount++;
        continue;
    }

    try
    {
        string json = File.ReadAllText(jsonFile);
        var serialized = SerializedScene.Deserialize(json);
        var scene = serialized.ToSceneBuffer();

        byte[] png = SkiaSceneRenderer.Render(scene, 256, 256);
        File.WriteAllBytes(pngPath, png);
        regenCount++;
        Console.WriteLine($"  Regenerated: {pngPath}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"  Error processing {jsonFile}: {ex.Message}");
    }
}

Console.WriteLine($"{(dryRun ? "Would regenerate" : "Regenerated")} {regenCount} golden images.");
return 0;
