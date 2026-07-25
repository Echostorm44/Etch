#if NET || NETSTANDARD
using System;
using System.Runtime.Versioning;
using System.Text;
using Etch;
using Etch.Gpu.Native;

namespace Etch.Gpu;

public static partial class SurfaceFactory
{
    [SupportedOSPlatform("linux")]
    [UnsupportedOSPlatform("windows")]
    [UnsupportedOSPlatform("macos")]
    public static unsafe Surface CreateFromXlib(Instance instance, nint display, nint window, string? label = null)
    {
        if (display == 0)
        {
            Panic.ArgumentOutOfRange(nameof(display), "X Display* cannot be null.");
        }

        if (window == 0)
        {
            Panic.ArgumentOutOfRange(nameof(window), "X Window cannot be null.");
        }

        WGPUSurfaceSourceXlibWindow xlib = default;
        xlib.Chain.Next = null;
        xlib.Chain.SType = WGPUSType.SurfaceSourceXlibWindow;
        xlib.Display = (void*)display;
        xlib.Window = (ulong)window;

        Span<byte> labelScratch = stackalloc byte[Labels.MaxLabelLength + 1];
        int labelLength = label is null ? 0 : Encoding.UTF8.GetBytes(label, labelScratch);

        fixed (byte* labelPtr = labelScratch)
        {
            WGPUSurfaceDescriptor desc = default;
            desc.NextInChain = &xlib.Chain;
            desc.Label = new WGPUStringView
            {
                Data = label is null ? null : labelPtr,
                Length = (nuint)labelLength,
            };

            SurfaceHandle handle = WebGPU.InstanceCreateSurface(instance.Handle, (nint)(&desc));
            return new Surface(handle, label);
        }
    }

    [SupportedOSPlatform("linux")]
    [UnsupportedOSPlatform("windows")]
    [UnsupportedOSPlatform("macos")]
    public static unsafe Surface CreateFromWayland(Instance instance, nint wlDisplay, nint wlSurface, string? label = null)
    {
        if (wlDisplay == 0)
        {
            Panic.ArgumentOutOfRange(nameof(wlDisplay), "wl_display* cannot be null.");
        }

        if (wlSurface == 0)
        {
            Panic.ArgumentOutOfRange(nameof(wlSurface), "wl_surface* cannot be null.");
        }

        WGPUSurfaceSourceWaylandSurface wayland = default;
        wayland.Chain.Next = null;
        wayland.Chain.SType = WGPUSType.SurfaceSourceWaylandSurface;
        wayland.Display = (void*)wlDisplay;
        wayland.Surface = (void*)wlSurface;

        Span<byte> labelScratch = stackalloc byte[Labels.MaxLabelLength + 1];
        int labelLength = label is null ? 0 : Encoding.UTF8.GetBytes(label, labelScratch);

        fixed (byte* labelPtr = labelScratch)
        {
            WGPUSurfaceDescriptor desc = default;
            desc.NextInChain = &wayland.Chain;
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
