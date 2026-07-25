using Etch.Gpu;
using Etch.Gpu.Descriptors;
using Etch.Gpu.Native;
using Etch.Gpu.SwapChains;

namespace Etch.Gpu.Compositor;

public readonly ref struct FrameContext : IDisposable
{
    private readonly SwapChain _swapChain;
    private readonly CommandEncoder _encoder;
    private readonly SurfaceTexture _texture;
    private readonly bool _ownsTexture;

    public SurfaceTextureResult LastAcquireResult { get; }

    internal FrameContext(SwapChain swapChain, CommandEncoder encoder, SurfaceTexture texture, SurfaceTextureResult acquireResult, bool ownsTexture)
    {
        _swapChain = swapChain;
        _encoder = encoder;
        _texture = texture;
        LastAcquireResult = acquireResult;
        _ownsTexture = ownsTexture;
    }

    public RenderPass BeginRenderPass(RenderPassDescriptor descriptor)
    {
        return _encoder.BeginRenderPass(descriptor);
    }

    public void CopyTextureToTexture(Texture source, uint srcMipLevel, WGPUOrigin3D srcOrigin, Texture destination, uint dstMipLevel, WGPUOrigin3D dstOrigin, Extent3D copySize)
    {
        _encoder.CopyTextureToTexture(source, srcMipLevel, srcOrigin, destination, dstMipLevel, dstOrigin, copySize);
    }

    public void Submit(Queue queue)
    {
        using CommandBuffer commands = _encoder.Finish();
        Span<CommandBuffer> commandList = stackalloc CommandBuffer[1];
        commandList[0] = commands;
        queue.Submit(commandList);
    }

    public SurfaceTexture Texture => _texture;

    public void Present()
    {
        if (_ownsTexture && _texture.IsValid)
        {
            _swapChain.Present(_texture);
        }
    }

    public void Dispose()
    {
        if (_ownsTexture && _texture.IsValid)
        {
            _texture.Dispose();
        }
        _encoder.Dispose();
    }
}