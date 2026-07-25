using System;

namespace Etch.Scene;

#pragma warning disable CA1819

public sealed class MeshGradient
{
    public int Rows { get; }
    public int Cols { get; }
    public int VertexCount => Rows * Cols;
    public MeshVertex[] Vertices { get; }

    public MeshGradient(int rows, int cols, MeshVertex[] vertices)
    {
        ArgumentNullException.ThrowIfNull(vertices);

        if (rows < 2)
            Etch.Panic.Invariant(Etch.PanicCodes.DegenerateMesh, $"Mesh gradient must have at least 2 rows, got {rows}");
        if (cols < 2)
            Etch.Panic.Invariant(Etch.PanicCodes.DegenerateMesh, $"Mesh gradient must have at least 2 columns, got {cols}");
        if (vertices.Length != rows * cols)
            Etch.Panic.Invariant(Etch.PanicCodes.DegenerateMesh, $"Vertex count {vertices.Length} does not match rows×cols ({rows}×{cols} = {rows * cols})");

        Rows = rows;
        Cols = cols;
        Vertices = vertices;
    }

    public MeshVertex GetVertex(int row, int col)
    {
        return Vertices[row * Cols + col];
    }
}
