# Scene Reproducer Guide

When you hit a rendering bug, Etch can capture the exact scene, GPU state, and machine profile needed to reproduce the issue — no guesswork required.

## Quick Start

### Dump a Scene

Captures the full `SceneBuffer` to a `.etrp` file:

```bash
dotnet run --project tools/Etch.Repro/Etch.Repro.csproj -- dump --scene my-scene.etsc --out crash.etrp
```

### Replay a Scene

Replays a reproducer file byte-identically:

```bash
dotnet run --project tools/Etch.Repro/Etch.Repro.csproj -- replay crash.etrp
```

## Scene Dump Format

`.etrp` files contain:
- **Scene payload**: Binary ETSC-encoded `SceneBuffer` (the full draw command list).
- **GPU validation log**: `ValidationLogRing` output (256-entry circular buffer of validation messages).
- **Adapter info**: GPU vendor, device name, backend type (Vulkan/DX12/Metal).
- **Surface config**: Resolution, format, sample count.
- **Machine profile**: OS version, CPU info, GPU driver version.

## Trace Capture

For GPU-level issues, capture a RenderDoc trace alongside the reproducer:

1. Launch your app under RenderDoc.
2. Reproduce the bug.
3. Capture the frame in RenderDoc.
4. Save the `.rdc` capture.
5. Attach both `.etrp` and `.rdc` to the bug report.

## Machine Profiling

Generate a machine profile JSON:

```bash
dotnet run --project tools/Etch.Repro/Etch.Repro.csproj -- profile --out machine.json
```

This produces:
```json
{
  "osVersion": "Windows 11 (10.0.22631)",
  "cpu": "AMD Ryzen 7 5800X",
  "gpuAdapter": "NVIDIA GeForce RTX 3060",
  "gpuBackend": "Vulkan",
  "gpuDriver": "545.84",
  "ramGB": 32
}
```

## Privacy Guidance

Reproducer files contain no PII (Personally Identifiable Information):
- **No window titles, desktop paths, or environment variables**.
- **No user document data** — only the Etch scene commands.
- **GPU adapter names ARE included** — needed for backend-specific bugs. If this is sensitive, scrub the adapter name before sharing.
- **Machine profiles exclude usernames, hostnames, and IP addresses**.

## How to Replay

### Byte-Identical Replay

```bash
dotnet run --project tools/Etch.Repro/Etch.Repro.csproj -- replay crash.etrp --backend Vulkan
```

The replay:
1. Deserializes the `SceneBuffer` from the `.etrp`.
2. Creates a fresh GPU device on the specified backend.
3. Renders the scene through the full pipeline.
4. Outputs a pixel-diff against the embedded reference (if present).

### Differential Replay

Compare the replay output against a known-good reference:

```bash
dotnet run --project tools/Etch.Repro/Etch.Repro.csproj -- replay crash.etrp --diff reference.png
```

## Filing a Bug

Use the [Rendering Bug template](https://github.com/anomalyco/Etch/issues/new?template=rendering-bug.md) on GitHub. Attach:
1. The `.etrp` reproducer file.
2. (Optional) A RenderDoc `.rdc` capture.
3. Your machine profile JSON.
