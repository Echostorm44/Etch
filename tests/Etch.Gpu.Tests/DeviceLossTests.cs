using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using Etch.Gpu.Descriptors;
using Etch.Gpu.SwapChains;
using Etch.Gpu.Validation;

namespace Etch.Gpu.Tests;

/// <summary>
/// A device lost mid-frame — after a frame was acquired and encoded, at submit, at present — must
/// not abort the process (the pinned wgpu-native's handle_error_fatal did; Etch carries patches
/// in native/wgpu-native/patches). <see cref="Device.Destroy"/> loses the device on purpose, as a
/// driver reset would. Each scenario runs on the software adapter (WARP on Windows) and on the
/// default high-performance adapter; when both are the same adapter the second run repeats the
/// first. A regression here ends the test process (the exit code is the only trace of it).
/// Not parallel: the scenarios assert on the process-wide validation ring.
/// </summary>
[NotInParallel]
internal sealed partial class DeviceLossTests
{
    private const uint FrameSize = 64;

    private static readonly Extent3D FrameExtent = new() { Width = FrameSize, Height = FrameSize, DepthOrArrayLayers = 1 };

    private static readonly BufferDescriptor ReadbackBuffer = new()
    {
        Usage = (ulong)(BufferUsage.MapRead | BufferUsage.CopyDst),
        Size = 256,
    };

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task OffscreenFrame_LostBeforeSubmit_ReportsLossAndLaterCallsAreSafe(bool softwareAdapter)
    {
        OffscreenObservations observed = RunOffscreenLoss(softwareAdapter);

        await Assert.That(observed.LostAfterSubmit).IsTrue();
        await Assert.That(observed.Reason).IsEqualTo(DeviceLostReason.Destroyed);
        await Assert.That(observed.PollReportedIdle).IsTrue();
        await Assert.That(observed.MapSucceeded).IsFalse();
        await Assert.That(observed.MappedRangeLength).IsEqualTo(0);
        // A lost device reports no validation errors (WebGPU), whatever its later calls run into.
        await Assert.That(observed.ValidationErrorsAfterLoss).IsEqualTo(0L);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task LostDevice_CallbackRunsOnce_ThroughEveryFailingCall(bool softwareAdapter)
    {
        int calls = RunCountingLoss(softwareAdapter, out uint reason, failingCalls: true);

        await Assert.That(calls).IsEqualTo(1);
        await Assert.That(reason).IsEqualTo((uint)DeviceLostReason.Destroyed);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task DestroyedDevice_IdlePoll_ReportsLossThroughWgpuCoreNotification(bool softwareAdapter)
    {
        // No operation meets the lost device here: the only report is wgpu-core's own notification
        // (its device-lost closure, fired when the destroyed device's queue is idle), which is also
        // the path a driver reset takes.
        int calls = RunCountingLoss(softwareAdapter, out uint reason, failingCalls: false);

        await Assert.That(calls).IsEqualTo(1);
        await Assert.That(reason).IsEqualTo((uint)DeviceLostReason.Destroyed);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task WindowFrame_LostAfterAcquire_SubmitAndPresentFailWithoutAborting(bool softwareAdapter)
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("Window surfaces are exercised on Windows (CI has no display on Linux)");
            return;
        }

        WindowObservations observed = RunWindowLoss(softwareAdapter, LossPoint.AfterAcquire);

        await Assert.That(observed.FirstAcquire).IsEqualTo(SurfaceTextureResult.Ok);
        await Assert.That(observed.Presented).IsFalse();
        await Assert.That(observed.Lost).IsTrue();
        await Assert.That(observed.NextAcquire).IsEqualTo(SurfaceTextureResult.DeviceLost);
        await Assert.That(observed.ValidationErrorsAfterLoss).IsEqualTo(0L);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task WindowFrame_LostAfterSubmit_PresentFailsWithoutAborting(bool softwareAdapter)
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("Window surfaces are exercised on Windows (CI has no display on Linux)");
            return;
        }

        WindowObservations observed = RunWindowLoss(softwareAdapter, LossPoint.AfterSubmit);

        await Assert.That(observed.FirstAcquire).IsEqualTo(SurfaceTextureResult.Ok);
        await Assert.That(observed.Presented).IsFalse();
        await Assert.That(observed.Lost).IsTrue();
        await Assert.That(observed.ValidationErrorsAfterLoss).IsEqualTo(0L);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task WindowFrame_LostBeforeAcquire_AcquireReportsErrorWithoutAborting(bool softwareAdapter)
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("Window surfaces are exercised on Windows (CI has no display on Linux)");
            return;
        }

        // No watch on this swap chain, so the acquire goes to wgpu-native, which used to abort.
        WindowObservations observed = RunWindowLoss(softwareAdapter, LossPoint.BeforeAcquireUnwatched);

        await Assert.That(observed.FirstAcquire).IsEqualTo(SurfaceTextureResult.Error);
        await Assert.That(observed.Lost).IsTrue();
        await Assert.That(observed.ValidationErrorsAfterLoss).IsEqualTo(0L);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task WindowFrame_AfterLoss_NewDeviceAndSurfacePresentAgain(bool softwareAdapter)
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("Window surfaces are exercised on Windows (CI has no display on Linux)");
            return;
        }

        WindowObservations observed = RunWindowLoss(softwareAdapter, LossPoint.AfterAcquire, recover: true);

        await Assert.That(observed.Lost).IsTrue();
        await Assert.That(observed.RecoveredAcquire).IsEqualTo(SurfaceTextureResult.Ok);
        await Assert.That(observed.RecoveredPresented).IsTrue();
    }

    // ── Offscreen ────────────────────────────────────────────────────────────

    private static unsafe OffscreenObservations RunOffscreenLoss(bool softwareAdapter)
    {
        using var instance = Instance.Create();
        using Adapter adapter = RequestAdapter(instance, surface: null, softwareAdapter);

        DeviceDescriptor descriptor = default;
        ValidationBridge.ConfigureDeviceDescriptor(&descriptor);
        using DeviceLossWatch watch = DeviceLossWatch.Attach(&descriptor);
        Device device = RequestDevice(instance, adapter, &descriptor);
        try
        {
            using Texture target = device.CreateTexture(new TextureDescriptor
            {
                Usage = (ulong)(TextureUsage.RenderAttachment | TextureUsage.CopySrc),
                Dimension = TextureDimension.D2,
                Size = FrameExtent,
                Format = TextureFormat.Rgba8Unorm,
            });
            using TextureView view = target.CreateView();
            using Buffer readback = device.CreateBuffer(ReadbackBuffer);

            // Encode a frame, then lose the device before it is submitted.
            using CommandBuffer frame = EncodeClear(device, view.Handle);
            ValidationBridge.AcknowledgeAll();
            long deliveredAtLoss = ValidationBridge.TotalDelivered;
            device.Destroy();
            Submit(device, frame);
            bool lostAfterSubmit = watch.IsLost;

            // Everything after the loss fails quietly.
            using CommandBuffer next = EncodeClear(device, view.Handle);
            Submit(device, next);
            device.Queue.WriteBuffer(readback, 0, stackalloc byte[16]);
            bool pollReportedIdle = device.Poll(wait: true);
            bool mapSucceeded = readback.MapSync(device, MapMode.Read, 0, ReadbackBuffer.Size, timeoutMilliseconds: 2_000);
            int mappedRangeLength = readback.GetConstMappedRange(0, ReadbackBuffer.Size).Length;

            return new OffscreenObservations(
                lostAfterSubmit,
                watch.Reason,
                pollReportedIdle,
                mapSucceeded,
                mappedRangeLength,
                ValidationBridge.TotalDelivered - deliveredAtLoss);
        }
        finally
        {
            // Releasing a lost device used to abort too (its final poll was fatal).
            watch.Dispose();
            device.Dispose();
            ValidationBridge.AcknowledgeAll();
        }
    }

    private static int lostCallbackCalls;
    private static uint lostCallbackReason;

    private static unsafe int RunCountingLoss(bool softwareAdapter, out uint reason, bool failingCalls)
    {
        using var instance = Instance.Create();
        using Adapter adapter = RequestAdapter(instance, surface: null, softwareAdapter);

        Volatile.Write(ref lostCallbackCalls, 0);
        Volatile.Write(ref lostCallbackReason, 0u);
        DeviceDescriptor descriptor = default;
        ValidationBridge.ConfigureDeviceDescriptor(&descriptor);
        descriptor.DeviceLostCallbackInfo = new DeviceLostCallbackInfo
        {
            Mode = (uint)CallbackMode.AllowSpontaneous,
            Callback = (IntPtr)(delegate* unmanaged[Cdecl]<void*, uint, StringViewRaw, void*, void*, void>)&CountDeviceLost,
        };
        Device device = RequestDevice(instance, adapter, &descriptor);
        try
        {
            device.Destroy();

            // Each of these meets the lost device, and so does the poll that retires it (wgpu-core's
            // own lost notification): the callback must still run once.
            if (failingCalls)
            {
                using CommandEncoder encoder = device.CreateCommandEncoder();
                using CommandBuffer empty = encoder.Finish();
                Submit(device, empty);
                Submit(device, empty);
            }

            device.Poll(wait: true);
            device.Poll(wait: false);

            reason = Volatile.Read(ref lostCallbackReason);
            return Volatile.Read(ref lostCallbackCalls);
        }
        finally
        {
            device.Dispose();
            ValidationBridge.AcknowledgeAll();
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe void CountDeviceLost(void* device, uint reason, StringViewRaw message, void* userdata1, void* userdata2)
    {
        Interlocked.Increment(ref lostCallbackCalls);
        Volatile.Write(ref lostCallbackReason, reason);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StringViewRaw
    {
        public IntPtr Data;
        public UIntPtr Length;
    }

    // ── Window ───────────────────────────────────────────────────────────────

    private enum LossPoint
    {
        AfterAcquire,
        AfterSubmit,
        BeforeAcquireUnwatched,
    }

    [SupportedOSPlatform("windows")]
    private static unsafe WindowObservations RunWindowLoss(bool softwareAdapter, LossPoint lossPoint, bool recover = false)
    {
        using var window = new TestWindow((int)FrameSize, (int)FrameSize);
        using var instance = Instance.Create();
        Surface surface = SurfaceFactory.CreateFromWin32(instance, window.Handle, TestWindow.ModuleHandle);
        var observed = new WindowObservations();
        try
        {
            using (Adapter adapter = RequestAdapter(instance, surface, softwareAdapter))
            {
                DeviceDescriptor descriptor = default;
                ValidationBridge.ConfigureDeviceDescriptor(&descriptor);
                using DeviceLossWatch watch = DeviceLossWatch.Attach(&descriptor);
                Device device = RequestDevice(instance, adapter, &descriptor);
                SwapChain swapChain = SwapChain.Configure(
                    device,
                    surface,
                    SwapChainFor(),
                    lossPoint == LossPoint.BeforeAcquireUnwatched ? null : watch);
                try
                {
                    ValidationBridge.AcknowledgeAll();
                    long deliveredAtLoss = ValidationBridge.TotalDelivered;
                    if (lossPoint == LossPoint.BeforeAcquireUnwatched)
                    {
                        device.Destroy();
                    }

                    observed.FirstAcquire = swapChain.AcquireFrame(out SurfaceTexture frame);
                    if (lossPoint == LossPoint.AfterAcquire)
                    {
                        device.Destroy();
                    }

                    if (frame.IsValid)
                    {
                        using CommandBuffer commands = EncodeClear(device, frame.View);
                        Submit(device, commands);
                        if (lossPoint == LossPoint.AfterSubmit)
                        {
                            device.Destroy();
                        }

                        // Present releases the frame even when it cannot present it (that release
                        // discards the frame, which used to abort on a lost device as well).
                        observed.Presented = swapChain.Present(frame);
                    }

                    observed.Lost = watch.IsLost;
                    observed.NextAcquire = swapChain.AcquireFrame(out SurfaceTexture next);
                    next.Dispose();

                    // Reconfiguring with the lost device (a resize) fails quietly too.
                    swapChain.Resize(FrameSize, FrameSize);
                    device.Poll(wait: true);
                    observed.ValidationErrorsAfterLoss = ValidationBridge.TotalDelivered - deliveredAtLoss;
                }
                finally
                {
                    swapChain.Dispose();
                    watch.Dispose();
                    device.Dispose();
                    ValidationBridge.AcknowledgeAll();
                }
            }

            if (recover)
            {
                // The lost device's surface still holds its last frame, so recovery starts over
                // with a new surface for the same window and a new device.
                surface.Dispose();
                surface = SurfaceFactory.CreateFromWin32(instance, window.Handle, TestWindow.ModuleHandle);
                using Adapter adapter = RequestAdapter(instance, surface, softwareAdapter);
                DeviceDescriptor descriptor = default;
                ValidationBridge.ConfigureDeviceDescriptor(&descriptor);
                using DeviceLossWatch watch = DeviceLossWatch.Attach(&descriptor);
                Device device = RequestDevice(instance, adapter, &descriptor);
                SwapChain swapChain = SwapChain.Configure(device, surface, SwapChainFor(), watch);
                try
                {
                    observed.RecoveredAcquire = swapChain.AcquireFrame(out SurfaceTexture frame);
                    if (frame.IsValid)
                    {
                        using CommandBuffer commands = EncodeClear(device, frame.View);
                        Submit(device, commands);
                        observed.RecoveredPresented = swapChain.Present(frame);
                    }
                }
                finally
                {
                    swapChain.Dispose();
                    watch.Dispose();
                    device.Dispose();
                }
            }

            return observed;
        }
        finally
        {
            surface.Dispose();
        }
    }

    private static SwapChainConfig SwapChainFor() => new()
    {
        Format = TextureFormat.Bgra8Unorm,
        Width = FrameSize,
        Height = FrameSize,
        PresentMode = PresentMode.Fifo,
        AlphaMode = CompositeAlphaMode.Auto,
        Usage = TextureUsage.RenderAttachment,
    };

    // ── Shared ───────────────────────────────────────────────────────────────

    private static Adapter RequestAdapter(Instance instance, Surface? surface, bool softwareAdapter)
    {
        var (status, adapter) = softwareAdapter
            ? AsyncRequest.RequestAdapterSync(instance, surface, forceFallbackAdapter: true)
            : AsyncRequest.RequestAdapterSync(instance, surface, PowerPreference.HighPerformance);
        if (status != RequestAdapterStatus.Success || adapter.IsInvalid)
        {
            Skip.Test(softwareAdapter ? "No software adapter available" : "No wgpu adapter available");
        }

        Console.WriteLine($"Adapter: {adapter.GetDescription()}");
        return adapter;
    }

    private static unsafe Device RequestDevice(Instance instance, Adapter adapter, DeviceDescriptor* descriptor)
    {
        var (status, device) = AsyncRequest.RequestDeviceSync(instance, adapter, descriptor);
        if (status != RequestDeviceStatus.Success || device.IsInvalid)
        {
            Skip.Test("Could not create a wgpu device");
        }

        return device;
    }

    private static unsafe CommandBuffer EncodeClear(Device device, Native.TextureViewHandle view)
    {
        using CommandEncoder encoder = device.CreateCommandEncoder();
        var attachment = new RenderPassColorAttachment
        {
            View = view,
            DepthSlice = 0xFFFFFFFFu,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
            ClearValue = new Color { R = 0.2, G = 0.4, B = 0.6, A = 1.0 },
        };
        using (RenderPass pass = encoder.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachmentCount = 1,
            ColorAttachments = (IntPtr)(&attachment),
        }))
        {
            pass.End();
        }

        return encoder.Finish();
    }

    private static void Submit(Device device, CommandBuffer commands)
    {
        Span<CommandBuffer> list = stackalloc CommandBuffer[1];
        list[0] = commands;
        device.Queue.Submit(list);
    }

    private readonly record struct OffscreenObservations(
        bool LostAfterSubmit,
        DeviceLostReason? Reason,
        bool PollReportedIdle,
        bool MapSucceeded,
        int MappedRangeLength,
        long ValidationErrorsAfterLoss);

    private sealed class WindowObservations
    {
        public SurfaceTextureResult FirstAcquire { get; set; } = SurfaceTextureResult.Error;
        public bool Presented { get; set; }
        public bool Lost { get; set; }
        public SurfaceTextureResult NextAcquire { get; set; } = SurfaceTextureResult.Ok;
        public long ValidationErrorsAfterLoss { get; set; }
        public SurfaceTextureResult RecoveredAcquire { get; set; } = SurfaceTextureResult.Error;
        public bool RecoveredPresented { get; set; }
    }

    /// <summary>A small visible popup window to present to (the predefined STATIC class).</summary>
    [SupportedOSPlatform("windows")]
    private sealed partial class TestWindow : IDisposable
    {
        private const uint WsPopup = 0x80000000;
        private const uint WsVisible = 0x10000000;
        private const uint WsExToolWindow = 0x00000080;
        private const uint WsExNoActivate = 0x08000000;

        public TestWindow(int width, int height)
        {
            Handle = CreateWindowExW(WsExToolWindow | WsExNoActivate, "STATIC", "Etch device loss", WsPopup | WsVisible, 0, 0, width, height, 0, 0, ModuleHandle, 0);
            if (Handle == 0)
            {
                throw new InvalidOperationException($"CreateWindowExW failed: {Marshal.GetLastPInvokeError()}");
            }
        }

        public static nint ModuleHandle { get; } = GetModuleHandleW(0);

        public nint Handle { get; }

        public void Dispose()
        {
            DestroyWindow(Handle);
        }

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        private static partial nint CreateWindowExW(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool DestroyWindow(nint window);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("kernel32.dll")]
        private static partial nint GetModuleHandleW(nint moduleName);
    }
}
