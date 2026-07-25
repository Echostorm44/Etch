# Golden-image corpus for Etch correctness tests

COR-006 — Skia golden-image oracle.

## Directory layout

```
<track>/<test>/<variant>.png   — Skia Skia reference PNGs
```

## Regeneration

Run the Skia reference generator to regenerate goldens:

```
dotnet run --project tools/etch-skia-ref/Etch.SkiaRef.csproj -- <scene.etsc> <output.png> [width] [height]
```

Guard: `ETCH_REGEN_GOLDENS=1` must be set for CI regeneration.
Skia uses deterministic settings (no thread-shim, seed=0, no hinting).

## Version

SkiaSharp version is pinned in `tools/etch-skia-ref/SkiaSharpVersion.txt`.
Bumping the version requires a deliberate PR and re-generation of all committed PNGs.
