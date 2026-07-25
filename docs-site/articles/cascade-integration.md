# Using Etch from CascadeUI

This guide covers integrating the Etch 2D renderer into a CascadeUI application — wiring the project, creating surfaces, submitting scenes, and handling lifecycle events.

## Prerequisites

Install the Etch NuGet package and its platform-specific native dependencies:

```xml
<PackageReference Include="Echostorm.Etch" Version="0.1.0" />
<PackageReference Include="Echostorm.Etch.NativeAssets.Win32" Version="0.1.0" Condition="$([MSBuild]::IsOSPlatform('Windows'))" />
```

## Project Wiring

Create a device and swapchain. The `SimpleCascade` sample demonstrates the minimal setup:

```csharp
using Etch;
using Etch.Gpu;
using Etch.Scene;
using Etch.Testing;

// Create GPU instance and adapter
using var instance = Instance.Create();
var (status, adapter) = AsyncRequest.RequestAdapterSync(instance);
using var device = AsyncRequest.RequestDeviceSync(instance, adapter).Device;

// Create a surface from your window handle
var surface = Surface.CreateWin32(instance, hwnd);
var swapChain = device.CreateSwapChain(surface, new SwapChainDescriptor { Width = 800, Height = 600 });
```

## Surface Creation

Create a surface from your window handle:

```csharp
// Windows (Win32 HWND)
var surface = Surface.CreateWin32(instance, hwnd);

// macOS (Metal layer)
var surface = Surface.CreateMetal(instance, metalLayer);

// Linux (X11)
var surface = Surface.CreateXlib(instance, display, window);
```

## Scene Submission

Build and submit a scene for rendering:

```csharp
var builder = SceneBuilder.Begin();
builder.BeginFrame();

int identity = builder.AddTransform(Affine.Identity);
int red = builder.AddPaint(Paint.Solid(0xFFFF0000));

// Draw a filled rectangle
builder.FillRect(new Rect(10, 10, 200, 100), red, identity);

builder.EndFrame();
var scene = builder.End();

// Render via CPU path
byte[] pixels = SceneRunner.RunCpu(scene, 800, 600);

// Or GPU path (requires device + swapchain)
byte[] pixels = SceneRunner.RunGpu(scene, 800, 600);
```

## Lifecycle Management

### Device Lost

Handle device loss by recreating the device and all dependent resources:

```csharp
try
{
    var result = await device.PollAsync();
    if (result == PollResult.DeviceLost)
    {
        device.Dispose();
        device = await CreateDeviceAsync(instance, adapter);
        // Recreate swapchain, pipelines, bind groups...
    }
}
catch (EtchException ex) when (ex.Code == Etch.PanicCodes.GpuDeviceLost)
{
    // Handle device loss
}
```

### Swapchain Resize

Respond to window resize events:

```csharp
void OnResize(int newWidth, int newHeight)
{
    swapChain.Resize(newWidth, newHeight);
}
```

## Memory Budget

The renderer targets:
- **< 20 MB** working set for SimpleCascade on Windows (NativeAOT).
- **0 bytes** per-frame managed allocations after warm-up.
- Use `GC.GetTotalAllocatedBytes(precise: false)` to verify zero-alloc paths.

## Zero-Alloc Patterns

Pre-compute tile classification and strips once, then render repeatedly:

```csharp
using var cache = new SceneCpuRenderer.CpuRenderCache(scene, 800, 600);
for (int i = 0; i < 100; i++)
{
    byte[] frame = cache.Render(); // zero managed alloc after warm-up
}
```

## Troubleshooting

| Symptom | Likely Cause | Fix |
|---|---|---|
| Black screen | Missing `BeginFrame()/EndFrame()` | Ensure every scene is bracketed |
| `GpuAdapterUnavailable` | No Vulkan/DX12/Metal driver | Install graphics driver or use `--cpu` |
| `GpuDeviceLost` | Driver crash or timeout | Handle and recreate device |
| `InvalidSurfaceSize` | Width or height ≤ 0 | Check resize event sends positive values |
| `NotImplemented` | Called a GPU feature not yet built | Check `docs/_status.md` for feature status |
| Memory grows over time | Missing `Dispose()` on GPU resources | Use `using` blocks or explicit disposal |
