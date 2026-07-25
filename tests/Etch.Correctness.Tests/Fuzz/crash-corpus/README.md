# Scene Fuzz Crash Corpus

This directory holds minimized reproducer inputs for scene-fuzz crashes.

Each file is a raw byte array that, when fed to `SceneFuzzDecoder.Decode`,
produces a scene that violates one of the fuzz invariants:

1. Unhandled non-`EtchException` during decode or render.
2. Output pixel values outside `[0, 255]` (NaN / Inf in the render pipeline).
3. Allocation exceeding 128 MB per iteration.

## Naming convention

```
crash-<invariant>-<hash>.bin
```

- `invariant`: `unhandled`, `badpixel`, or `alloc`
- `hash`: first 8 hex chars of SHA-256 of the input bytes

## Adding a crash

When `SceneFuzzTests` finds a crash, it should write the reproducer here
and fail the test with the file path. The nightly fuzz lane commits new
crashes automatically.

## Minimization

Before committing, minimize the reproducer with greedy byte removal:
run `SceneFuzzDecoder.Decode` + render; if the crash still reproduces,
keep the byte removed. Repeat until no single byte can be removed.
