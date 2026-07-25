# RenderDoc Playbook

RenderDoc is the primary GPU debugging tool for Etch. This guide covers installation, capture, and investigation of render passes.

## Installation

1. Download RenderDoc from [renderdoc.org](https://renderdoc.org/).
2. Install the latest stable release (≥ v1.30 recommended).
3. Add RenderDoc to your PATH or use the full path to `renderdoccmd.exe`.

## Attaching to an Etch Sample

### Method 1: Launch via RenderDoc UI

1. Open RenderDoc.
2. **File → Launch Application**.
3. Set **Executable Path** to `dotnet.exe`.
4. Set **Working Directory** to your Etch sample directory.
5. Set **Command Line Arguments** to `run --project samples/FilledCircle/FilledCircle.csproj`.
6. Click **Launch**.

### Method 2: Inject into Running Process

1. Run your Etch sample normally.
2. In RenderDoc: **File → Attach to Running Instance**.
3. Select the `FilledCircle.exe` (or `dotnet.exe`) process.
4. Press **F12** or click **Capture Frame** to capture.

## Reading the Event List

Etch GPU commands are labeled for readability in the event browser:

| Label Pattern | Meaning |
|---|---|
| `SolidFill: #N` | The N-th solid rectangle draw call |
| `TileQuad: tile (x,y)` | Tile-quad for tile at grid position (x,y) |
| `StripAtlas upload` | Strip coverage atlas upload to GPU |
| `UniformRing write` | Per-draw uniform buffer update |

## Common Investigation Scenarios

### Scenario 1: "My shapes aren't appearing"

1. Capture a frame.
2. Open the **Texture Viewer** and select the color output.
3. Check the **Overlay** dropdown → **Draw calls**.
4. Step through draw calls. If none appear, check:
   - Scene commands were processed (no `NotImplemented` panics).
   - `BeginFrame()`/`EndFrame()` are bracketed.
   - The paint is `Solid` (gradient/image paints need GPU-010).

### Scenario 2: "Colors look wrong"

1. Select a draw call in the event browser.
2. Open the **Pipeline State** tab → **Fragment Shader**.
3. Check the uniform buffer contents: `PerDrawData.color` should have correct RGBA values.
4. Verify the color format is `Bgra8UnormSrgb` (not swapped channels).

### Scenario 3: "Performance is unexpectedly slow"

1. Open **Window → Timeline**.
2. Look for gaps between draw calls — indicates CPU-side waits.
3. Check **Statistics** for draw call count. More draws = more overhead.
4. Check **Texture** sizes — oversized render targets waste bandwidth.

### Scenario 4: "GPU validation error"

1. Open **Window → Validation**.
2. Review validation messages from `wgpu-native`.
3. Common errors:
   - Bind group mismatch: uniform buffer size < declared `MinBindingSize`.
   - Texture usage flags missing `RenderAttachment` or `CopySrc`.
   - Pipeline layout doesn't match shader bind group declarations.

## Labels Reference

The debug label scheme from FFI-008:

| Resource | Label Format |
|---|---|
| Instance | `"Etch Instance"` |
| Adapter | `"Etch Adapter {name}"` |
| Device | `"Etch Device {name}"` |
| Pipeline | `"SolidFillPipeline"` |
| BindGroup | `"PerDraw BindGroup"` |
| Texture | `"Offscreen {w}x{h} Bgra8"` |
| Buffer | `"Uniform_Ring[{i}]"` |

## Tips

- **Capture only what you need**: Large scenes generate large captures. Use F12 sparingly.
- **Compare frames**: RenderDoc's **Image Compare** tool diffs two captures pixel-by-pixel.
- **Shader debugging**: Right-click any pixel in the Texture Viewer → **Debug Pixel** to step through the fragment shader.
