using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Etch.Gpu.Descriptors;
using Etch.Gpu.Diagnostics;
using Etch.Gpu.Native;

namespace Etch.Gpu.Validation;

// Values are WGPUErrorType / WGPUErrorFilter (webgpu.h v29): PopErrorScope reports the native
// value, which is cast straight to GpuErrorType.
public enum GpuErrorType : uint
{
    NoError = 1,
    Validation = 2,
    OutOfMemory = 3,
    Internal = 4,
    Unknown = 5,
}

public enum ErrorFilter : uint
{
    Validation = 1,
    OutOfMemory = 2,
    Internal = 3,
}

public readonly struct GpuError
{
    public GpuErrorType Type { get; }
    public string? Message { get; }

    public GpuError(GpuErrorType type, string? message)
    {
        Type = type;
        Message = message;
    }
}

// WGPUStringView. wgpu callbacks receive it BY VALUE: on Windows x64 a 16-byte struct travels as a
// hidden pointer, which is why a pointer parameter happened to work there, but SysV x64 and AArch64
// pass it in two registers, so declaring it as a pointer shifts every later argument by one.
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct StringViewNative
{
    public IntPtr Data;
    public UIntPtr Length;

    // WGPU_STRLEN (SIZE_MAX) marks a null-terminated string. Casting it to int gave -1, which made
    // the span constructor throw inside an UnmanagedCallersOnly callback: a process crash.
    public readonly ReadOnlySpan<byte> AsSpan()
    {
        if (Data == IntPtr.Zero || Length == 0)
        {
            return ReadOnlySpan<byte>.Empty;
        }
        if (Length == UIntPtr.MaxValue)
        {
            return MemoryMarshal.CreateReadOnlySpanFromNullTerminated((byte*)Data);
        }
        return new ReadOnlySpan<byte>((void*)Data, (int)Math.Min((ulong)Length, int.MaxValue));
    }
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PopErrorScopeState
{
    // wgpu owns the message only for the duration of the callback, so it is copied in here.
    public const int MessageCapacity = 1024;

    public uint Completed;
    public uint Status;
    public uint ErrorType;
    public int MessageLength;
    public fixed byte Message[MessageCapacity];
}

public sealed class ErrorScope : IDisposable
{
    private readonly Device _device;
    private bool _disposed;
    private GpuError? _capturedError;

    public ErrorScope(Device device, ErrorFilter filter)
    {
        _device = device;
        WebGPU.DevicePushErrorScope(device.Handle, (WGPUErrorFilter)filter);
    }

    public unsafe GpuError? Pop(Instance instance, int timeoutMs = 5000)
    {
        if (_disposed)
            return null;

        if (_capturedError.HasValue)
            return _capturedError;

        // Shared with the callback rather than on this stack: after a timeout wgpu still fires the
        // callback later, at the latest when the device is released.
        PopErrorScopeState* state = CallbackState.Allocate<PopErrorScopeState>();

        var callbackInfo = new WGPUPopErrorScopeCallbackInfo
        {
            NextInChain = null,
            Mode = (uint)CallbackMode.AllowProcessEvents,
            Callback = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint, StringViewNative, IntPtr, IntPtr, void>)&PopErrorScopeCallback,
            Userdata1 = state,
            Userdata2 = null
        };

        WebGPU.DevicePopErrorScope(_device.Handle, callbackInfo);

        // The callback usually fires on the first pump, so check before sleeping; the deadline is
        // wall-clock because Thread.Sleep(1) can take a whole scheduler tick.
        long deadline = System.Diagnostics.Stopwatch.GetTimestamp() + (long)timeoutMs * System.Diagnostics.Stopwatch.Frequency / 1000;
        while (true)
        {
            // wgpu-native polls every device of the instance here, which can retire submissions.
            using (WarpSerialization.Enter())
            {
                WebGPU.InstanceProcessEvents(instance.Handle);
            }
            if (System.Threading.Volatile.Read(ref state->Completed) != 0 || System.Diagnostics.Stopwatch.GetTimestamp() >= deadline)
            {
                break;
            }
            Thread.Sleep(1);
        }

        if (System.Threading.Volatile.Read(ref state->Completed) == 0)
        {
            _capturedError = new GpuError(GpuErrorType.Unknown, "PopErrorScope timed out");
        }
        else
        {
            // On Success the message is the captured error's text (empty for NoError); otherwise
            // it says why the pop failed.
            string? message = null;
            if (state->MessageLength > 0)
            {
                message = System.Text.Encoding.UTF8.GetString(state->Message, state->MessageLength);
            }
            _capturedError = new GpuError((GpuErrorType)state->ErrorType, message);
        }

        CallbackState.Release(state);
        return _capturedError;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe void PopErrorScopeCallback(
        uint status,
        uint type,
        StringViewNative message,
        IntPtr userdata1,
        IntPtr userdata2)
    {
        PopErrorScopeState* state = (PopErrorScopeState*)(userdata1.ToPointer());
        if (state == null)
            return;

        state->Status = status;
        state->ErrorType = type;
        ReadOnlySpan<byte> text = message.AsSpan();
        int length = Math.Min(text.Length, PopErrorScopeState.MessageCapacity);
        text.Slice(0, length).CopyTo(new Span<byte>(state->Message, PopErrorScopeState.MessageCapacity));
        state->MessageLength = length;
        System.Threading.Volatile.Write(ref state->Completed, 1u);
        CallbackState.Release(state);
    }
}

/// <summary>
/// Bridges wgpu-native validation messages into Etch's error infrastructure.
/// In v29 the uncaptured-error callback is set at device-creation time via
/// <see cref="DeviceDescriptor.UncapturedErrorCallbackInfo"/>; there is no
/// separate <c>wgpuDeviceSetUncapturedErrorCallback</c> setter.
/// </summary>
public static class ValidationBridge
{
    private static readonly ValidationLogRing s_ring = new ValidationLogRing();
    private static IEtchLogger? s_logger;
    // Two separate counts: how many errors the wgpu callback delivered, and how many ring writes
    // have been reported or acknowledged. They used to be one counter that the callback also bumped,
    // which marked every real error as already acknowledged, so ThrowIfValidationErrorsPresent
    // only ever saw entries pushed straight into the ring.
    private static long s_deliveredCount;
    private static long s_acknowledgedWrites;

    /// <summary>
    /// The ring that receives every uncaptured validation message. Thread-safe
    /// for a single producer (wgpu callback thread) and any consumer.
    /// </summary>
    public static ValidationLogRing Ring => s_ring;

    /// <summary>
    /// Total number of uncaptured errors the wgpu callback has delivered to the ring (entries pushed
    /// directly with <see cref="ValidationLogRing.Push"/> are not counted).
    /// </summary>
    public static long TotalDelivered => Interlocked.Read(ref s_deliveredCount);

    /// <summary>
    /// Marks all current ring entries as acknowledged so that
    /// <see cref="ThrowIfValidationErrorsPresent"/> reports only later ones. Does not change
    /// <see cref="TotalDelivered"/>. Use in test setup/cleanup to avoid cross-test interference on
    /// the shared <see cref="Ring"/>.
    /// </summary>
    public static void AcknowledgeAll()
    {
        long ringWrites = s_ring.TotalWrites;
        Interlocked.Exchange(ref s_acknowledgedWrites, ringWrites);
    }

    /// <summary>
    /// Attaches the logger used in Release builds. Debug builds panic instead.
    /// </summary>
    public static void SetLogger(IEtchLogger? logger)
    {
        s_logger = logger;
    }

    /// <summary>
    /// Fills <paramref name="descriptor"/> so that it routes uncaptured errors
    /// through <see cref="EtchValidationCallback"/>.
    /// Call this before passing the descriptor to <c>wgpuAdapterRequestDevice</c>.
    /// </summary>
    public static unsafe void ConfigureDeviceDescriptor(DeviceDescriptor* descriptor)
    {
        descriptor->UncapturedErrorCallbackInfo = new UncapturedErrorCallbackInfo
        {
            NextInChain = IntPtr.Zero,
            Callback = (IntPtr)(delegate* unmanaged[Cdecl]<void*, uint, StringViewNative, void*, void*, void>)&EtchValidationCallback,
            Userdata1 = IntPtr.Zero,
            Userdata2 = IntPtr.Zero
        };
    }

    /// <summary>
    /// Throws <see cref="EtchException"/> with code <c>ET-P-0201 GpuValidation</c>
    /// if the ring contains any entries written since the last call to this method.
    /// Returns the number of new errors found (zero on success).
    /// </summary>
    /// <remarks>
    /// Use this after <c>Queue.Submit</c> in strict-validation mode (tests).
    /// </remarks>
    public static int ThrowIfValidationErrorsPresent(string context)
    {
        long total = Interlocked.Read(ref s_acknowledgedWrites);
        long ringWrites = s_ring.TotalWrites;
        long newErrors = ringWrites - total;

        if (newErrors <= 0)
            return 0;

        // Advance the counter so subsequent calls don't re-report the same errors.
        Interlocked.Add(ref s_acknowledgedWrites, newErrors);

        byte[] blob = s_ring.Snapshot();
        if (ValidationLogRing.TryDecode(blob, out var snapshot) && snapshot.Count > 0)
        {
            var last = snapshot[snapshot.Count - 1];
            string msg = $"GPU validation error in '{context}': {last.Message}";
            Etch.Panic.Invariant(Etch.PanicCodes.GpuValidation, msg);
        }

        return (int)newErrors;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe void EtchValidationCallback(
        void* device,
        uint errorType,
        StringViewNative message,
        void* userdata1,
        void* userdata2)
    {
        // message is a WGPUStringView passed by value (see StringViewNative).
        ReadOnlySpan<byte> utf8 = message.AsSpan();

        s_ring.Push((ErrorType)errorType, utf8, Stopwatch.GetTimestamp());
        Interlocked.Increment(ref s_deliveredCount);

#if DEBUG
        if (utf8.Length > 0)
        {
            string? msg = System.Text.Encoding.UTF8.GetString(utf8);
            Console.Error.WriteLine("wgpu validation: " + msg);
        }
#endif
    }
}
