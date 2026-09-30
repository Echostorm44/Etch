namespace Etch.Scene;

/// <summary>
/// Table sizes for a <see cref="SceneBuilder"/>: commands, path-arena bytes, paths, paints,
/// transforms and rects. See <see cref="SceneBuilder.Begin(SceneCapacity)"/>.
/// </summary>
public readonly record struct SceneCapacity(int Commands, int PathArenaBytes, int Paths, int Paints, int Transforms, int Rects);
