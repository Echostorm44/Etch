using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Etch.Gpu.Descriptors;

namespace Etch.Gpu.Validation;

/// <summary>
/// Reports when a device is lost — a driver reset or removal, or <see cref="Device.Destroy"/> —
/// so its owner can stop using it and fall back. Attach one to a device descriptor before the
/// device is requested (<see cref="Attach"/>); dispose it before releasing the device, so the
/// release itself is not reported.
/// </summary>
public sealed class DeviceLossWatch : IDisposable
{
    private static readonly ConcurrentDictionary<long, DeviceLossWatch> Watches = new();
    private static long nextId;

    private readonly long id;
    private int reason;

    private DeviceLossWatch(long id)
    {
        this.id = id;
    }

    /// <summary>True once the device was lost.</summary>
    public bool IsLost => Volatile.Read(ref reason) != 0;

    /// <summary>Why the device was lost, or null while it is not.</summary>
    public DeviceLostReason? Reason => Volatile.Read(ref reason) is var r && r != 0 ? (DeviceLostReason)r : null;

    /// <summary>
    /// Creates a watch and sets <paramref name="descriptor"/>'s device-lost callback to report to
    /// it. The callback may run on any thread, at any time.
    /// </summary>
    public static unsafe DeviceLossWatch Attach(DeviceDescriptor* descriptor)
    {
        var watch = new DeviceLossWatch(Interlocked.Increment(ref nextId));
        Watches[watch.id] = watch;
        descriptor->DeviceLostCallbackInfo = new DeviceLostCallbackInfo
        {
            NextInChain = IntPtr.Zero,
            Mode = (uint)CallbackMode.AllowSpontaneous,
            Callback = (IntPtr)(delegate* unmanaged[Cdecl]<void*, uint, StringViewNative, void*, void*, void>)&OnDeviceLost,
            Userdata1 = checked((IntPtr)watch.id),
            Userdata2 = IntPtr.Zero,
        };
        return watch;
    }

    /// <summary>Stops reporting (call before releasing the device).</summary>
    public void Dispose() => Watches.TryRemove(id, out _);

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe void OnDeviceLost(void* device, uint lostReason, StringViewNative message, void* userdata1, void* userdata2)
    {
        // Nothing here may throw: an exception out of an UnmanagedCallersOnly callback ends the process.
        if (lostReason == (uint)DeviceLostReason.CallbackCancelled)
        {
            return;
        }
        if (Watches.TryGetValue((long)(nint)userdata1, out var watch))
        {
            Interlocked.CompareExchange(ref watch.reason, (int)Math.Max(1u, lostReason), 0);
        }
    }
}