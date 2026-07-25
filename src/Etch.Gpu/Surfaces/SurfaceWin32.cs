#if NET || NETSTANDARD
using System;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using Etch.Gpu.Native;

namespace Etch.Gpu;

public static partial class SurfaceFactory
{
    [SupportedOSPlatform("windows")]
    [UnsupportedOSPlatform("linux")]
    [UnsupportedOSPlatform("macos")]
    public static unsafe Surface CreateFromWin32(Instance instance, nint hwnd, nint hinstance, string? label = null)
    {
        Debug.Assert(!instance.Handle.IsInvalid, "Instance handle must be valid");
        Debug.Assert(hwnd != 0, "HWND must be non-null");
        Debug.Assert(hinstance != 0, "HINSTANCE must be non-null");

        WGPUSurfaceSourceWindowsHWND win32 = default;
        win32.Chain.Next = null;
        win32.Chain.SType = WGPUSType.SurfaceSourceWindowsHWND;
        win32.Hinstance = (void*)hinstance;
        win32.Hwnd = (void*)hwnd;

        // Encode label into stack scratch; keep it pinned across the native
        // call via the enclosing `fixed` scope below.
        Span<byte> labelScratch = stackalloc byte[Labels.MaxLabelLength + 1];
        int labelLength = label is null ? 0 : Encoding.UTF8.GetBytes(label, labelScratch);

        fixed (byte* labelPtr = labelScratch)
        {
            WGPUSurfaceDescriptor desc = default;
            desc.NextInChain = &win32.Chain;
            desc.Label = new WGPUStringView
            {
                Data = label is null ? null : labelPtr,
                Length = (nuint)labelLength,
            };

            SurfaceHandle handle = WebGPU.InstanceCreateSurface(instance.Handle, (nint)(&desc));
            return new Surface(handle, label);
        }
    }
}
#endif
