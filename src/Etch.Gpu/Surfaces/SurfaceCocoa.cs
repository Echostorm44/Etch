#if NET || NETSTANDARD
using System;
using System.Runtime.Versioning;
using System.Text;
using Etch;
using Etch.Gpu.Native;

namespace Etch.Gpu;

public static partial class SurfaceFactory
{
    [SupportedOSPlatform("macos")]
    [UnsupportedOSPlatform("windows")]
    [UnsupportedOSPlatform("linux")]
    public static unsafe Surface CreateFromCaMetalLayer(Instance instance, nint metalLayer, string? label = null)
    {
        if (metalLayer == 0)
        {
            Panic.ArgumentOutOfRange(nameof(metalLayer), "CAMetalLayer pointer cannot be null. Caller must assign a CAMetalLayer to the view before calling this method.");
        }

        WGPUSurfaceSourceMetalLayer cocoa = default;
        cocoa.Chain.Next = null;
        cocoa.Chain.SType = WGPUSType.SurfaceSourceMetalLayer;
        cocoa.Layer = (void*)metalLayer;

        Span<byte> labelScratch = stackalloc byte[Labels.MaxLabelLength + 1];
        int labelLength = label is null ? 0 : Encoding.UTF8.GetBytes(label, labelScratch);

        fixed (byte* labelPtr = labelScratch)
        {
            WGPUSurfaceDescriptor desc = default;
            desc.NextInChain = &cocoa.Chain;
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
