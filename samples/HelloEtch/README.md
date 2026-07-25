# HelloEtch

The smallest possible end-to-end Etch proof: a native window, an `Etch.Gpu`
device, one draw call, a red triangle. This is the **M0 milestone gate**
artifact (SMP-001).

## Quick start

```bash
dotnet run -c Release
```

A 640×480 window opens showing a red triangle on a dark-blue background.
Close the window to exit.

## Headless smoke test

The same renderer is exercised without a window in
`tests/Etch.Samples.Tests/HelloEtchSmokeTests.cs`.  It renders one frame
to an off-screen texture, reads back the pixels, and asserts that the
center pixel is red.

```bash
dotrun run --project tests/Etch.Samples.Tests
```

## AOT publish

```bash
dotnet publish -c Release -r win-x64 -p:PublishAot=true
```

Produces a single native binary (target ≤ 15 MB).

## Troubleshooting

**Missing `wgpu_native.dll`** — The build target copies the native DLL to
`$(OutDir)` automatically. If it is missing, ensure the `CopyNativeBinaries`
MSBuild target ran and that `native/wgpu-native/win-x64/lib/wgpu_native.dll`
exists in the repo.

**No adapter available** — Etch tries Vulkan, D3D12, OpenGL, and Metal in
that order. If none succeed, verify your GPU drivers and that a compatible
backend is installed.
