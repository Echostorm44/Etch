using System;
using System.Threading;
using Etch.Gpu.Descriptors;
using Etch.Gpu.Native;
using Etch.Gpu.Validation;

namespace Etch.Gpu.Tests;

/// <summary>
/// Tests for <see cref="ErrorScope"/> push/pop behaviour.
/// Live-device tests are deferred until the wgpu-native v29 callback ABI
/// is fully reconciled (see ValidationBridge.EtchValidationCallback docs).
/// </summary>
[NotInParallel]
internal sealed class ErrorScopeTests
{
    [Test]
    public async Task ErrorScopeTypeIsAccessible()
    {
        // Smoke test that the ErrorScope type and its enums are wired.
        // Live-device tests are deferred until the wgpu-native v29 callback
        // ABI is fully reconciled.
        _ = ErrorFilter.Validation;
        _ = GpuErrorType.Validation;
        await Task.CompletedTask;
    }
}
