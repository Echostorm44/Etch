using System;
using System.Runtime.InteropServices;
using System.Text;
using Etch.Gpu.Descriptors;
using Etch.Gpu.Native;

namespace Etch.Gpu;

// ═══════════════════════════════════════════════════════════════════════════
// Top-level WebGPU wrappers: Instance, Adapter, Device, Queue.
//
// v29 creation pattern: every wgpu*Create* function takes a pointer to a
// descriptor struct whose String fields are WGPUStringView (ptr+len) and
// whose flag fields are 64-bit. The descriptors in Etch.Gpu.Descriptors are
// sized and laid out to match exactly; we can therefore blit them with a
// simple `fixed` pin instead of going through Marshal.StructureToPtr.
//
// Note on shader modules: WGSL source is NOT a field of
// WGPUShaderModuleDescriptor in v29. It rides in on a chained
// WGPUShaderSourceWGSL. Use `CreateShaderModuleWgsl` to do it correctly;
// the raw `CreateShaderModule(descriptor)` overload is kept for callers
// that want to build their own chain.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// A handle to a wgpu-native instance. This is the root of the GPU hierarchy;
/// create one at application startup and dispose at shutdown.
/// </summary>
public readonly struct Instance : IDisposable
{
    private readonly InstanceHandle _handle;

    public Instance(InstanceHandle handle) => _handle = handle;

    public InstanceHandle Handle => _handle;

    public bool IsInvalid => _handle.IsInvalid;

    public void Dispose()
    {
        if (!_handle.IsInvalid)
        {
            WebGPU.InstanceRelease(_handle);
        }
    }

    /// <summary>Creates a new wgpu instance with optional capabilities.</summary>
    public static unsafe Instance Create(InstanceDescriptor? descriptor = null)
    {
        if (!descriptor.HasValue)
        {
            return new Instance(WebGPU.CreateInstance(IntPtr.Zero));
        }

        InstanceDescriptor desc = descriptor.Value;
        return new Instance(WebGPU.CreateInstance((nint)(&desc)));
    }
}

public readonly struct Adapter : IDisposable
{
    private readonly AdapterHandle _handle;

    public Adapter(AdapterHandle handle) => _handle = handle;

    public AdapterHandle Handle => _handle;

    public bool IsInvalid => _handle.IsInvalid;

    public void Dispose()
    {
        if (!_handle.IsInvalid)
        {
            WebGPU.AdapterRelease(_handle);
        }
    }
}

/// <summary>
/// A handle to a wgpu-native logical device. Created from an <see cref="Adapter"/>;
/// owns the command queue and is the factory for buffers, textures, pipelines, etc.
/// </summary>
public readonly struct Device : IDisposable
{
    private readonly DeviceHandle _handle;
    private readonly Queue _queue;

    public Device(DeviceHandle handle)
    {
        _handle = handle;
        _queue = handle.IsInvalid ? default : new Queue(WebGPU.DeviceGetQueue(handle));
    }

    public DeviceHandle Handle => _handle;

    public Queue Queue => _queue;

    public bool IsInvalid => _handle.IsInvalid;

    public void Dispose()
    {
        if (!_handle.IsInvalid)
        {
            _queue.Dispose();
            WebGPU.DeviceRelease(_handle);
        }
    }

    public void Poll(bool wait = false)
    {
        WebGPU.DevicePoll(_handle, wait ? (byte)1 : (byte)0, IntPtr.Zero);
    }

    public unsafe Buffer CreateBuffer(BufferDescriptor descriptor)
    {
        return new Buffer(WebGPU.DeviceCreateBuffer(_handle, (nint)(&descriptor)));
    }

    // Raw variant: caller has already assembled a valid chained descriptor
    // (e.g. WGPUShaderSourceWGSL) and keeps all referenced memory pinned
    // for the duration of this call.
    public unsafe ShaderModule CreateShaderModule(ShaderModuleDescriptor descriptor)
    {
        return new ShaderModule(WebGPU.DeviceCreateShaderModule(_handle, (nint)(&descriptor)));
    }

    // Convenience: encode WGSL + label into temporary buffers, chain them
    // through ShaderSourceWGSL, pin for the native call. Everything lives
    // on the stack or in ArrayPool-free managed buffers that exit scope
    // before this method returns, so there is zero retention risk.
    public unsafe ShaderModule CreateShaderModuleWgsl(string wgsl, string? label = null)
    {
        if (wgsl is null)
        {
            Panic.ArgumentNull(nameof(wgsl));
        }

        int codeByteCount = Encoding.UTF8.GetByteCount(wgsl);
        byte[] codeBuffer = new byte[codeByteCount];
        Encoding.UTF8.GetBytes(wgsl, codeBuffer);

        Span<byte> labelScratch = stackalloc byte[Labels.MaxLabelLength + 1];
        int labelLength = Labels.EncodeUtf8(label, labelScratch);

        fixed (byte* codePtr = codeBuffer)
        fixed (byte* labelPtr = labelScratch)
        {
            ShaderSourceWGSL wgslSource = default;
            wgslSource.Chain.NextInChain = IntPtr.Zero;
            wgslSource.Chain.SType = WGPUSType.ShaderSourceWGSL;
            wgslSource.Code.Data = (IntPtr)codePtr;
            wgslSource.Code.Length = (UIntPtr)codeByteCount;

            ShaderModuleDescriptor desc = default;
            desc.NextInChain = (IntPtr)(&wgslSource);
            desc.Label.Data = label is null ? IntPtr.Zero : (IntPtr)labelPtr;
            desc.Label.Length = (UIntPtr)labelLength;

            return new ShaderModule(WebGPU.DeviceCreateShaderModule(_handle, (nint)(&desc)));
        }
    }

    public unsafe Sampler CreateSampler(SamplerDescriptor? descriptor = null)
    {
        if (!descriptor.HasValue)
        {
            return new Sampler(WebGPU.DeviceCreateSampler(_handle, IntPtr.Zero));
        }

        SamplerDescriptor desc = descriptor.Value;
        return new Sampler(WebGPU.DeviceCreateSampler(_handle, (nint)(&desc)));
    }

    public unsafe BindGroupLayout CreateBindGroupLayout(BindGroupLayoutDescriptor descriptor)
    {
        return new BindGroupLayout(WebGPU.DeviceCreateBindGroupLayout(_handle, (nint)(&descriptor)));
    }

    public unsafe PipelineLayout CreatePipelineLayout(PipelineLayoutDescriptor descriptor)
    {
        return new PipelineLayout(WebGPU.DeviceCreatePipelineLayout(_handle, (nint)(&descriptor)));
    }

    public unsafe BindGroup CreateBindGroup(BindGroupDescriptor descriptor)
    {
        return new BindGroup(WebGPU.DeviceCreateBindGroup(_handle, (nint)(&descriptor)));
    }

    public unsafe RenderPipeline CreateRenderPipeline(RenderPipelineDescriptor descriptor)
    {
        return new RenderPipeline(WebGPU.DeviceCreateRenderPipeline(_handle, (nint)(&descriptor)));
    }

    public unsafe CommandEncoder CreateCommandEncoder(CommandEncoderDescriptor? descriptor = null)
    {
        if (!descriptor.HasValue)
        {
            return new CommandEncoder(WebGPU.DeviceCreateCommandEncoder(_handle, IntPtr.Zero));
        }

        CommandEncoderDescriptor desc = descriptor.Value;
        return new CommandEncoder(WebGPU.DeviceCreateCommandEncoder(_handle, (nint)(&desc)));
    }

    public unsafe Texture CreateTexture(TextureDescriptor descriptor)
    {
        return new Texture(WebGPU.DeviceCreateTexture(_handle, (nint)(&descriptor)));
    }
}

public readonly struct Queue : IDisposable
{
    private readonly QueueHandle _handle;

    public Queue(QueueHandle handle) => _handle = handle;

    public QueueHandle Handle => _handle;

    public bool IsInvalid => _handle.IsInvalid;

    public void Dispose()
    {
        if (!_handle.IsInvalid)
        {
            WebGPU.QueueRelease(_handle);
        }
    }

    public unsafe void Submit(ReadOnlySpan<CommandBuffer> commands)
    {
        if (commands.Length == 0)
        {
            return;
        }

        Span<nint> handles = stackalloc nint[commands.Length];
        for (int i = 0; i < commands.Length; i++)
        {
            handles[i] = commands[i].Handle;
        }

        fixed (nint* ptr = handles)
        {
            WebGPU.QueueSubmit(_handle, (nuint)commands.Length, (nint)ptr);
        }
    }

    public unsafe void WriteBuffer(Buffer buffer, ulong bufferOffset, ReadOnlySpan<byte> data)
    {
        fixed (byte* ptr = data)
        {
            WebGPU.QueueWriteBuffer(_handle, buffer.Handle, bufferOffset, (nint)ptr, (nuint)data.Length);
        }
    }

    public unsafe void WriteTexture(Texture texture, uint mipLevel, WGPUOrigin3D origin, ReadOnlySpan<byte> data, uint bytesPerRow, uint rowsPerImage, Extent3D writeSize)
    {
        WGPUTexelCopyTextureInfo destination = default;
        destination.Texture = texture.Handle;
        destination.MipLevel = mipLevel;
        destination.Origin = origin;
        destination.Aspect = 1u; // WGPUTextureAspect_All

        WGPUTexelCopyBufferLayout layout = default;
        layout.Offset = 0;
        layout.BytesPerRow = bytesPerRow;
        layout.RowsPerImage = rowsPerImage;

        fixed (byte* ptr = data)
        {
            WebGPU.QueueWriteTexture(_handle, (nint)(&destination), (nint)ptr, (nuint)data.Length, (nint)(&layout), (nint)(&writeSize));
        }
    }
}
