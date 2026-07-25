namespace Etch.Gpu.Compositor;

public readonly struct TileQuadBuffers : IDisposable
{
    public Buffer UnitQuadVertex { get; }
    public Buffer PerTileInstance { get; }

    public TileQuadBuffers(Buffer unitQuadVertex, Buffer perTileInstance)
    {
        UnitQuadVertex = unitQuadVertex;
        PerTileInstance = perTileInstance;
    }

    public void Dispose()
    {
        PerTileInstance.Dispose();
        UnitQuadVertex.Dispose();
    }
}