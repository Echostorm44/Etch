using System;
using Etch.Gpu.Descriptors;
using Etch.Gpu.Validation;

namespace Etch.Gpu.Tests;

/// <summary>
/// Live-device tests for <see cref="ErrorScope"/> and the uncaptured-error callback installed by
/// <see cref="ValidationBridge.ConfigureDeviceDescriptor"/>. Both callbacks receive a WGPUStringView
/// by value; these tests provoke real wgpu errors so a callback ABI mismatch (which shifts the
/// userdata arguments on SysV x64 and AArch64) shows up as a wrong or missing result, not a latent
/// crash. Not parallel: they share the process-wide validation ring.
/// </summary>
[NotInParallel]
internal sealed class ErrorScopeTests
{
    private const int TimeoutMs = 5000;

    // MAP_READ may only be combined with COPY_DST, so this usage is a validation error.
    private static readonly BufferDescriptor InvalidUsageBuffer = new()
    {
        Usage = (ulong)(BufferUsage.MapRead | BufferUsage.MapWrite),
        Size = 256,
    };

    private static readonly BufferDescriptor ValidBuffer = new()
    {
        Usage = (ulong)(BufferUsage.MapRead | BufferUsage.CopyDst),
        Size = 256,
    };

    [Test]
    public async Task ValidationScope_CapturesErrorAndItsMessage()
    {
        using DeviceContext context = CreateDeviceContext();
        ValidationBridge.AcknowledgeAll();
        long deliveredBefore = ValidationBridge.TotalDelivered;

        using var scope = new ErrorScope(context.Device, ErrorFilter.Validation);
        context.Device.CreateBuffer(InvalidUsageBuffer).Dispose();
        GpuError? error = scope.Pop(context.Instance, TimeoutMs);

        await Assert.That(error.HasValue).IsTrue();
        await Assert.That(error!.Value.Type).IsEqualTo(GpuErrorType.Validation);
        await Assert.That(error.Value.Message).IsNotNull();
        await Assert.That(error.Value.Message!).Contains("MAP_WRITE");
        // Captured by the scope, so it must not also reach the uncaptured callback.
        await Assert.That(ValidationBridge.TotalDelivered).IsEqualTo(deliveredBefore);
    }

    [Test]
    public async Task ValidationScope_WithoutError_ReportsNoError()
    {
        using DeviceContext context = CreateDeviceContext();

        using var scope = new ErrorScope(context.Device, ErrorFilter.Validation);
        context.Device.CreateBuffer(ValidBuffer).Dispose();
        GpuError? error = scope.Pop(context.Instance, TimeoutMs);

        await Assert.That(error.HasValue).IsTrue();
        await Assert.That(error!.Value.Type).IsEqualTo(GpuErrorType.NoError);
    }

    [Test]
    public async Task UnscopedError_ReachesValidationRingWithItsMessage()
    {
        using DeviceContext context = CreateDeviceContext();
        ValidationBridge.AcknowledgeAll();

        // An OutOfMemory scope does not match a validation error, which therefore falls through to
        // the uncaptured-error callback.
        using var scope = new ErrorScope(context.Device, ErrorFilter.OutOfMemory);
        context.Device.CreateBuffer(InvalidUsageBuffer).Dispose();
        GpuError? scoped = scope.Pop(context.Instance, TimeoutMs);

        await Assert.That(scoped.HasValue).IsTrue();
        await Assert.That(scoped!.Value.Type).IsEqualTo(GpuErrorType.NoError);

        string? panicMessage = null;
        try
        {
            ValidationBridge.ThrowIfValidationErrorsPresent("invalid buffer usage");
        }
        catch (EtchException exception)
        {
            panicMessage = exception.Message;
        }

        await Assert.That(panicMessage).IsNotNull();
        await Assert.That(panicMessage!).Contains("MAP_WRITE");
    }

    private static unsafe DeviceContext CreateDeviceContext()
    {
        var instance = Instance.Create();
        var (adapterStatus, adapter) = AsyncRequest.RequestAdapterSync(instance);
        if (adapterStatus != RequestAdapterStatus.Success || adapter.IsInvalid)
        {
            instance.Dispose();
            Skip.Test("No wgpu adapter available");
        }

        DeviceDescriptor descriptor = default;
        ValidationBridge.ConfigureDeviceDescriptor(&descriptor);
        var (deviceStatus, device) = AsyncRequest.RequestDeviceSync(instance, adapter, &descriptor);
        adapter.Dispose();
        if (deviceStatus != RequestDeviceStatus.Success || device.IsInvalid)
        {
            instance.Dispose();
            Skip.Test("Could not create a wgpu device");
        }

        return new DeviceContext(instance, device);
    }

    private readonly struct DeviceContext : IDisposable
    {
        public readonly Instance Instance;
        public readonly Device Device;

        public DeviceContext(Instance instance, Device device)
        {
            Instance = instance;
            Device = device;
        }

        public void Dispose()
        {
            Device.Dispose();
            Instance.Dispose();
        }
    }
}
