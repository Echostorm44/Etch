# Using Etch Standalone

This guide walks through building a standalone application with Etch — no CascadeUI required. You'll learn the scene API, lifecycle management, threading model, and zero-allocation rendering patterns.

## 5-Minute Quickstart

Build a minimal app that renders a filled rectangle:

```csharp
using Etch;
using Etch.Geometry;
using Etch.Scene;
using Etch.Testing;

// Build a scene with a red rectangle
var builder = SceneBuilder.Begin();
builder.BeginFrame();

int identity = builder.AddTransform(Affine.Identity);
int redPaintId = builder.AddPaint(Paint.Solid(0xFFFF0000u));

builder.FillRect(new Rect(50, 50, 200, 150), redPaintId, identity);
builder.EndFrame();

var scene = builder.End();

// Render to RGBA8 bytes
byte[] pixels = SceneRunner.RunCpu(scene, 640, 480);
// pixels is BGRA-ordered (B at idx+0, G at idx+1, R at idx+2, A at idx+3)
```

## The Scene API

### Building Scenes

`SceneBuilder` is a ref struct. Every scene must be bracketed by `BeginFrame()`/`EndFrame()`:

```csharp
var builder = SceneBuilder.Begin();
builder.BeginFrame();

// --- Add commands here ---

builder.EndFrame();
var scene = builder.End(); // builds SceneBuffer
```

### Fills

Solid fills with optional blend modes:

```csharp
// Normal blend (default)
int redPaint = builder.AddPaint(Paint.Solid(0xFFFF0000u));

// Multiply blend
int blendPaint = builder.AddPaint(Paint.Solid(0xFF00FF00u, blendModeId: (byte)BlendMode.Multiply));

// Rectangles
builder.FillRect(new Rect(10, 10, 100, 50), redPaint, identity);

// Paths (Bezier curves)
using var pb = BezPathBuilder.Begin();
pb.MoveTo(new Point(50, 20));
pb.LineTo(new Point(200, 200));
pb.LineTo(new Point(20, 200));
pb.Close();
int pathId = builder.AddPath(pb.Build());
builder.FillPath(pathId, redPaint, identity, FillRule.NonZero);
```

### Transforms

Transforms stack: use `SetTransform` for persistent transforms, or the per-command transform for one-off:

```csharp
int translateId = builder.AddTransform(Affine.Translate(new Vec2(100, 50)));
builder.FillRect(new Rect(0, 0, 50, 50), redPaint, translateId);
// Renders at (100, 50)
```

### Supported Blend Modes

All 16 W3C compositing blend modes: Normal, Multiply, Screen, Overlay, Darken, Lighten, ColorDodge, ColorBurn, HardLight, SoftLight, Difference, Exclusion, Hue, Saturation, Color, Luminosity.

```csharp
int huePaint = builder.AddPaint(Paint.Solid(0xFF8000FFu, blendModeId: (byte)BlendMode.Hue));
```

## Lifecycle

### GPU Device

Create a device once, reuse across frames:

```csharp
using var instance = Instance.Create();
var (status, adapter) = AsyncRequest.RequestAdapterSync(instance);
var (ds, device) = AsyncRequest.RequestDeviceSync(instance, adapter);
// Use device for multiple frames...
device.Dispose();
adapter.Dispose();
```

### Window Surface

Platform-specific surfaces feed the swapchain:

| Platform | Surface Creation |
|---|---|
| Windows | `Surface.CreateWin32(instance, hwnd)` |
| macOS | `Surface.CreateMetal(instance, metalLayer)` |
| Linux X11 | `Surface.CreateXlib(instance, display, window)` |
| Linux Wayland | `Surface.CreateWayland(instance, display, surface)` |

## Threading Model

- **Scene building**: Any thread. `SceneBuilder` is a ref struct (stack-only).
- **CPU rendering**: Scalar renderer is single-threaded. Use `ParallelClassifier` for multi-threaded classification.
- **GPU rendering**: Device commands are serialized through `Queue.Submit`. Not thread-safe — call from one thread.
- **Analyzers**: `ET0105` bans `Task.Run` and `Parallel.For` in draw-path code.

## Resource Management

### Disposal Pattern

All GPU resources implement `IDisposable`. Use `using` blocks:

```csharp
using var texture = device.CreateTexture(desc);
using var buffer = device.CreateBuffer(desc);
```

### Render Caches

Pre-compute classification and strips for zero-alloc per-frame:

```csharp
using var cache = new SceneCpuRenderer.CpuRenderCache(scene, 800, 600);
for (int frame = 0; frame < 1000; frame++)
{
    byte[] result = cache.Render();
    // Display result...
}
```

## Zero-Alloc Patterns

### Measuring Allocations

```csharp
long before = GC.GetTotalAllocatedBytes(precise: false);
// ... render loop ...
long after = GC.GetTotalAllocatedBytes(precise: false);
Assert.That(after).IsEqualTo(before); // zero alloc after warm-up
```

### Memory Budgets

| Scenario | Budget |
|---|---|
| SimpleCascade idle (Windows) | < 20 MB |
| Medium app (500 controls) | < 80 MB |
| Per-frame managed allocations | 0 bytes |

## Error Handling

All internal failures route through `Panic.Invariant` / `Panic.NotImplemented` etc., throwing `EtchException` with a stable `ET-P-####` code. Catch at the application boundary:

```csharp
try
{
    byte[] pixels = SceneRunner.RunGpu(scene, w, h);
}
catch (EtchException ex)
{
    Console.WriteLine($"Etch panic: {ex.Code} — {ex.Message}");
}
```

## Next Steps

- See the [CascadeUI integration guide](cascade-integration.md) for Cascade-specific patterns.
- Browse the [API reference](../api/) for detailed type documentation.
- Read the [performance methodology guide](performance-methodology.md) for benchmarking.
