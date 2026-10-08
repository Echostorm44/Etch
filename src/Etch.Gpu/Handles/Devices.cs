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
            using var gate = WarpSerialization.Enter();
            WebGPU.InstanceRelease(_handle);
        }
    }

    /// <summary>
    /// Creates an instance limited to the platform's primary backend
    /// (<see cref="InstanceOptions.Default"/>).
    /// </summary>
    public static Instance Create() => Create(InstanceOptions.Default);

    /// <summary>Creates an instance with the given backends and flags.</summary>
    public static unsafe Instance Create(InstanceOptions options)
    {
        WGPUInstanceExtras extras = default;
        extras.Chain.SType = WGPUSType.InstanceExtras;
        extras.Backends = (ulong)options.Backends;
        extras.Flags = (ulong)options.Flags;

        InstanceDescriptor descriptor = default;
        descriptor.NextInChain = (IntPtr)(&extras);
        return new Instance(WebGPU.CreateInstance((nint)(&descriptor)));
    }

    /// <summary>
    /// Creates an instance from a caller-assembled descriptor. Nothing is added to its chain, so
    /// without a chained WGPUInstanceExtras wgpu enables every backend.
    /// </summary>
    public static unsafe Instance CreateRaw(InstanceDescriptor descriptor)
    {
        return new Instance(WebGPU.CreateInstance((nint)(&descriptor)));
    }

    /// <summary>
    /// Every adapter on the backends this instance enabled, in the platform's enumeration order
    /// (DXGI's on Windows). The caller owns and must dispose each returned adapter.
    /// </summary>
    public unsafe Adapter[] EnumerateAdapters()
    {
        if (_handle.IsInvalid)
        {
            return [];
        }

        int count = (int)WebGPU.InstanceEnumerateAdapters(_handle, IntPtr.Zero, IntPtr.Zero);
        if (count == 0)
        {
            return [];
        }

        Span<nint> handles = count <= 32 ? stackalloc nint[count] : new nint[count];
        int written;
        fixed (nint* handlesPtr = handles)
        {
            written = (int)WebGPU.InstanceEnumerateAdapters(_handle, IntPtr.Zero, (nint)handlesPtr);
        }
        var adapters = new Adapter[Math.Min(count, written)];
        for (int i = 0; i < adapters.Length; i++)
        {
            adapters[i] = new Adapter(new AdapterHandle(handles[i]));
        }
        return adapters;
    }
}

/// <summary>What an adapter is: which GPU, on which backend, of which kind.</summary>
public readonly struct AdapterDescription
{
    public AdapterDescription(BackendType backend, AdapterType type, uint vendorId, uint deviceId, string name, string driver)
    {
        Backend = backend;
        Type = type;
        VendorId = vendorId;
        DeviceId = deviceId;
        Name = name;
        Driver = driver;
    }

    public BackendType Backend { get; }

    public AdapterType Type { get; }

    /// <summary>PCI vendor id (0x10DE NVIDIA, 0x1002 AMD, 0x8086 Intel; 0x1414 Microsoft for WARP).</summary>
    public uint VendorId { get; }

    /// <summary>PCI device id.</summary>
    public uint DeviceId { get; }

    /// <summary>The adapter's name, e.g. "NVIDIA GeForce RTX 4090".</summary>
    public string Name { get; }

    /// <summary>Driver description as reported by the backend; may be empty.</summary>
    public string Driver { get; }

    /// <summary>True for a software rasterizer (WARP, llvmpipe, lavapipe).</summary>
    public bool IsSoftware => Type == AdapterType.CPU;

    public override string ToString() => $"{Name} ({Type}, {Backend})";
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

    /// <summary>Describes the adapter; a default description when the adapter is invalid or the query fails.</summary>
    public unsafe AdapterDescription GetDescription()
    {
        if (_handle.IsInvalid)
        {
            return default;
        }

        WGPUAdapterInfo info = default;
        if (WebGPU.AdapterGetInfo(_handle, (nint)(&info)) != 1)
        {
            return default;
        }

        try
        {
            return new AdapterDescription(
                (BackendType)info.BackendType,
                (AdapterType)info.AdapterType,
                info.VendorId,
                info.DeviceId,
                ReadString(info.Device),
                ReadString(info.Description));
        }
        finally
        {
            WebGPU.AdapterInfoFreeMembers(info);
        }
    }

    /// <summary>True when this adapter can present to <paramref name="surface"/> with at least one format.</summary>
    /// <param name="surface">The window surface frames will be presented to.</param>
    public unsafe bool CanPresentTo(Surface surface)
    {
        if (_handle.IsInvalid || !surface.IsValid)
        {
            return false;
        }

        WGPUSurfaceCapabilities capabilities = default;
        if (WebGPU.SurfaceGetCapabilities(surface.Handle, _handle, (nint)(&capabilities)) != 1)
        {
            return false;
        }

        bool usable = capabilities.FormatCount > 0;
        WebGPU.SurfaceCapabilitiesFreeMembers(capabilities);
        return usable;
    }

    private static unsafe string ReadString(WGPUStringView view)
    {
        if (view.Data == null || view.Length == 0)
        {
            return string.Empty;
        }
        int length = view.Length == WGPUStringView.StrLen
            ? MemoryMarshal.CreateReadOnlySpanFromNullTerminated(view.Data).Length
            : (int)Math.Min((ulong)view.Length, int.MaxValue);
        return Encoding.UTF8.GetString(view.Data, length);
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
            using var gate = WarpSerialization.Enter();
            _queue.Dispose();
            WebGPU.DeviceRelease(_handle);
        }
    }

    /// <summary>
    /// Destroys the device's GPU resources now: the device is lost (reason Destroyed) and every
    /// later use fails, as after a driver reset. The handle is still released by <see cref="Dispose"/>.
    /// </summary>
    public void Destroy()
    {
        if (!_handle.IsInvalid)
        {
            using var gate = WarpSerialization.Enter();
            WebGPU.DeviceDestroy(_handle);
        }
    }

    /// <summary>Processes completed GPU work; with <paramref name="wait"/>, blocks until the queue drains.</summary>
    /// <param name="wait">Block until all submitted work has finished.</param>
    /// <returns>True when no submissions are still in flight.</returns>
    public bool Poll(bool wait = false)
    {
        // Retiring a submission can destroy pipelines released while it was in flight.
        using var gate = WarpSerialization.Enter();
        return WebGPU.DevicePoll(_handle, wait ? 1u : 0u, IntPtr.Zero) != 0;
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
            using var gate = WarpSerialization.Enter();
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
            using var gate = WarpSerialization.Enter();
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
