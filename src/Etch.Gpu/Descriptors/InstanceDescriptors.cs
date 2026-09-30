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

// WGPUInstanceDescriptor (webgpu.h). Backend selection and flags ride in a chained
// WGPUInstanceExtras; Instance.Create(InstanceOptions) builds that chain.
[StructLayout(LayoutKind.Sequential)]
public struct InstanceDescriptor
{
    public IntPtr NextInChain;           // WGPUChainedStruct const*
    public UIntPtr RequiredFeatureCount;
    public IntPtr RequiredFeatures;      // WGPUInstanceFeatureName const*
    public IntPtr RequiredLimits;        // WGPUInstanceLimits const* (nullable)
}

/// <summary>
/// What an <see cref="Instance"/> is created with. <see cref="Default"/> enables only the
/// platform's primary backend, so an instance never loads driver stacks it will not use.
/// </summary>
public struct InstanceOptions
{
    /// <summary>
    /// Backends wgpu may enumerate. <see cref="InstanceBackend.All"/> loads every available
    /// driver stack (Vulkan, GL and D3D12 for every GPU on Windows), which costs tens of
    /// megabytes and start-up time; use it only to compare backends.
    /// </summary>
    public InstanceBackend Backends { get; set; }

    /// <summary>Debug/validation flags. <see cref="InstanceFlag.Default"/> is wgpu's own default.</summary>
    public InstanceFlag Flags { get; set; }

    /// <summary>The platform's primary backend and wgpu's default flags.</summary>
    public static InstanceOptions Default => new() { Backends = PlatformBackends, Flags = InstanceFlag.Default };

    /// <summary>Every backend wgpu supports on this platform.</summary>
    public static InstanceOptions AllBackends => new() { Backends = InstanceBackend.All, Flags = InstanceFlag.Default };

    /// <summary>
    /// D3D12 on Windows (every Windows 10+ machine has it, including the WARP software adapter),
    /// Metal on macOS, and Vulkan with GL as the fallback on Linux.
    /// </summary>
    public static InstanceBackend PlatformBackends
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                return InstanceBackend.DX12;
            }
            if (OperatingSystem.IsMacOS())
            {
                return InstanceBackend.Metal;
            }
            return InstanceBackend.Vulkan | InstanceBackend.GL;
        }
    }
}

/// <summary>
/// What a <see cref="Device"/> is requested with, on top of the caller's
/// <see cref="DeviceDescriptor"/>. <see cref="Default"/> suits a UI renderer: GPU memory grows
/// with content instead of being committed up front.
/// </summary>
public struct DeviceOptions
{
    /// <summary>
    /// Default cap on live non-sampler bindings. Only D3D12 sizes a shader-visible descriptor heap
    /// from it (32–64 bytes per entry). wgpu's own default of 1,000,000 commits 30–60 MB for a heap a
    /// UI never approaches: Cascade keeps one bind group per unique image plus a handful per frame.
    /// </summary>
    public const uint DefaultMaxNonSamplerBindings = 65_536;

    /// <summary>Allocator block-size strategy.</summary>
    public MemoryHints MemoryHints { get; set; }

    /// <summary>
    /// Cap on live non-sampler bindings, clamped to what the adapter supports.
    /// 0 keeps wgpu's default (1,000,000 on D3D12).
    /// </summary>
    public uint MaxNonSamplerBindings { get; set; }

    /// <summary>Memory-lean allocator blocks and a UI-sized descriptor heap.</summary>
    public static DeviceOptions Default => new()
    {
        MemoryHints = MemoryHints.MemoryUsage,
        MaxNonSamplerBindings = DefaultMaxNonSamplerBindings,
    };

    /// <summary>wgpu's own defaults, for comparison and for content that streams very large resources.</summary>
    public static DeviceOptions WgpuDefaults => new() { MemoryHints = MemoryHints.Performance, MaxNonSamplerBindings = 0 };
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
