using System;
using System.Text;
using Etch.Gpu.Descriptors;
using Etch.Gpu.Diagnostics;
using Etch.Gpu.Native;
using Etch.Gpu.Validation;

namespace Etch.Gpu.Tests;

[NotInParallel]
internal sealed class ValidationBridgeTests
{
    [Test]
    public async Task ConfigureDeviceDescriptorSetsNonZeroCallback()
    {
        IntPtr callback = GetConfiguredCallback();
        if (callback == IntPtr.Zero)
        {
            throw new InvalidOperationException("ConfigureDeviceDescriptor did not set Callback");
        }

        await Task.CompletedTask;
    }

    [Test]
    public async Task ThrowIfValidationErrorsPresentReturnsZeroWhenClean()
    {
        ValidationBridge.AcknowledgeAll();
        int result = ValidationBridge.ThrowIfValidationErrorsPresent("test-clean");
        if (result != 0)
        {
            throw new InvalidOperationException($"Expected 0 errors, got {result}");
        }

        await Task.CompletedTask;
    }

    [Test]
    public async Task ThrowIfValidationErrorsPresentThrowsWithGpuValidationCode()
    {
        ValidationBridge.AcknowledgeAll();

        // Manually push an entry into the shared ring to simulate a callback.
        ValidationBridge.Ring.Push(ErrorType.Validation, Encoding.UTF8.GetBytes("test-error"), 1);

        try
        {
            ValidationBridge.ThrowIfValidationErrorsPresent("test-context");
            throw new InvalidOperationException("Expected EtchException was not thrown");
        }
        catch (EtchException ex) when (ex.Code == Etch.PanicCodes.GpuValidation)
        {
            if (!ex.Message.Contains("test-context", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Message missing context: {ex.Message}");
            }
            if (!ex.Message.Contains("test-error", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Message missing error text: {ex.Message}");
            }
        }
        finally
        {
            ValidationBridge.AcknowledgeAll();
        }

        await Task.CompletedTask;
    }

    [Test]
    public async Task TotalDeliveredTracksCallbackCount()
    {
        // TotalDelivered tracks s_callbackCounter, which is only incremented
        // by the unmanaged callback (EtchValidationCallback), not by direct
        // Ring.Push calls. We verify the counter itself by observing that
        // AcknowledgeAll aligns it with the ring.
        ValidationBridge.AcknowledgeAll();
        long before = ValidationBridge.TotalDelivered;

        // Push directly — this does NOT increment TotalDelivered.
        ValidationBridge.Ring.Push(ErrorType.Validation, Encoding.UTF8.GetBytes("a"), 1);
        ValidationBridge.Ring.Push(ErrorType.Validation, Encoding.UTF8.GetBytes("b"), 2);

        long mid = ValidationBridge.TotalDelivered;
        if (mid != before)
        {
            throw new InvalidOperationException($"Direct Push should not affect TotalDelivered, but got {mid - before}");
        }

        // After acknowledging, TotalDelivered catches up.
        ValidationBridge.AcknowledgeAll();
        long after = ValidationBridge.TotalDelivered;
        if (after != before + 2)
        {
            throw new InvalidOperationException($"Expected +2 after AcknowledgeAll, got {after - before}");
        }

        await Task.CompletedTask;
    }

    private static unsafe IntPtr GetConfiguredCallback()
    {
        DeviceDescriptor desc = default;
        ValidationBridge.ConfigureDeviceDescriptor(&desc);
        return desc.UncapturedErrorCallbackInfo.Callback;
    }
}
