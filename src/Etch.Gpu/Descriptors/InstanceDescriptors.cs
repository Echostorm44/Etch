using System;
using System.Runtime.InteropServices;
using Etch.Gpu.Native;

namespace Etch.Gpu.Descriptors;

// ═══════════════════════════════════════════════════════════════════════════
// Small shared interop types. Layout-identical to WGPUStringView /
// WGPUChainedStruct in Etch.Gpu.Native; duplicated here so the user-facing
// descriptor namespace has ergonomic names.
// ═══════════════════════════════════════════════════════════════════════════

[StructLayout(LayoutKind.Sequential)]
public struct StringView
{
    public IntPtr Data;
    public UIntPtr Length;
}

[StructLayout(LayoutKind.Sequential)]
public struct ChainedStruct
{
    public IntPtr NextInChain;
    public uint SType;
}

// ═══════════════════════════════════════════════════════════════════════════
// Instance / Adapter / Device descriptors (v29).
// ═══════════════════════════════════════════════════════════════════════════

// wgpuCreateInstance takes an optional WGPUInstanceDescriptor*. Field
// ordering and contents match webgpu.h v29. `Features` and `Capabilities`
// are pointer-to-sub-struct in v29 but we only need a zeroed descriptor
// for the red triangle path, so we don't model the sub-structs yet.
[StructLayout(LayoutKind.Sequential)]
public struct InstanceDescriptor
{
    public IntPtr NextInChain;           // WGPUChainedStruct const*
    public InstanceCapabilities Capabilities;
}

// Matches WGPUInstanceCapabilities (webgpu.h v29).
[StructLayout(LayoutKind.Sequential)]
public struct InstanceCapabilities
{
    public IntPtr NextInChain;
    public uint TimedWaitAnyEnable;      // WGPUBool
    public UIntPtr TimedWaitAnyMaxCount;
}

// WGPURequestAdapterOptions (webgpu.h v29).
//   NextInChain, FeatureLevel, PowerPreference, ForceFallbackAdapter,
//   BackendType, CompatibleSurface.
// BackendType uses WGPUBackendType enum; InstanceBackend bit-flags go
// through a chained InstanceExtras struct (not modelled here yet).
[StructLayout(LayoutKind.Sequential)]
public struct AdapterOptions
{
    public IntPtr NextInChain;
    public uint FeatureLevel;            // WGPUFeatureLevel
    public uint PowerPreference;         // WGPUPowerPreference
    public uint ForceFallbackAdapter;    // WGPUBool
    public uint BackendType;             // WGPUBackendType
    public IntPtr CompatibleSurface;     // WGPUSurface
}

// WGPUQueueDescriptor { NextInChain, Label }.
[StructLayout(LayoutKind.Sequential)]
public struct QueueDescriptor
{
    public IntPtr NextInChain;
    public StringView Label;
}

// WGPUDeviceLostCallbackInfo { NextInChain, Mode, Callback, Userdata1, Userdata2 }.
[StructLayout(LayoutKind.Sequential)]
public struct DeviceLostCallbackInfo
{
    public IntPtr NextInChain;
    public uint Mode;                    // WGPUCallbackMode
    public IntPtr Callback;
    public IntPtr Userdata1;
    public IntPtr Userdata2;
}

// WGPUUncapturedErrorCallbackInfo { NextInChain, Callback, Userdata1, Userdata2 }.
// No Mode field (error callbacks are spontaneous).
[StructLayout(LayoutKind.Sequential)]
public struct UncapturedErrorCallbackInfo
{
    public IntPtr NextInChain;
    public IntPtr Callback;
    public IntPtr Userdata1;
    public IntPtr Userdata2;
}

// WGPUDeviceDescriptor v29:
//   NextInChain, Label, RequiredFeatureCount, RequiredFeatures,
//   RequiredLimits*, DefaultQueue (by value!), DeviceLostCallbackInfo (by value!),
//   UncapturedErrorCallbackInfo (by value!).
[StructLayout(LayoutKind.Sequential)]
public struct DeviceDescriptor
{
    public IntPtr NextInChain;
    public StringView Label;
    public UIntPtr RequiredFeatureCount;
    public IntPtr RequiredFeatures;      // WGPUFeatureName const*
    public IntPtr RequiredLimits;        // WGPULimits const*  (single, not array)
    public QueueDescriptor DefaultQueue;
    public DeviceLostCallbackInfo DeviceLostCallbackInfo;
    public UncapturedErrorCallbackInfo UncapturedErrorCallbackInfo;
}

// ═══════════════════════════════════════════════════════════════════════════
// Callback info wrappers used when requesting adapters / devices (v29).
// ═══════════════════════════════════════════════════════════════════════════

// WGPURequestAdapterCallbackInfo { NextInChain, Mode, Callback, Userdata1, Userdata2 }.
[StructLayout(LayoutKind.Sequential)]
public struct RequestAdapterCallbackInfo
{
    public IntPtr NextInChain;
    public uint Mode;                    // WGPUCallbackMode
    public IntPtr Callback;              // void(*)(status, adapter, message, ud1, ud2)
    public IntPtr Userdata1;
    public IntPtr Userdata2;
}

// WGPURequestDeviceCallbackInfo { NextInChain, Mode, Callback, Userdata1, Userdata2 }.
[StructLayout(LayoutKind.Sequential)]
public struct RequestDeviceCallbackInfo
{
    public IntPtr NextInChain;
    public uint Mode;
    public IntPtr Callback;              // void(*)(status, device, message, ud1, ud2)
    public IntPtr Userdata1;
    public IntPtr Userdata2;
}

// ═══════════════════════════════════════════════════════════════════════════
// Buffer descriptor (v29). Label is a StringView; Usage is u64.
// ═══════════════════════════════════════════════════════════════════════════

[StructLayout(LayoutKind.Sequential)]
public struct BufferDescriptor
{
    public IntPtr NextInChain;
    public StringView Label;
    public ulong Usage;                  // WGPUBufferUsage (WGPUFlags = u64)
    public ulong Size;
    public uint MappedAtCreation;        // WGPUBool
}
