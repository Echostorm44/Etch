namespace Etch.Gpu;

// WGPUErrorType (webgpu.h v29): NoError=1, Validation=2, OutOfMemory=3,
// Internal=4, Unknown=5. Values shifted up by 1 from prior revisions.

public static class Errors
{
    public static void HandleUncapturedError(ErrorType type, string message)
    {
        var code = type switch
        {
            ErrorType.Validation => PanicCodes.GpuValidation,
            ErrorType.OutOfMemory => PanicCodes.GpuOutOfMemory,
            ErrorType.Internal => PanicCodes.GpuInternal,
            ErrorType.NoError => PanicCodes.GpuUnknown,
            _ => PanicCodes.GpuUnknown,
        };
        Panic.Invariant(code, message);
    }
}
