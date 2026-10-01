namespace Etch.Gpu.Native.Tests;

// The expected numbers are bindgen's own layout assertions for the wgpu-native commit pinned in
// native/wgpu-native/SOURCE (target/release/build/wgpu-native-*/out/bindings.rs). If an upgrade
// changes a struct, these fail before a mis-sized chain reaches native code, where it would be
// silently ignored or read past its end.
internal sealed class NativeAbiLayoutTests
{
    [Test]
    public async Task LimitsMatchesNativeSize()
    {
        await Assert.That(Layout.LimitsSize()).IsEqualTo(152);
    }

    [Test]
    public async Task NativeLimitsFieldsFollowPaddedChain()
    {
        (int size, int bindingsOffset) = Layout.NativeLimits();
        await Assert.That(size).IsEqualTo(32);
        await Assert.That(bindingsOffset).IsEqualTo(16);
    }

    [Test]
    public async Task InstanceExtrasMatchesNativeLayout()
    {
        (int size, int backends, int flags, int dxcPath, int displayHandle) = Layout.InstanceExtras();
        await Assert.That(size).IsEqualTo(112);
        await Assert.That(backends).IsEqualTo(16);
        await Assert.That(flags).IsEqualTo(24);
        await Assert.That(dxcPath).IsEqualTo(48);
        await Assert.That(displayHandle).IsEqualTo(88);
    }

    [Test]
    public async Task DeviceExtrasMatchesNativeLayout()
    {
        (int size, int memoryHints, int blockSizeStart) = Layout.DeviceExtras();
        await Assert.That(size).IsEqualTo(56);
        await Assert.That(memoryHints).IsEqualTo(32);
        await Assert.That(blockSizeStart).IsEqualTo(40);
    }

    [Test]
    public async Task CallbackInfosMatchNativeLayout()
    {
        // { nextInChain, mode (+4 padding), callback, userdata1, userdata2 } = 40 bytes. These travel
        // by value, so a missing field (PopErrorScope once lacked `mode`) misplaces the callback.
        foreach ((int size, int mode, int callback) in Layout.CallbackInfos())
        {
            await Assert.That(size).IsEqualTo(40);
            await Assert.That(mode).IsEqualTo(8);
            await Assert.That(callback).IsEqualTo(16);
        }
    }

    [Test]
    public async Task UndefinedLimitsLeaveEveryFieldAtWgpuDefault()
    {
        WGPULimits limits = WGPULimits.Undefined();
        await Assert.That(Layout.HasNoChain(limits)).IsTrue();
        await Assert.That(limits.MaxBindGroups).IsEqualTo(WGPULimits.U32Undefined);
        await Assert.That(limits.MaxBufferSize).IsEqualTo(WGPULimits.U64Undefined);
        await Assert.That(limits.MaxImmediateSize).IsEqualTo(WGPULimits.U32Undefined);
    }

    // Pointer arithmetic cannot live in async test bodies, so the measurements happen here.
    private static unsafe class Layout
    {
        public static int LimitsSize() => sizeof(WGPULimits);

        public static bool HasNoChain(WGPULimits limits) => limits.NextInChain == null;

        public static (int Size, int BindingsOffset) NativeLimits()
        {
            WGPUNativeLimits limits = default;
            byte* start = (byte*)&limits;
            return (sizeof(WGPUNativeLimits), (int)((byte*)&limits.MaxNonSamplerBindings - start));
        }

        public static (int Size, int Backends, int Flags, int DxcPath, int DisplayHandle) InstanceExtras()
        {
            WGPUInstanceExtras extras = default;
            byte* start = (byte*)&extras;
            return (
                sizeof(WGPUInstanceExtras),
                (int)((byte*)&extras.Backends - start),
                (int)((byte*)&extras.Flags - start),
                (int)((byte*)&extras.DxcPath - start),
                (int)((byte*)&extras.DisplayHandle - start));
        }

        public static (int Size, int Mode, int Callback)[] CallbackInfos()
        {
            WGPURequestAdapterCallbackInfo adapter = default;
            WGPURequestDeviceCallbackInfo device = default;
            WGPUBufferMapCallbackInfo map = default;
            WGPUPopErrorScopeCallbackInfo popErrorScope = default;
            return
            [
                (sizeof(WGPURequestAdapterCallbackInfo), (int)((byte*)&adapter.Mode - (byte*)&adapter), (int)((byte*)&adapter.Callback - (byte*)&adapter)),
                (sizeof(WGPURequestDeviceCallbackInfo), (int)((byte*)&device.Mode - (byte*)&device), (int)((byte*)&device.Callback - (byte*)&device)),
                (sizeof(WGPUBufferMapCallbackInfo), (int)((byte*)&map.Mode - (byte*)&map), (int)((byte*)&map.Callback - (byte*)&map)),
                (sizeof(WGPUPopErrorScopeCallbackInfo), (int)((byte*)&popErrorScope.Mode - (byte*)&popErrorScope), (int)((byte*)&popErrorScope.Callback - (byte*)&popErrorScope)),
            ];
        }

        public static (int Size, int MemoryHints, int BlockSizeStart) DeviceExtras()
        {
            WGPUDeviceExtras extras = default;
            byte* start = (byte*)&extras;
            return (
                sizeof(WGPUDeviceExtras),
                (int)((byte*)&extras.MemoryHints - start),
                (int)((byte*)&extras.SuballocatedDeviceMemoryBlockSizeStart - start));
        }
    }
}
