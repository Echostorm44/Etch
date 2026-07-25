using System;
using System.Runtime.InteropServices;
using System.Threading;
using Etch.Gpu;
using Etch.Gpu.Native;
using Etch.Gpu.SwapChains;

namespace HelloEtch;

// ═══════════════════════════════════════════════════════════════════════════
// HelloEtch — M0 gate artifact (SMP-001).
//
// Pipeline: instance → adapter → device → HWND surface → swap chain
// → WGSL shader → render pipeline → render loop.
//
// Close the window or press Esc to exit.
// ═══════════════════════════════════════════════════════════════════════════

internal static unsafe class Program
{
    private static void Main()
    {
        Console.WriteLine("HelloEtch — Red Triangle Sample");

        Instance instance = Instance.Create();
        if (instance.IsInvalid)
        {
            Console.WriteLine("Instance creation failed.");
            return;
        }

        (BackendType BackendType, string Name)[] backends =
        {
            (BackendType.Undefined, "Auto"),
            (BackendType.Vulkan, "Vulkan"),
            (BackendType.D3D12, "D3D12"),
            (BackendType.OpenGL, "OpenGL"),
            (BackendType.Metal, "Metal"),
        };

        Adapter adapter = default;
        foreach (var backend in backends)
        {
            Console.WriteLine($"Trying {backend.Name} backend...");
            var result = AsyncRequest.RequestAdapterSync(instance, backendType: backend.BackendType);
            Console.WriteLine($"  status={result.Status}, valid={!result.Adapter.IsInvalid}");

            if (result.Status == RequestAdapterStatus.Success && !result.Adapter.IsInvalid)
            {
                adapter = result.Adapter;
                Console.WriteLine($"  → using {backend.Name}");
                break;
            }
        }

        if (adapter.IsInvalid)
        {
            Console.WriteLine("No adapter available on any backend.");
            instance.Dispose();
            return;
        }

        var deviceResult = AsyncRequest.RequestDeviceSync(instance, adapter);
        Console.WriteLine($"Device: status={deviceResult.Status}, valid={!deviceResult.Device.IsInvalid}");

        if (deviceResult.Status != RequestDeviceStatus.Success || deviceResult.Device.IsInvalid)
        {
            Console.WriteLine("Device request failed.");
            adapter.Dispose();
            instance.Dispose();
            return;
        }

        Device device = deviceResult.Device;

#if WINDOWS
        using var window = Win32Window.Create(640, 480, "HelloEtch");
        IntPtr hinstance = GetModuleHandleW(IntPtr.Zero);

        using Surface surface = SurfaceFactory.CreateFromWin32(instance, window.Handle, hinstance, "HelloEtch Surface");
        Console.WriteLine($"Surface: valid={surface.IsValid}");
        if (!surface.IsValid)
        {
            device.Dispose();
            adapter.Dispose();
            instance.Dispose();
            return;
        }

        var swapChainConfig = new SwapChainConfig
        {
            Format = TextureFormat.Bgra8Unorm,
            Width = 640,
            Height = 480,
            PresentMode = PresentMode.Fifo,
            AlphaMode = CompositeAlphaMode.Auto,
            Usage = TextureUsage.RenderAttachment,
        };
        var swapChain = SwapChain.Configure(device, surface, swapChainConfig);
        Console.WriteLine("Swap chain configured.");

        using var renderer = new HelloEtchRenderer(device, swapChainConfig.Format);
        Console.WriteLine("Renderer ready. Close the window to exit.");

        while (Win32Window.PumpMessages())
        {
            device.Poll(false);

            var status = swapChain.AcquireFrame(out SurfaceTexture frame);
            if (status != SurfaceTextureResult.Ok || !frame.IsValid)
            {
                if (status == SurfaceTextureResult.Outdated || status == SurfaceTextureResult.Lost)
                {
                    swapChain.Resize(swapChainConfig.Width, swapChainConfig.Height);
                }
                Thread.Sleep(4);
                continue;
            }

            renderer.Render(new TextureView(frame.View));
            swapChain.Present(frame);
        }

        swapChain.Dispose();
#else
        Console.WriteLine("Windowed mode is only implemented on Windows in this sample.");
        Console.WriteLine("Run the headless smoke test (Etch.Samples.Tests) for cross-platform verification.");
#endif

        device.Dispose();
        adapter.Dispose();
        instance.Dispose();
        Console.WriteLine("Done.");
    }

#if WINDOWS
    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(IntPtr lpModuleName);
#endif
}
