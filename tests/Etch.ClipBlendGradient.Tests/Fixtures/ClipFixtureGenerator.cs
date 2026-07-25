using System;
using System.IO;
using System.Text.Json;
using Etch.Scene;
using Etch.Scene.Serialization;

namespace Etch.ClipBlendGradient.Tests;

internal static class ClipFixtureGenerator
{
    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

    public static void GenerateAll(string outputDir)
    {
        Directory.CreateDirectory(outputDir);

        GenerateScene(outputDir, "nested-circles", ClipFixtureScenes.NestedCircles(), toleranceMean: 1.0f, toleranceMax: 3.0f);
        GenerateScene(outputDir, "rect-minus-circle", ClipFixtureScenes.RectMinusCircle(), toleranceMean: 1.0f, toleranceMax: 3.0f);
        GenerateScene(outputDir, "soft-clipped-rect", ClipFixtureScenes.SoftClippedRect(), toleranceMean: 1.5f, toleranceMax: 4.0f);
        GenerateScene(outputDir, "8-level-nesting", ClipFixtureScenes.EightLevelNesting(), toleranceMean: 1.0f, toleranceMax: 3.0f);
        GenerateScene(outputDir, "clip-around-solid", ClipFixtureScenes.ClipAroundSolid(), toleranceMean: 1.5f, toleranceMax: 4.0f);
        GenerateScene(outputDir, "overlapping-clips", ClipFixtureScenes.OverlappingClips(), toleranceMean: 1.0f, toleranceMax: 3.0f);
        GenerateScene(outputDir, "clip-then-translate", ClipFixtureScenes.ClipThenTranslate(), toleranceMean: 1.0f, toleranceMax: 3.0f);
        GenerateScene(outputDir, "clip-rotate", ClipFixtureScenes.ClipRotate(), toleranceMean: 2.0f, toleranceMax: 6.0f, notes: "Rotated clip has slightly higher tolerance due to sub-pixel edge positioning.");
        GenerateScene(outputDir, "clip-scale", ClipFixtureScenes.ClipScale(), toleranceMean: 1.0f, toleranceMax: 3.0f);
        GenerateScene(outputDir, "non-convex-clip", ClipFixtureScenes.NonConvexClip(), toleranceMean: 2.0f, toleranceMax: 6.0f, notes: "Non-convex clip uses different flatten strategy; scene-specific override applied.");
    }

    private static void GenerateScene(string dir, string name, SceneBuffer scene, float toleranceMean, float toleranceMax, string? notes = null)
    {
        string etscPath = Path.Combine(dir, $"{name}.etsc");
        string jsonPath = Path.Combine(dir, $"{name}.json");

        int size = SceneWriter.GetRequiredSize(scene);
        byte[] buffer = new byte[size];
        int written = SceneWriter.Write(scene, buffer);
        File.WriteAllBytes(etscPath, buffer.AsSpan(0, written).ToArray());

        var json = new
        {
            scenePath = $"{name}.etsc",
            goldenPath = $"{name}.png",
            toleranceMean,
            toleranceMax,
            notes
        };
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(json, s_jsonOptions));
    }
}
