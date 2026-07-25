# Versioning

Etch follows Semantic Versioning 2.0.0 with platform-specific RID matrices and explicit deprecation policies.

## SemVer Mapping

| Version Component | Meaning for Etch |
|---|---|
| **Major** | Breaking API change. Scene format version bump. Binary `.etrp` incompatibility. |
| **Minor** | New features (new `SceneOpcode`, new blend mode, new pipeline). Backward-compatible scene and reproducer format. |
| **Patch** | Bug fixes. No API changes. No format changes. |

## RID Matrix

Each release ships per-platform native binaries:

| RID | Platform | GPU Backend |
|---|---|---|
| `win-x64` | Windows 10+ (x64) | Vulkan, DX12 |
| `linux-x64` | Ubuntu 22.04+ (x64) | Vulkan |
| `osx-arm64` | macOS 14+ (Apple Silicon) | Metal |
| `osx-x64` | macOS 14+ (Intel) | Metal |

## wgpu-native Version Pinning

- The `wgpu-native` library is pinned to a specific version in `native/wgpu-native/VERSION`.
- Upgrades follow a quarterly cadence per `docs/01-foundations/runbooks/wgpu-native-upgrade.md`.
- Bug-fix point releases may be taken opportunistically.
- Each upgrade re-runs the full pixel-diff and performance regression suites.
- Upgrades are never bundled with feature work. One dependency per PR.

## Deprecation Policy

| Status | Meaning |
|---|---|
| **Deprecated** | Marked `[Obsolete]` with message. Still functional. Removed in next major version. |
| **Removed** | Gone. Listed in release notes under **Breaking**. |
| **Renamed** | Old name deprecated for one major version, then removed. |

## Binary Format Compatibility

| Format | Versioning | Compatibility |
|---|---|---|
| `.etsc` (scene) | Major version in header | Forward: minor compat; backward: not guaranteed |
| `.etrp` (reproducer) | Same as `.etsc` | Must match major version |
| `.golden.png` (reference) | Per-task version pin | Regenerated on deliberate bump |

## Release Process

1. Update `CHANGELOG.md` with the release notes template.
2. Bump version in `Directory.Build.props`.
3. Tag: `git tag v{major}.{minor}.{patch}`.
4. CI builds, signs, and publishes NuGet packages.
5. GitHub release created from tag with auto-generated notes.
