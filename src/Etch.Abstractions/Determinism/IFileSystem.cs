namespace Etch.Abstractions.Determinism;

/// <summary>
/// Provides file system access for deterministic rendering.
/// All file I/O must route through this seam.
/// </summary>
/// <remarks>
/// Failure to use this seam causes pixel-identical output guarantees to be violated,
/// as file system state varies across machines and runs. The production default
/// maps straight to <see cref="System.IO"/>; test doubles use in-memory storage.
/// </remarks>
public interface IFileSystem
{
    /// <summary>
    /// Reads the contents of the file at <paramref name="path"/> as bytes.
    /// </summary>
    byte[] ReadAllBytes(string path);

    /// <summary>
    /// Reads the contents of the file at <paramref name="path"/> as text.
    /// </summary>
    string ReadAllText(string path);

    /// <summary>
    /// Writes <paramref name="data"/> to the file at <paramref name="path"/>.
    /// </summary>
    void WriteAllBytes(string path, byte[] data);

    /// <summary>
    /// Writes <paramref name="content"/> to the file at <paramref name="path"/>.
    /// </summary>
    void WriteAllText(string path, string content);
}