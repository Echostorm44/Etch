using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Etch.Gpu.Descriptors;
using Etch.Gpu.Native;

namespace Etch.Gpu;

// ═══════════════════════════════════════════════════════════════════════════
// Async adapter / device request helpers for wgpu-native v29.
//
// v29 callback ABI (webgpu.h):
//   void (*WGPURequestAdapterCallback)(
//       WGPURequestAdapterStatus status,
//       WGPUAdapter              adapter,
//       WGPUStringView           message,
//       void*                    userdata1,
//       void*                    userdata2);
//
//   void (*WGPURequestDeviceCallback)(
//       WGPURequestDeviceStatus  status,
//       WGPUDevice               device,
//       WGPUStringView           message,
//       void*                    userdata1,
//       void*                    userdata2);
//
// Callback info v29:
//   { next_in_chain, mode, callback, userdata1, userdata2 }
//
// We use the synchronous spin-on-ProcessEvents pattern instead of real Tasks
// because wgpu-native drives the callbacks from wgpuInstanceProcessEvents on
// the same thread. Wrapping in a TaskCompletionSource buys us nothing here
// and forces extra managed allocations. The callback's message is only valid
// during the callback, so it is copied into the request state.
//
// The state lives in native memory owned jointly by the waiter and the callback
// (see CallbackState), not on the waiter's stack: a wait can time out, and wgpu
// still delivers the callback later (at the latest when the instance, adapter or
// buffer is released), which would then write into a dead stack frame.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>Outcome of <see cref="AsyncRequest.RequestAdapterSync"/>.</summary>
public readonly struct RequestAdapterResult
{
    public RequestAdapterResult(RequestAdapterStatus status, Adapter adapter, string? message)
    {
        Status = status;
        Adapter = adapter;
        Message = message;
    }

    public RequestAdapterStatus Status { get; }

    public Adapter Adapter { get; }

    /// <summary>wgpu's explanation when the request did not succeed; null otherwise or on timeout.</summary>
    public string? Message { get; }

    public void Deconstruct(out RequestAdapterStatus status, out Adapter adapter)
    {
        status = Status;
        adapter = Adapter;
    }
}

/// <summary>Outcome of <see cref="AsyncRequest.RequestDeviceSync(Instance, Adapter, int)"/> and its overloads.</summary>
public readonly struct RequestDeviceResult
{
    public RequestDeviceResult(RequestDeviceStatus status, Device device, string? message)
    {
        Status = status;
        Device = device;
        Message = message;
    }

    public RequestDeviceStatus Status { get; }

    public Device Device { get; }

    /// <summary>wgpu's explanation when the request did not succeed; null otherwise or on timeout.</summary>
    public string? Message { get; }

    public void Deconstruct(out RequestDeviceStatus status, out Device device)
    {
        status = Status;
        device = Device;
    }
}

public static unsafe class AsyncRequest
{
    private const int MessageCapacity = 512;

    /// <summary>
    /// Requests an adapter. With <see cref="PowerPreference.Undefined"/> (the default) the platform's
    /// own adapter order is kept (on Windows, DXGI's: normally the GPU driving the primary display,
    /// honouring the user's per-app graphics preference), except that a hardware adapter always
    /// beats a software one. HighPerformance would instead wake a discrete GPU on hybrid laptops
    /// even for a light UI.
    /// </summary>
    /// <param name="instance">Instance whose backends are searched.</param>
    /// <param name="compatibleSurface">When set, only adapters that can present to it are considered.</param>
    /// <param name="preference">Power preference; leave Undefined to keep the platform's order.</param>
    /// <param name="backendType">Restrict to one backend; Undefined searches every enabled one.</param>
    /// <param name="timeoutMilliseconds">Wall-clock limit for the request.</param>
    /// <param name="forceFallbackAdapter">
    /// Ask for the software adapter (WARP on D3D12). Useful when no hardware adapter exists, e.g. in
    /// VMs or over remote desktop.
    /// </param>
    public static RequestAdapterResult RequestAdapterSync(
        Instance instance,
        Surface? compatibleSurface = null,
        PowerPreference preference = PowerPreference.Undefined,
        BackendType backendType = BackendType.Undefined,
        int timeoutMilliseconds = 5_000,
        bool forceFallbackAdapter = false)
    {
        if (instance.IsInvalid)
        {
            return new RequestAdapterResult(RequestAdapterStatus.Error, default, "Instance is invalid.");
        }

        RequestState* state = CallbackState.Allocate<RequestState>();
        state->StatusValue = (uint)RequestAdapterStatus.Error;

        AdapterOptions options = default;
        options.NextInChain = IntPtr.Zero;
        options.FeatureLevel = (uint)FeatureLevel.Undefined;
        options.PowerPreference = (uint)preference;
        options.ForceFallbackAdapter = forceFallbackAdapter ? 1u : 0u;
        options.BackendType = (uint)backendType;
        options.CompatibleSurface = compatibleSurface.HasValue ? compatibleSurface.Value.Handle : IntPtr.Zero;

        var callbackInfo = new WGPURequestAdapterCallbackInfo
        {
            Mode = (uint)CallbackMode.AllowProcessEvents,
            Callback = (IntPtr)(delegate* unmanaged[Cdecl]<uint, AdapterHandle, StringViewRaw, void*, void*, void>)&AdapterCallback,
            Userdata1 = state,
        };

        WebGPU.InstanceRequestAdapter(instance.Handle, (nint)(&options), callbackInfo);

        if (!WaitForCompletion(instance, state, timeoutMilliseconds))
        {
            CallbackState.Release(state);
            return new RequestAdapterResult(RequestAdapterStatus.Error, default, $"Timed out after {timeoutMilliseconds} ms.");
        }

        var result = new RequestAdapterResult(
            (RequestAdapterStatus)state->StatusValue,
            new Adapter(new AdapterHandle(state->Handle)),
            state->ReadMessage());
        CallbackState.Release(state);

        // With no preference wgpu keeps the platform order verbatim, and that order can start with
        // the software adapter (DXGI lists WARP first in some remote sessions). A GPU always wins
        // over software when one can do the job.
        if (preference == PowerPreference.Undefined
            && !forceFallbackAdapter
            && result.Status == RequestAdapterStatus.Success
            && result.Adapter.GetDescription().IsSoftware)
        {
            Adapter hardware = FirstHardwareAdapter(instance, compatibleSurface, backendType);
            if (!hardware.IsInvalid)
            {
                result.Adapter.Dispose();
                return new RequestAdapterResult(RequestAdapterStatus.Success, hardware, null);
            }
        }

        return result;
    }

    // First non-software adapter in enumeration order that matches the backend filter and can
    // present to the surface; every other enumerated adapter is released.
    private static Adapter FirstHardwareAdapter(Instance instance, Surface? compatibleSurface, BackendType backendType)
    {
        Adapter chosen = default;
        foreach (Adapter candidate in instance.EnumerateAdapters())
        {
            if (chosen.IsInvalid && IsUsableHardware(candidate, compatibleSurface, backendType))
            {
                chosen = candidate;
                continue;
            }
            candidate.Dispose();
        }
        return chosen;
    }

    private static bool IsUsableHardware(Adapter adapter, Surface? compatibleSurface, BackendType backendType)
    {
        AdapterDescription description = adapter.GetDescription();
        if (description.IsSoftware)
        {
            return false;
        }
        if (backendType != BackendType.Undefined && description.Backend != backendType)
        {
            return false;
        }
        return !compatibleSurface.HasValue || adapter.CanPresentTo(compatibleSurface.Value);
    }

    /// <summary>Requests a device with <see cref="DeviceOptions.Default"/>.</summary>
    public static RequestDeviceResult RequestDeviceSync(
        Instance instance,
        Adapter adapter,
        int timeoutMilliseconds = 5_000)
    {
        return RequestDeviceSync(instance, adapter, null, DeviceOptions.Default, timeoutMilliseconds);
    }

    /// <summary>Requests a device from <paramref name="descriptor"/> with <see cref="DeviceOptions.Default"/>.</summary>
    public static RequestDeviceResult RequestDeviceSync(
        Instance instance,
        Adapter adapter,
        DeviceDescriptor* descriptor,
        int timeoutMilliseconds = 5_000)
    {
        return RequestDeviceSync(instance, adapter, descriptor, DeviceOptions.Default, timeoutMilliseconds);
    }

    /// <summary>
    /// Requests a device. <paramref name="options"/> are applied by prepending a WGPUDeviceExtras to
    /// the descriptor's chain and, when a binding cap is set, a WGPUNativeLimits to its required
    /// limits (the caller's limits are kept; an absent limits struct means "wgpu defaults").
    /// The caller's descriptor is copied, never modified.
    /// </summary>
    public static RequestDeviceResult RequestDeviceSync(
        Instance instance,
        Adapter adapter,
        DeviceDescriptor* descriptor,
        DeviceOptions options,
        int timeoutMilliseconds = 5_000)
    {
        if (adapter.IsInvalid)
        {
            return new RequestDeviceResult(RequestDeviceStatus.Error, default, "Adapter is invalid.");
        }

        DeviceDescriptor effective = descriptor != null ? *descriptor : default;

        WGPUDeviceExtras extras = default;
        extras.Chain.SType = WGPUSType.DeviceExtras;
        extras.Chain.Next = (WGPUChainedStruct*)effective.NextInChain;
        extras.MemoryHints = (WGPUMemoryHints)options.MemoryHints;
        effective.NextInChain = (IntPtr)(&extras);

        WGPULimits limits = effective.RequiredLimits != IntPtr.Zero
            ? *(WGPULimits*)effective.RequiredLimits
            : WGPULimits.Undefined();
        WGPUNativeLimits nativeLimits = WGPUNativeLimits.Undefined();
        uint bindings = ClampNonSamplerBindings(adapter, options.MaxNonSamplerBindings);
        if (bindings != 0)
        {
            nativeLimits.MaxNonSamplerBindings = bindings;
            nativeLimits.Chain.Next = limits.NextInChain;
            limits.NextInChain = &nativeLimits.Chain;
            effective.RequiredLimits = (IntPtr)(&limits);
        }

        RequestState* state = CallbackState.Allocate<RequestState>();
        state->StatusValue = (uint)RequestDeviceStatus.Error;

        var callbackInfo = new WGPURequestDeviceCallbackInfo
        {
            Mode = (uint)CallbackMode.AllowProcessEvents,
            Callback = (IntPtr)(delegate* unmanaged[Cdecl]<uint, DeviceHandle, StringViewRaw, void*, void*, void>)&DeviceCallback,
            Userdata1 = state,
        };

        // Creating the device creates its D3D12 queue, which WARP's JIT state spans as well.
        WarpSerialization.ActivateFor(adapter.GetDescription());
        using (WarpSerialization.Enter())
        {
            WebGPU.AdapterRequestDevice(adapter.Handle, (nint)(&effective), callbackInfo);
        }

        if (!WaitForCompletion(instance, state, timeoutMilliseconds))
        {
            CallbackState.Release(state);
            return new RequestDeviceResult(RequestDeviceStatus.Error, default, $"Timed out after {timeoutMilliseconds} ms.");
        }

        var result = new RequestDeviceResult(
            (RequestDeviceStatus)state->StatusValue,
            new Device(new DeviceHandle(state->Handle)),
            state->ReadMessage());
        CallbackState.Release(state);
        return result;
    }

    // 0 means "leave wgpu's default". Otherwise never ask for more than the adapter offers, or
    // device creation fails validation on backends that report a lower ceiling.
    private static uint ClampNonSamplerBindings(Adapter adapter, uint requested)
    {
        if (requested == 0)
        {
            return 0;
        }

        WGPUNativeLimits supported = default;
        supported.Chain.SType = WGPUSType.NativeLimits;
        WGPULimits limits = default;
        limits.NextInChain = &supported.Chain;
        if (WebGPU.AdapterGetLimits(adapter.Handle, (nint)(&limits)) != 1 || supported.MaxNonSamplerBindings == 0)
        {
            return 0;
        }

        return Math.Min(requested, supported.MaxNonSamplerBindings);
    }

    // Callbacks fire from InstanceProcessEvents, so check before sleeping: a request that completes
    // on the first pump costs no sleep at all. The deadline is wall-clock, not an iteration count,
    // because Thread.Sleep(1) can take a full scheduler tick (~15.6 ms on Windows).
    private static bool WaitForCompletion(Instance instance, RequestState* state, int timeoutMilliseconds)
    {
        long deadline = Stopwatch.GetTimestamp() + (long)timeoutMilliseconds * Stopwatch.Frequency / 1000;
        while (true)
        {
            // wgpu-native polls every device of the instance here, which can retire submissions.
            using (WarpSerialization.Enter())
            {
                WebGPU.InstanceProcessEvents(instance.Handle);
            }
            if (Volatile.Read(ref state->Completed) != 0)
            {
                return true;
            }
            if (Stopwatch.GetTimestamp() >= deadline)
            {
                return false;
            }
            Thread.Sleep(1);
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void AdapterCallback(uint status, AdapterHandle adapter, StringViewRaw message, void* userdata1, void* userdata2)
    {
        RequestState* state = (RequestState*)userdata1;
        if (state == null)
        {
            return;
        }
        state->StatusValue = status;
        state->Handle = (nint)adapter;
        state->CopyMessage(message);
        Volatile.Write(ref state->Completed, 1);
        CallbackState.Release(state);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void DeviceCallback(uint status, DeviceHandle device, StringViewRaw message, void* userdata1, void* userdata2)
    {
        RequestState* state = (RequestState*)userdata1;
        if (state == null)
        {
            return;
        }
        state->StatusValue = status;
        state->Handle = (nint)device;
        state->CopyMessage(message);
        Volatile.Write(ref state->Completed, 1);
        CallbackState.Release(state);
    }

    // Raw StringView used only in unmanaged callback signatures. Matches
    // WGPUStringView layout byte-for-byte (pointer + nuint length).
    [StructLayout(LayoutKind.Sequential)]
    private struct StringViewRaw
    {
        public IntPtr Data;
        public UIntPtr Length;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RequestState
    {
        public uint StatusValue;
        public uint Completed;
        public nint Handle;
        public int MessageLength;
        public fixed byte Message[MessageCapacity];

        public void CopyMessage(StringViewRaw message)
        {
            if (message.Data == IntPtr.Zero || message.Length == 0)
            {
                MessageLength = 0;
                return;
            }

            // WGPU_STRLEN (SIZE_MAX) marks a null-terminated string.
            int length = message.Length == UIntPtr.MaxValue
                ? MemoryMarshal.CreateReadOnlySpanFromNullTerminated((byte*)message.Data).Length
                : (int)Math.Min((ulong)message.Length, int.MaxValue);
            length = Math.Min(length, MessageCapacity);
            fixed (byte* destination = Message)
            {
                new ReadOnlySpan<byte>((void*)message.Data, length).CopyTo(new Span<byte>(destination, MessageCapacity));
            }
            MessageLength = length;
        }

        public string? ReadMessage()
        {
            if (MessageLength == 0)
            {
                return null;
            }
            fixed (byte* source = Message)
            {
                return Encoding.UTF8.GetString(source, MessageLength);
            }
        }
    }
}

/// <summary>
/// Native memory shared by a waiter and the wgpu callback that completes it. Each side calls
/// <see cref="Release{T}"/> exactly once and the second one frees the block, so a waiter that
/// gives up (timeout) can return while the callback is still pending without leaving it a dangling
/// pointer. A callback that never fires leaks the block; wgpu guarantees one (with an
/// instance-dropped or aborted status when nothing else).
/// </summary>
internal static unsafe class CallbackState
{
    // The reference count sits in front of the payload, padded to keep the payload 16-byte aligned.
    private const int HeaderSize = 16;

    /// <summary>Allocates a zeroed <typeparamref name="T"/> owned by two parties: the waiter and the callback.</summary>
    public static T* Allocate<T>() where T : unmanaged
    {
        byte* block = (byte*)NativeMemory.AlignedAlloc((nuint)(HeaderSize + sizeof(T)), HeaderSize);
        NativeMemory.Clear(block, (nuint)(HeaderSize + sizeof(T)));
        *(int*)block = 2;
        return (T*)(block + HeaderSize);
    }

    /// <summary>Drops one party's ownership; frees the block when it was the last one.</summary>
    public static void Release<T>(T* state) where T : unmanaged
    {
        byte* block = (byte*)state - HeaderSize;
        if (Interlocked.Decrement(ref *(int*)block) == 0)
        {
            NativeMemory.AlignedFree(block);
        }
    }
}
