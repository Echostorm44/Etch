using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Etch.Gpu.Native;

internal static class NativeLibraryResolver
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register()
    {
        NativeLibrary.SetDllImportResolver(typeof(WebGPU).Assembly, Resolve);
    }

    private static nint Resolve(string name, Assembly asm, DllImportSearchPath? path)
    {
        if (name != "wgpu_native")
            return 0;

        string rid = RuntimeInformation.RuntimeIdentifier;
        string fileName = OperatingSystem.IsWindows() ? "wgpu_native.dll"
            : OperatingSystem.IsMacOS() ? "libwgpu_native.dylib"
            : "libwgpu_native.so";

        // 1. Probe runtimes/{rid}/native/ relative to app base (publish / self-contained layout)
        string probe = Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native", fileName);
        if (NativeLibrary.TryLoad(probe, out nint handle))
            return handle;

        // 2. Probe app base directly (build-time copy for project-reference consumers)
        probe = Path.Combine(AppContext.BaseDirectory, fileName);
        if (NativeLibrary.TryLoad(probe, out handle))
            return handle;

        return 0;
    }
}