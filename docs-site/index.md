# Etch API Reference

Etch is a data-oriented 2-D renderer for .NET, built from the ground up for zero-allocation hot paths, AOT-native publication, and deterministic output across CPU and GPU backends.

## Getting started

- [HelloEtch sample](https://github.com/Echostorm/Etch/tree/main/samples/HelloEtch) — red triangle in < 100 lines
- [Integration guides](articles/README.md)

## Assembly reference

| Assembly | Description |
|---|---|
| `Etch.Gpu` | wgpu-native facade: devices, surfaces, pipelines, command encoding |
| `Etch.Scene` | Scene graph: `SceneBuilder`, `SceneBuffer`, opcodes, serialization |
| `Etch.Geometry` | Primitives: `Point`, `Vec2`, `Affine`, `BezPath`, flattening |
| `Etch.Tiling` | Tile classifier, strip emitter, raster orchestration |
| `Etch.Raster.Cpu` | CPU rasterizer: strip coverage, blending, SRGB encode |
| `Etch.Effects` | Image decode, blur, shadow, backdrop effects |
| `Etch.Text` | Glyph shaping, atlas packing, sub-pixel rasterization |
| `Etch.Strokes` | Stroke expansion: dash, join, cap |
| `Etch.ClipBlendGradient` | Clip masks, blend modes, gradient LUTs |
| `Etch.Primitives` | Logging, pooling, hashing, benchmarking primitives |

## Search

Use the search box above to find types, methods, and namespaces.
