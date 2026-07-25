using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Etch.Gpu.Descriptors;
using Etch.Gpu.Diagnostics;
using Etch.Gpu.Native;

namespace Etch.Gpu.Validation;

public enum GpuErrorType : uint
{
    NoError = 0,
    Validation = 1,
    OutOfMemory = 2,
    Unknown = 3,
    DeviceLost = 4,
}

public enum ErrorFilter : uint
{
    Validation = 1,
    OutOfMemory = 2,
    DeviceLost = 3,
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

[StructLayout(LayoutKind.Sequential)]
internal struct StringViewNative
{
    public IntPtr Data;
    public UIntPtr Length;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PopErrorScopeState
{
    public uint Completed;
    public uint Status;
    public uint ErrorType;
    public IntPtr ErrorMessage;
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

    public GpuError? Pop(Instance instance, int timeoutMs = 5000)
    {
        if (_disposed)
            return null;

        if (_capturedError.HasValue)
            return _capturedError;

        PopErrorScopeState state = default;
        state.Completed = 0;

        WGPUPopErrorScopeCallbackInfo callbackInfo;
        unsafe
        {
            callbackInfo = new WGPUPopErrorScopeCallbackInfo
            {
                NextInChain = null,
                Callback = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint, StringViewNative*, IntPtr, IntPtr, void>)&PopErrorScopeCallback,
                Userdata1 = (void*)(&state),
                Userdata2 = null
            };
        }

        WebGPU.DevicePopErrorScope(_device.Handle, callbackInfo);

        int waitedMs = 0;
        while (System.Threading.Volatile.Read(ref state.Completed) == 0 && waitedMs < timeoutMs)
        {
            WebGPU.InstanceProcessEvents(instance.Handle);
            Thread.Sleep(1);
            waitedMs++;
        }

        if (System.Threading.Volatile.Read(ref state.Completed) == 0)
        {
            _capturedError = new GpuError(GpuErrorType.Unknown, "PopErrorScope timed out");
        }
        else if (state.Status == (uint)WGPUPopErrorScopeStatus.Success)
        {
            _capturedError = new GpuError((GpuErrorType)state.ErrorType, null);
        }
        else
        {
            string? message = state.ErrorMessage != IntPtr.Zero
                ? Marshal.PtrToStringAnsi(state.ErrorMessage)
                : null;
            _capturedError = new GpuError((GpuErrorType)state.ErrorType, message);
        }

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
        StringViewNative* messagePtr,
        IntPtr userdata1,
        IntPtr userdata2)
    {
        PopErrorScopeState* state = (PopErrorScopeState*)(userdata1.ToPointer());
        if (state == null)
            return;

        state->Status = status;
        state->ErrorType = type;
        state->ErrorMessage = messagePtr != null ? messagePtr->Data : IntPtr.Zero;
        state->Completed = 1;
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
    private static long s_callbackCounter; // diagnostics: how many errors captured

    /// <summary>
    /// The ring that receives every uncaptured validation message. Thread-safe
    /// for a single producer (wgpu callback thread) and any consumer.
    /// </summary>
    public static ValidationLogRing Ring => s_ring;

    /// <summary>
    /// Total number of uncaptured errors that have been delivered to the ring.
    /// </summary>
    public static long TotalDelivered => Interlocked.Read(ref s_callbackCounter);

    /// <summary>
    /// Advances the internal counter so that all current ring entries are
    /// considered acknowledged. Use in test cleanup to avoid cross-test
    /// interference on the shared <see cref="Ring"/>.
    /// </summary>
    public static void AcknowledgeAll()
    {
        long ringWrites = s_ring.TotalWrites;
        Interlocked.Exchange(ref s_callbackCounter, ringWrites);
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
            Callback = (IntPtr)(delegate* unmanaged[Cdecl]<void*, uint, void*, void*, void*, void>)&EtchValidationCallback,
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
        long total = Interlocked.Read(ref s_callbackCounter);
        long ringWrites = s_ring.TotalWrites;
        long newErrors = ringWrites - total;

        if (newErrors <= 0)
            return 0;

        // Advance the counter so subsequent calls don't re-report the same errors.
        Interlocked.Add(ref s_callbackCounter, newErrors);

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
        void* message,
        void* userdata1,
        void* userdata2)
    {
        // All parameters are raw void* to eliminate any C# struct ABI mismatch.
        // message is a pointer to WGPUStringView.
        var messagePtr = (StringViewNative*)message;
        ReadOnlySpan<byte> utf8 = messagePtr != null && messagePtr->Data != IntPtr.Zero
            ? new ReadOnlySpan<byte>((byte*)messagePtr->Data, (int)messagePtr->Length)
            : ReadOnlySpan<byte>.Empty;

        s_ring.Push((ErrorType)errorType, utf8, Stopwatch.GetTimestamp());
        Interlocked.Increment(ref s_callbackCounter);

#if DEBUG
        if (utf8.Length > 0)
        {
            string? msg = System.Text.Encoding.UTF8.GetString(utf8);
            Console.Error.WriteLine("wgpu validation: " + msg);
        }
#endif
    }
}
