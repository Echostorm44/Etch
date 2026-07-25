# Etch Kurbo Oracle (Native)

Rust cdylib wrapping [kurbo](https://github.com/linebender/kurbo) geometry operations for differential testing.

## Building locally

```bash
# Requires Rust toolchain (1.82+)
cargo build --release
```

Output: `target/release/etch_kurbo_oracle.dll` (Windows), `target/release/libetch_kurbo_oracle.so` (Linux), or `target/release/libetch_kurbo_oracle.dylib` (macOS).

## Rebuilding after kurbo version bump

1. Update `Cargo.toml` with the new kurbo version
2. Update `native-dependencies.md` with the new version and date
3. Run `cargo build --release` to verify
4. Commit and push

## Rebuilding via CI script

```powershell
# Build all RIDs
pwsh tools/ci/build-oracle.ps1

# Build specific RID
pwsh tools/ci/build-oracle.ps1 -Targets win-x64
```

## Extending the shim surface

When adding a new oracle function:

1. Add the `extern "C"` shim to `src/lib.rs` with `#[no_mangle]`
2. Add the `LibraryImport` declaration and managed wrapper to `tests/Etch.Geometry.Oracle/KurboOracle.cs`
3. Add a smoke test to `tests/Etch.Geometry.Oracle/OracleSmokeTests.cs`
4. Document the new function in this README

## Pinned kurbo version

See `Cargo.toml` for the current pinned version. Upgrade policy: pin unless a new primitive is needed.
