namespace Etch.Shaders;

public sealed class EmbeddedShaderSource : IShaderSource
{
    private readonly ulong _version;

    public EmbeddedShaderSource()
    {
        var version = typeof(EmbeddedShaderSource).Assembly.GetName().Version;
        _version = version != null ? (ulong)version.GetHashCode() : 0;
    }

    public ReadOnlySpan<byte> GetSource(string name)
    {
        return name switch
        {
            "solid_fill" => ShaderResources.solid_fill,
            "duplicate_binding" => ShaderResources.duplicate_binding,
            "no_vertex_entry" => ShaderResources.no_vertex_entry,
            "test" => ShaderResources.test,
            _ => ReadOnlySpan<byte>.Empty
        };
    }

    public bool TryGetVersion(string name, out ulong version)
    {
        version = _version;
        return true;
    }

    public event EventHandler<string>? Changed;
}