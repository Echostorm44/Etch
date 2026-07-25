using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Etch.Gpu.Descriptors;
using Etch.Gpu.Native;

namespace Etch.Gpu;

public readonly struct Buffer : IDisposable
{
    private readonly BufferHandle _handle;

    public Buffer(BufferHandle handle) => _handle = handle;

    public BufferHandle Handle => _handle;

    public bool IsInvalid => _handle.IsInvalid;

    public void Dispose()
    {
        if (!_handle.IsInvalid)
        {
            WebGPU.BufferRelease(_handle);
        }
    }

    public void Unmap()
    {
        WebGPU.BufferUnmap(_handle);
    }

    public unsafe void MapAsync(MapMode mode, ulong offset, ulong size, IntPtr callback)
    {
        var callbackInfo = new WGPUBufferMapCallbackInfo
        {
            NextInChain = null,
            Mode = (uint)CallbackMode.AllowProcessEvents,
            Callback = callback,
            Userdata1 = null,
            Userdata2 = null
        };
        WebGPU.BufferMapAsync(_handle, (uint)mode, offset, size, (nint)(&callbackInfo));
    }

    public unsafe ReadOnlySpan<byte> GetConstMappedRange(ulong offset, ulong size)
    {
        nint ptr = WebGPU.BufferGetConstMappedRange(_handle, offset, size);
        if (ptr == 0)
            return ReadOnlySpan<byte>.Empty;
        return new ReadOnlySpan<byte>((void*)ptr, (int)size);
    }

    public unsafe Span<byte> GetMappedRange(ulong offset, ulong size)
    {
        nint ptr = WebGPU.BufferGetMappedRange(_handle, offset, size);
        if (ptr == 0)
            return Span<byte>.Empty;
        return new Span<byte>((void*)ptr, (int)size);
    }

    /// <summary>
    /// Synchronously maps the buffer, spinning on <see cref="Device.Poll"/> until the
    /// callback fires or the timeout elapses. Returns true on success.
    /// </summary>
#pragma warning disable CA1508
    public unsafe bool MapSync(Device device, MapMode mode, ulong offset = 0, ulong size = ulong.MaxValue, int timeoutMilliseconds = 5_000)
    {
        if (_handle.IsInvalid)
            return false;

        var state = new MapState { StatusValue = 0, Completed = 0 };

        var callbackInfo = new WGPUBufferMapCallbackInfo
        {
            NextInChain = null,
            Mode = (uint)CallbackMode.AllowProcessEvents,
            Callback = (IntPtr)(delegate* unmanaged[Cdecl]<uint, StringViewRaw, void*, void*, void>)&MapCallback,
            Userdata1 = (void*)&state
        };

        WebGPU.BufferMapAsync(_handle, (uint)mode, offset, size, (nint)(&callbackInfo));

        int waitedMs = 0;
        while (state.Completed == 0 && waitedMs < timeoutMilliseconds)
        {
            device.Poll(false);
            Thread.Sleep(1);
            waitedMs++;
        }

        return state.Completed != 0 && state.StatusValue == (uint)MapAsyncStatus.Success;
    }
#pragma warning restore CA1508

    [StructLayout(LayoutKind.Sequential)]
    private struct StringViewRaw
    {
        public IntPtr Data;
        public UIntPtr Length;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe void MapCallback(uint status, StringViewRaw message, void* userdata1, void* userdata2)
    {
        MapState* state = (MapState*)userdata1;
        if (state == null)
            return;
        state->StatusValue = status;
        state->Completed = 1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MapState
    {
        public uint StatusValue;
        public uint Completed;
    }
}

public readonly struct Texture : IDisposable
{
    private readonly TextureHandle _handle;

    public Texture(TextureHandle handle) => _handle = handle;

    public TextureHandle Handle => _handle;

    public bool IsInvalid => _handle.IsInvalid;

    public void Dispose()
    {
        if (!_handle.IsInvalid)
        {
            WebGPU.TextureRelease(_handle);
        }
    }

    public unsafe TextureView CreateView(TextureViewDescriptor? descriptor = null)
    {
        if (!descriptor.HasValue)
        {
            return new TextureView(WebGPU.TextureCreateView(_handle, IntPtr.Zero));
        }

        TextureViewDescriptor desc = descriptor.Value;
        return new TextureView(WebGPU.TextureCreateView(_handle, (nint)(&desc)));
    }
}

public readonly struct TextureView : IDisposable
{
    private readonly TextureViewHandle _handle;

    public TextureView(TextureViewHandle handle) => _handle = handle;

    public TextureViewHandle Handle => _handle;

    public bool IsInvalid => _handle.IsInvalid;

    public void Dispose()
    {
        if (!_handle.IsInvalid)
        {
            WebGPU.TextureViewRelease(_handle);
        }
    }
}

public readonly struct Sampler : IDisposable
{
    private readonly SamplerHandle _handle;

    public Sampler(SamplerHandle handle) => _handle = handle;

    public SamplerHandle Handle => _handle;

    public bool IsInvalid => _handle.IsInvalid;

    public void Dispose()
    {
        if (!_handle.IsInvalid)
        {
            WebGPU.SamplerRelease(_handle);
        }
    }
}