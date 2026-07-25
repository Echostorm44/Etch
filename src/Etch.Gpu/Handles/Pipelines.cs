using Etch.Gpu.Native;

namespace Etch.Gpu;

public readonly struct ShaderModule : IDisposable
{
    private readonly ShaderModuleHandle _handle;

    public ShaderModule(ShaderModuleHandle handle) => _handle = handle;

    public ShaderModuleHandle Handle => _handle;

    public bool IsInvalid => _handle.IsInvalid;

    public void Dispose()
    {
        if (!_handle.IsInvalid)
        {
            WebGPU.ShaderModuleRelease(_handle);
        }
    }
}

public readonly struct BindGroupLayout : IDisposable
{
    private readonly BindGroupLayoutHandle _handle;

    public BindGroupLayout(BindGroupLayoutHandle handle) => _handle = handle;

    public BindGroupLayoutHandle Handle => _handle;

    public bool IsInvalid => _handle.IsInvalid;

    public void Dispose()
    {
        if (!_handle.IsInvalid)
        {
            WebGPU.BindGroupLayoutRelease(_handle);
        }
    }
}

public readonly struct PipelineLayout : IDisposable
{
    private readonly PipelineLayoutHandle _handle;

    public PipelineLayout(PipelineLayoutHandle handle) => _handle = handle;

    public PipelineLayoutHandle Handle => _handle;

    public bool IsInvalid => _handle.IsInvalid;

    public void Dispose()
    {
        if (!_handle.IsInvalid)
        {
            WebGPU.PipelineLayoutRelease(_handle);
        }
    }
}

public readonly struct BindGroup : IDisposable
{
    private readonly BindGroupHandle _handle;

    public BindGroup(BindGroupHandle handle) => _handle = handle;

    public BindGroupHandle Handle => _handle;

    public bool IsInvalid => _handle.IsInvalid;

    public void Dispose()
    {
        if (!_handle.IsInvalid)
        {
            WebGPU.BindGroupRelease(_handle);
        }
    }
}

public readonly struct RenderPipeline : IDisposable
{
    private readonly RenderPipelineHandle _handle;

    public RenderPipeline(RenderPipelineHandle handle) => _handle = handle;

    public RenderPipelineHandle Handle => _handle;

    public bool IsInvalid => _handle.IsInvalid;

    public void Dispose()
    {
        if (!_handle.IsInvalid)
        {
            WebGPU.RenderPipelineRelease(_handle);
        }
    }
}