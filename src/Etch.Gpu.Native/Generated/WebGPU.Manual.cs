// Manual additions to the auto-generated WebGPU bindings.
// These are standard webgpu.h v29 functions that were not emitted by the
// generator but are required for off-screen render → read-back.

#pragma warning disable CA1401
#pragma warning disable CS0649

namespace Etch.Gpu.Native;

public enum MapAsyncStatus : uint
{
    Success = 0x00000001,
    ValidationError = 0x00000002,
    Unknown = 0x00000003,
}

public unsafe struct WGPUTexelCopyBufferInfo
{
    public WGPUTexelCopyBufferLayout Layout;
    public BufferHandle Buffer;
}

public unsafe struct WGPUBufferMapCallbackInfo
{
    public WGPUChainedStruct* NextInChain;
    public uint Mode;                    // WGPUCallbackMode
    public IntPtr Callback;              // WGPUBufferMapCallback
    public void* Userdata1;
    public void* Userdata2;
}

public static partial class WebGPU
{
    [System.Runtime.InteropServices.LibraryImport("wgpu_native", EntryPoint = "wgpuCommandEncoderCopyTextureToBuffer")]
    [System.Runtime.InteropServices.UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void CommandEncoderCopyTextureToBuffer(CommandEncoderHandle encoder, System.IntPtr source, System.IntPtr destination, System.IntPtr copySize);

    [System.Runtime.InteropServices.LibraryImport("wgpu_native", EntryPoint = "wgpuBufferMapAsync")]
    [System.Runtime.InteropServices.UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void BufferMapAsync(BufferHandle buffer, uint mode, ulong offset, ulong size, System.IntPtr callbackInfo);

    [System.Runtime.InteropServices.LibraryImport("wgpu_native", EntryPoint = "wgpuBufferGetConstMappedRange")]
    [System.Runtime.InteropServices.UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial System.IntPtr BufferGetConstMappedRange(BufferHandle buffer, ulong offset, ulong size);

    [System.Runtime.InteropServices.LibraryImport("wgpu_native", EntryPoint = "wgpuBufferGetMappedRange")]
    [System.Runtime.InteropServices.UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial System.IntPtr BufferGetMappedRange(BufferHandle buffer, ulong offset, ulong size);
}
