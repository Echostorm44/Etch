using Etch.Gpu.Native;

namespace Etch.Gpu;

public readonly struct Surface : IDisposable
{
    private readonly SurfaceHandle _handle;
    private readonly string? _label;

    public Surface(SurfaceHandle handle, string? label = null)
    {
        _handle = handle;
        _label = label;
    }

    public SurfaceHandle Handle => _handle;

    public bool IsValid => !_handle.IsInvalid;

    public void Dispose()
    {
        if (!_handle.IsInvalid)
        {
            WebGPU.SurfaceRelease(_handle);
        }
    }
}