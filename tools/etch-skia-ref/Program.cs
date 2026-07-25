using System;
using System.IO;
using Etch.Scene;
using Etch.Scene.Serialization;
using Etch.SkiaRef;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: Etch.SkiaRef <scene.etsc> <output.png> [width=256] [height=256]");
    return 1;
}

string scenePath = args[0];
string outputPath = args[1];
int width = args.Length > 2 ? int.Parse(args[2]) : 256;
int height = args.Length > 3 ? int.Parse(args[3]) : 256;

if (!File.Exists(scenePath))
{
    Console.Error.WriteLine($"Error: scene file not found: {scenePath}");
    return 1;
}

byte[] sceneBytes = File.ReadAllBytes(scenePath);
var scene = SceneReader.Read(sceneBytes);

byte[] png = SkiaSceneRenderer.Render(scene, width, height);

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
File.WriteAllBytes(outputPath, png);
Console.WriteLine($"Written {png.Length} bytes to {outputPath}");

return 0;
