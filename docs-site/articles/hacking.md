# Hacking Guide

Welcome to Etch development. This guide covers repo layout, the task-claiming workflow, analyzer development, and running the full local gate.

## Repo Layout

```
Etch/
├── src/                    # Production assemblies
│   ├── Etch.Abstractions/  # Errors, Panic, logging interfaces
│   ├── Etch.Primitives/    # Core types, pooling, Span utilities
│   ├── Etch.Geometry/      # Point, Vec2, Affine, Rect, BezPath
│   ├── Etch.Scene/         # SceneBuffer, SceneBuilder, Paint
│   ├── Etch.Tiling/        # Tile grid, classifier, strips
│   ├── Etch.Raster.Cpu/    # CPU rasterizer, blend modes, sRGB
│   ├── Etch.Gpu/           # wgpu wrappers (Instance, Device, etc.)
│   ├── Etch.Gpu.Native/    # Generated wgpu-native P/Invoke bindings
│   ├── Etch.Text/          # Text shaping, rasterization, atlas
│   ├── Etch.Testing/       # Test renderers, pixel diff, reporters
│   └── ...
├── tests/                  # Test projects (one per source project)
├── bench/                  # BenchmarkDotNet projects
├── tools/                  # CLI tools (Repro, Verifier, translators)
├── samples/                # Sample applications
├── docs/                   # Task specs (tracks 01-15)
├── docs-site/              # DocFX site (articles, API reference)
├── shaders/                # WGSL shader files
├── src-gen/                # Generated code (analyzers)
├── native/                 # Vendored native binaries (wgpu-native)
├── build/                  # Shared MSBuild props/targets
├── AGENTS.md               # Agent instructions (read first!)
├── _conventions.md          # Code conventions
└── ProjectPlan.md          # Architecture, memory/perf budgets
```

## Task-Claiming Flow

1. Open `docs/_status.md` — find a `pending` task whose `depends:` are all `done`.
2. Read the task spec in `docs/{track}/{TASK-ID}.md`.
3. Claim the task: change `pending` → `in-progress` with your agent ID and date.
4. Implement per the spec's **Deliverables** and **Acceptance Criteria**.
5. Update `_status.md` to `done` with a note describing what was built.
6. Run all affected test suites: `dotnet run --project tests/{project}/`.

## "Touches" Discipline

Each task spec has a `touches:` field listing directories the task is allowed to modify. Do not modify files outside `touches:` without updating the spec or getting approval. This is enforced by code review.

## Panic Code Allocation

Every internal failure path must route through `Panic.Invariant()` or siblings with a stable `ET-P-####` code:

```csharp
// In PanicCode.cs:
public static readonly PanicCode InvalidSurfaceSize = new("ET-P-0501");

// In your code:
Etch.Panic.Invariant(Etch.PanicCodes.InvalidSurfaceSize, "Dimensions must be positive");
```

Panic code format: `ET-P-{track}{seq}` where `track` is the 2-digit track number and `seq` is a 3-digit sequence within that track. The next available code per track is documented at the bottom of `PanicCode.cs`.

## Analyzer Development

### Adding a New Analyzer

1. Create `src-gen/Etch.Analyzers/{Name}Analyzer.cs`.
2. Register in `AnalyzerReleases.Shipped.md`.
3. Add tests in `src-gen/Etch.Analyzers.Tests/`.
4. The analyzer project uses Roslyn's `SyntaxNodeAction` / `OperationAction` pattern.

### Analyzer Code Allocation

Analyzers use `ET{track}{seq}` format (e.g., `ET0101`):

| Track | Code Range |
|---|---|
| 01 (Foundations) | ET0101–ET0199 |
| 02 (FFI) | ET0201–ET0299 |
| 04 (Scene) | ET0401–ET0499 |
| 05 (Tiling) | ET0501–ET0599 |
| 06 (Shaders) | ET0601–ET0699 |

### Common Pitfalls

- **Don't use `RegisterSyntaxNodeAction`** with generic type params in .NET 10 — use concrete type `RegisterSyntaxNodeAction<TypeDeclarationSyntax>`.
- **Diagnostic descriptors must be `public static readonly`**, not `const`.
- **Test harness**: use `VerifyCS.Diagnostic(analyzer).WithLocation(line, col)` for exact-position matching.

## Running the Full Local Gate

```bash
# Build everything
dotnet build

# Run all test suites (in order)
dotnet run --project tests/Etch.Primitives.Tests/
dotnet run --project tests/Etch.Geometry.Tests/
dotnet run --project tests/Etch.Scene.Tests/
dotnet run --project tests/Etch.Tiling.Tests/
dotnet run --project tests/Etch.Raster.Cpu.Tests/
dotnet run --project tests/Etch.ClipBlendGradient.Tests/
dotnet run --project tests/Etch.Text.Tests/
dotnet run --project tests/Etch.Samples.Tests/
dotnet run --project tests/Etch.Correctness.Tests/  # skip if GPU unavailable

# Run benchmarks (informational)
dotnet run --project bench/Etch.Bench.Cpu/ --configuration Release
```

## Common Pitfalls

| Pitfall | Fix |
|---|---|
| Ref struct across await boundary | Use synchronous helpers for path/scene building |
| `BezPathBuilder` after `await` | Extract path building to a separate sync method |
| GPU adapter unavailable in tests | Catch `EtchException` with `GpuAdapterUnavailable` |
| `CA2000` on GPU resources | Use `using` pattern or explicit `Dispose()` |
| `ET0105` analyzer blocks `Task.Run` | Use `Thread` or `SingleThreadedTileScheduler` |
| `IDE0055` formatting errors | Add to project's `NoWarn` |

## References

- `AGENTS.md` — Full agent instructions.
- `_conventions.md` — Code conventions and architecture decisions.
- `docs/00-overview/design-decisions.md` — All D-### decisions.
- `docs/01-foundations/analyzers.md` — Analyzer development guide.
