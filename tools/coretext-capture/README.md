# CoreText reference capture (WP-3534)

Captures a **CoreText** (macOS native text engine) reference for the text-quality
benchmark — the macOS arbiter for "is Etch's dark-on-light weight macOS-faithful or
an overshoot?". CoreText only runs on macOS, so this is a **stored corpus**: capture
once on a Mac, commit the JSON, and the Windows benchmark grades against it.

## What it measures

The exact same metric the benchmark uses for Etch/Skia/DirectWrite: mean displayed
ink (`1 − luminance`) over inked pixels (`> 0.04`) for black text on white, per glyph,
averaged over the corpus `HMEOBRAWNhmeobgjqpadtsn0258` at sizes
`{9,11,13,16,20,24,32,48}` pt (rendered at the benchmark's 96/72 raster scale).

## Run it (on a Mac)

Needs only the Command Line Tools (`xcode-select --install`) — no Xcode project, no
Swift. **macOS 10.14 (Mojave) or later recommended**: Mojave dropped subpixel-AA, so
10.14+ reflects modern macOS grayscale rendering. The OS version is recorded in the
JSON so the reference is interpreted correctly (an older OS is still usable, just
heavier/subpixel — note it in the verdict).

```sh
cd tools/coretext-capture
./capture.sh                     # downloads the same Roboto-Regular + captures
# or: ./capture.sh /path/to/Roboto-Regular.ttf
```

This writes `CoreTextCorpus.json`. Commit it to close the loop:

```sh
cp CoreTextCorpus.json ../../tests/Etch.Text.Tests/TextParity/CoreTextCorpus.json
git add tests/Etch.Text.Tests/TextParity/CoreTextCorpus.json
git commit -m "WP-3534: CoreText reference corpus (captured on <macOS version>)"
```

## What happens next

The benchmark (`TextQualityBenchmark`) auto-detects the committed corpus and adds a
**"Weight vs CoreText"** section to `text-quality-benchmark.md`: a per-size Etch vs
CoreText table plus a verdict (macOS-faithful / overshoot / under-weighted). Until the
corpus is present the benchmark runs unchanged and the section prints a "not present"
note. Files:

- `coretext_capture.m` — the capture program (CoreText + CoreGraphics, no deps).
- `capture.sh` — build + run helper.
- `tests/Etch.Text.Tests/TextParity/TextQualityBenchmark.cs` — `LoadCoreTextCorpus` +
  the grading section.
