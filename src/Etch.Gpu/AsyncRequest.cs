using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
// and forces extra allocations; a stack-scoped spinner is both simpler and
// zero-alloc on the hot path.
// ═══════════════════════════════════════════════════════════════════════════

public static unsafe class AsyncRequest
{
    public static (RequestAdapterStatus Status, Adapter Adapter) RequestAdapterSync(
        Instance instance,
        Surface? compatibleSurface = null,
        PowerPreference preference = PowerPreference.HighPerformance,
        BackendType backendType = BackendType.Undefined,
        int timeoutMilliseconds = 5_000)
    {
        if (instance.IsInvalid)
        {
            return (RequestAdapterStatus.Error, default);
        }

        AdapterRequestState state = default;
        state.StatusValue = (uint)RequestAdapterStatus.Error;

        AdapterOptions options = default;
        options.NextInChain = IntPtr.Zero;
        options.FeatureLevel = (uint)FeatureLevel.Undefined;
        options.PowerPreference = (uint)preference;
        options.ForceFallbackAdapter = 0;
        options.BackendType = (uint)backendType;
        options.CompatibleSurface = compatibleSurface.HasValue ? compatibleSurface.Value.Handle : IntPtr.Zero;

        RequestAdapterCallbackInfo callbackInfo = default;
        callbackInfo.NextInChain = IntPtr.Zero;
        callbackInfo.Mode = (uint)CallbackMode.AllowProcessEvents;
        callbackInfo.Callback = (IntPtr)(delegate* unmanaged[Cdecl]<uint, AdapterHandle, StringViewRaw, void*, void*, void>)&AdapterCallback;
        callbackInfo.Userdata1 = (IntPtr)(&state);
        callbackInfo.Userdata2 = IntPtr.Zero;

        WebGPU.InstanceRequestAdapter(instance.Handle, (nint)(&options), (nint)(&callbackInfo));

        int waitedMs = 0;
        while (state.Completed == 0 && waitedMs < timeoutMilliseconds)
        {
            WebGPU.InstanceProcessEvents(instance.Handle);
            Thread.Sleep(1);
            waitedMs++;
        }

        if (state.Completed == 0)
        {
            return (RequestAdapterStatus.Error, default);
        }

        return ((RequestAdapterStatus)state.StatusValue, new Adapter(new AdapterHandle(state.AdapterHandle)));
    }

    public static (RequestDeviceStatus Status, Device Device) RequestDeviceSync(
        Instance instance,
        Adapter adapter,
        int timeoutMilliseconds = 5_000)
    {
        return RequestDeviceSync(instance, adapter, null, timeoutMilliseconds);
    }

    public static (RequestDeviceStatus Status, Device Device) RequestDeviceSync(
        Instance instance,
        Adapter adapter,
        DeviceDescriptor* descriptor,
        int timeoutMilliseconds = 5_000)
    {
        if (adapter.IsInvalid)
        {
            return (RequestDeviceStatus.Error, default);
        }

        DeviceRequestState state = default;
        state.StatusValue = (uint)RequestDeviceStatus.Error;

        RequestDeviceCallbackInfo callbackInfo = default;
        callbackInfo.NextInChain = IntPtr.Zero;
        callbackInfo.Mode = (uint)CallbackMode.AllowProcessEvents;
        callbackInfo.Callback = (IntPtr)(delegate* unmanaged[Cdecl]<uint, DeviceHandle, StringViewRaw, void*, void*, void>)&DeviceCallback;
        callbackInfo.Userdata1 = (IntPtr)(&state);
        callbackInfo.Userdata2 = IntPtr.Zero;

        nint descPtr = descriptor != null ? (nint)descriptor : IntPtr.Zero;
        WebGPU.AdapterRequestDevice(adapter.Handle, descPtr, (nint)(&callbackInfo));

        int waitedMs = 0;
        while (state.Completed == 0 && waitedMs < timeoutMilliseconds)
        {
            WebGPU.InstanceProcessEvents(instance.Handle);
            Thread.Sleep(1);
            waitedMs++;
        }

        if (state.Completed == 0)
        {
            return (RequestDeviceStatus.Error, default);
        }

        return ((RequestDeviceStatus)state.StatusValue, new Device(new DeviceHandle(state.DeviceHandle)));
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void AdapterCallback(uint status, AdapterHandle adapter, StringViewRaw message, void* userdata1, void* userdata2)
    {
        AdapterRequestState* state = (AdapterRequestState*)userdata1;
        if (state == null)
        {
            return;
        }
        state->StatusValue = status;
        state->AdapterHandle = (nint)adapter;
        state->Completed = 1;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void DeviceCallback(uint status, DeviceHandle device, StringViewRaw message, void* userdata1, void* userdata2)
    {
        DeviceRequestState* state = (DeviceRequestState*)userdata1;
        if (state == null)
        {
            return;
        }
        state->StatusValue = status;
        state->DeviceHandle = (nint)device;
        state->Completed = 1;
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
    private struct AdapterRequestState
    {
        public uint StatusValue;
        public uint Completed;
        public nint AdapterHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceRequestState
    {
        public uint StatusValue;
        public uint Completed;
        public nint DeviceHandle;
    }
}
