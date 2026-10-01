# Budget tables

Tracked copy of the budget tables in `ProjectPlan.md` ("Performance and memory targets"), which is
not checked in. The memory and performance regression tests parse this file so they run on every
checkout; `BudgetTablesTests.TrackedCopy_MatchesProjectPlan` fails wherever `ProjectPlan.md` exists
and its tables have drifted from these. Edit both together.

### Memory

| Scenario | Target | Notes |
|---|---|---|
| SimpleCascade idle (NativeAOT, Windows) | **< 20 MB** working set | The headline number. This is what developers see first. |
| SimpleCascade idle (macOS, Linux) | **< 25 MB** working set | Allows for platform overhead we don't fully control. |
| SimpleCascade at 60 fps animating | **< 35 MB** | Includes frame scratch. |
| Medium app (500 controls, dashboard) | **< 80 MB** | In the ballpark of Notepad++ with a real document. |
| Photoshop-class app baseline (renderer only) | **< 150 MB renderer-attributable** | Excludes the app's own document data, image buffers, history stack — that's the app developer's budget. The renderer contributes no more than 150 MB regardless of scene complexity at 1080p. |
| Per-frame managed allocations (any app) | **0 bytes** | Hard rule. CI-enforced. |

### Performance

| Scenario | Target | Machine class |
|---|---|---|
| SimpleCascade cold start to first frame | **< 300 ms** | Mid-tier laptop, NVMe |
| Input-to-photon latency (click → visual response) | **< 16 ms** | Anything modern |
| 1,000 filled AA paths, 1080p | **< 2 ms CPU + < 1 ms GPU** | Mid-tier laptop, integrated GPU |
| 10,000 filled AA paths, 1080p | **< 8 ms CPU + < 4 ms GPU** | Mid-tier laptop, integrated GPU |
| 10,000 glyphs rendered, 1080p | **< 5 ms CPU + < 2 ms GPU** | Mid-tier laptop, integrated GPU |
| 1,000 filled AA paths, CPU-only software mode | **< 12 ms** (83 fps) | Mid-tier laptop |
| 10,000 filled AA paths, CPU-only software mode | **< 30 ms** (33 fps) | Mid-tier laptop |
| Scene encoding throughput | **> 1M path ops/sec** per core | AOT, any modern CPU |
| Idle power (static UI, 60 Hz display) | **< 0.5 W renderer-attributable** | Laptop on battery |

### CPU-only Performance Targets (x64 CI Reference)

| Scenario | Target median | Allocation budget | Notes |
|---|---|---|---|
| 1080p red rect | **< 0.8 ms** | 1 alloc (framebuffer) | Single full-screen rect, solid fill |
| 1080p 1000 solid rects | **< 5 ms** | steady-state | Scattered opaque rects, strip pipeline |
| 1080p 1000 alpha-blended rects | **< 8 ms** | steady-state | Scattered rects, alpha = 0.5 |
| 1080p 500 anti-aliased paths (avg 100 verts) | **< 20 ms** | steady-state | Scattered bezier paths, strip pipeline |
| 1080p full-screen AA gradient | **< 12 ms** | steady-state | Placeholder until CPU-010 |

### GPU Performance Targets (GPU-011 Reference)

| Scenario | Target median | Notes |
|---|---|---|
| 1080p 1000 solid rects | **< 1 ms** | GPU time, strip pipeline |
| 1080p 5000 AA paths | **< 4 ms** | GPU time, strip pipeline |
| 4K 2000 AA paths | **< 10 ms** | GPU time, strip pipeline |
| 1080p 1000 linear gradients | **< 3 ms** | GPU time, [pending-GPU-013] |
| 1080p full-screen blur @ r=32 | **< 4 ms** | GPU time, [pending-GPU-017] |
