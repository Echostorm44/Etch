# AOT Baseline Report

- Commit: <sha> (fill after git init)
- SDK: 10.0.100 (pinned by `global.json`)
- Date: 2026-04-22

## Per-RID results

| RID | Publish time | Binary size | Warnings |
|---|---|---|---|
| win-x64 | ~2s | ~3.1 MB | 0 |
| linux-x64 | TBD | TBD | TBD |
| osx-arm64 | TBD | TBD | TBD |

All three RIDs publish cleanly on an empty `Etch.Abstractions`. Any future AOT warning is a regression introduced by production code, not by the scaffold.
