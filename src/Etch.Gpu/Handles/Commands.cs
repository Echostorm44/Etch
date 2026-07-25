using Etch.Gpu.Descriptors;
using Etch.Gpu.Native;

namespace Etch.Gpu;

public unsafe readonly struct CommandEncoder : IDisposable
{
    private readonly CommandEncoderHandle _handle;

    public CommandEncoder(CommandEncoderHandle handle) => _handle = handle;

    public CommandEncoderHandle Handle => _handle;

    public bool IsInvalid => _handle.IsInvalid;

    public void Dispose()
    {
        if (!_handle.IsInvalid)
        {
            WebGPU.CommandEncoderRelease(_handle);
        }
    }

    public CommandBuffer Finish()
    {
        return new CommandBuffer(WebGPU.CommandEncoderFinish(_handle, 0));
    }

    public unsafe RenderPass BeginRenderPass(RenderPassDescriptor descriptor)
    {
        return new RenderPass(WebGPU.CommandEncoderBeginRenderPass(_handle, (nint)(&descriptor)));
    }

    public unsafe void CopyTextureToTexture(Texture source, uint srcMipLevel, WGPUOrigin3D srcOrigin, Texture destination, uint dstMipLevel, WGPUOrigin3D dstOrigin, Extent3D copySize)
    {
        WGPUTexelCopyTextureInfo srcInfo = default;
        srcInfo.Texture = source.Handle;
        srcInfo.MipLevel = srcMipLevel;
        srcInfo.Origin = srcOrigin;
        srcInfo.Aspect = 1u;

        WGPUTexelCopyTextureInfo dstInfo = default;
        dstInfo.Texture = destination.Handle;
        dstInfo.MipLevel = dstMipLevel;
        dstInfo.Origin = dstOrigin;
        dstInfo.Aspect = 1u;

        WebGPU.CommandEncoderCopyTextureToTexture(_handle, (nint)(&srcInfo), (nint)(&dstInfo), (nint)(&copySize));
    }

    public unsafe void CopyTextureToBuffer(Texture source, uint srcMipLevel, WGPUOrigin3D srcOrigin, Buffer destination, WGPUTexelCopyBufferLayout layout, Extent3D copySize)
    {
        WGPUTexelCopyTextureInfo srcInfo = default;
        srcInfo.Texture = source.Handle;
        srcInfo.MipLevel = srcMipLevel;
        srcInfo.Origin = srcOrigin;
        srcInfo.Aspect = 1u;

        WGPUTexelCopyBufferInfo dstInfo = default;
        dstInfo.Layout = layout;
        dstInfo.Buffer = destination.Handle;

        WebGPU.CommandEncoderCopyTextureToBuffer(_handle, (nint)(&srcInfo), (nint)(&dstInfo), (nint)(&copySize));
    }
}

public readonly struct RenderPass : IDisposable
{
    private readonly RenderPassEncoderHandle _handle;

    public RenderPass(RenderPassEncoderHandle handle) => _handle = handle;

    public RenderPassEncoderHandle Handle => _handle;

    public bool IsInvalid => _handle.IsInvalid;

    public void Dispose()
    {
        if (!_handle.IsInvalid)
        {
            WebGPU.RenderPassEncoderRelease(_handle);
        }
    }

    public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
    {
        WebGPU.RenderPassEncoderDraw(_handle, vertexCount, instanceCount, firstVertex, firstInstance);
    }

    public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0)
    {
        WebGPU.RenderPassEncoderDrawIndexed(_handle, indexCount, instanceCount, firstIndex, baseVertex, firstInstance);
    }

    public void SetPipeline(RenderPipeline pipeline)
    {
        WebGPU.RenderPassEncoderSetPipeline(_handle, pipeline.Handle);
    }

    public void SetVertexBuffer(uint slot, Buffer buffer, ulong offset = 0, ulong size = ulong.MaxValue)
    {
        WebGPU.RenderPassEncoderSetVertexBuffer(_handle, slot, buffer.Handle, offset, size);
    }

    public void SetIndexBuffer(Buffer buffer, IndexFormat format, ulong offset = 0, ulong size = ulong.MaxValue)
    {
        WebGPU.RenderPassEncoderSetIndexBuffer(_handle, buffer.Handle, (uint)format, offset, size);
    }

    public void SetBindGroup(uint groupIndex, BindGroup bindGroup)
    {
        WebGPU.RenderPassEncoderSetBindGroup(_handle, groupIndex, bindGroup.Handle, 0, 0);
    }

    public void End()
    {
        WebGPU.RenderPassEncoderEnd(_handle);
    }
}

public readonly struct CommandBuffer : IDisposable
{
    private readonly CommandBufferHandle _handle;

    public CommandBuffer(CommandBufferHandle handle) => _handle = handle;

    public CommandBufferHandle Handle => _handle;

    public bool IsInvalid => _handle.IsInvalid;

    public void Dispose()
    {
        if (!_handle.IsInvalid)
        {
            WebGPU.CommandBufferRelease(_handle);
        }
    }
}