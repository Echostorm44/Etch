---
name: Rendering Bug
about: Report a visual rendering issue (incorrect output, crash, panic)
title: "[Rendering] "
labels: bug, rendering
assignees: ''
---

## Description

<!-- Brief description of the visual artifact or crash -->

## Steps to Reproduce

1.
2.
3.

## Expected Behavior

<!-- What should the renderer have produced? -->

## Actual Behavior

<!-- Screenshots, pixel-diff output, or panic message -->

## Environment

- OS: <!-- e.g., Windows 11, Ubuntu 24.04, macOS 14 -->
- GPU: <!-- e.g., NVIDIA RTX 3060, Apple M2 Pro -->
- Backend: <!-- e.g., Vulkan, DX12, Metal -->
- Etch version: <!-- commit hash or NuGet version -->

## Reproducer

<!-- Attach .etrp file (generated via tools/Etch.Repro) -->
<!-- Optional: RenderDoc .rdc capture -->

## Machine Profile

```json
<!-- Output of: dotnet run --project tools/Etch.Repro -- profile -->
```
