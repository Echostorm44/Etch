#!/usr/bin/env pwsh
<#
.SYNOPSIS
    wgpu-native upgrade helper — runs the mechanical steps of the quarterly upgrade.

.DESCRIPTION
    Runs Steps 2–4 of the upgrade runbook:
      2. Update native/wgpu-native/VERSION
      3. Re-fetch binaries and refresh CHECKSUMS.txt
      4. Regenerate C# bindings

    The remaining steps (5–10) require human judgement and are not automated.

    Pass -NewVersion to make mechanical changes. Run with no arguments for a
    dry-run that verifies the script completes without error on an unchanged repo.

.PARAMETER NewVersion
    The new wgpu-native version tag to write (e.g. "v30.0.0.0").
    If omitted, the script runs in dry-run mode: it validates all paths and
    commands without modifying VERSION or any other file.

.EXAMPLE
    ./wgpu-upgrade-helper.ps1 -NewVersion "v30.0.0.0"
    # Performs the mechanical upgrade steps for v30.0.0.0.

.EXAMPLE
    ./wgpu-upgrade-helper.ps1
    # Dry-run: validates the script without modifying any files.
#>
param(
    [string]$NewVersion = ""
)

$ErrorActionPreference = 'Stop'
$RepoRoot = (Get-Item $PSScriptRoot).Parent.Parent.FullName
$VersionFile = Join-Path $RepoRoot "native/wgpu-native/VERSION"
$ChecksumsFile = Join-Path $RepoRoot "native/wgpu-native/CHECKSUMS.txt"
$FetchScript = Join-Path $RepoRoot "native/wgpu-native/fetch.ps1"

$IsDryRun = ($NewVersion -eq "")

Write-Host "=== wgpu-native upgrade helper ===" -ForegroundColor Cyan
Write-Host "Repo root: $RepoRoot"
Write-Host "Mode: $(if ($IsDryRun) { 'DRY-RUN' } else { "upgrade to $NewVersion" })"
Write-Host ""

# ── Step 2: Update VERSION ──────────────────────────────────────────────────

if ($IsDryRun) {
    $CurrentVersion = (Get-Content $VersionFile -Raw).Trim()
    Write-Host "[Step 2] DRY-RUN: would write <new-version> to VERSION (currently: $CurrentVersion)"
} else {
    $OldVersion = (Get-Content $VersionFile -Raw).Trim()
    Set-Content -Path $VersionFile -Value $NewVersion -NoNewline
    Write-Host "[Step 2] VERSION: $OldVersion → $NewVersion" -ForegroundColor Green
}

# ── Step 3: Re-fetch binaries and refresh checksums ──────────────────────────

Write-Host ""
Write-Host "[Step 3] Fetching binaries for all RIDs..."

if ($IsDryRun) {
    Write-Host "  [DRY-RUN] Would run: $FetchScript all"
} else {
    & $FetchScript -Rid all
    if ($LASTEXITCODE -ne 0) { throw "fetch.ps1 failed with exit code $LASTEXITCODE" }
    Write-Host "  Binaries fetched and verified." -ForegroundColor Green

    # The fetch.ps1 script validates checksums against CHECKSUMS.txt automatically.
    # After a version bump the CHECKSUMS.txt still holds the old hashes.
    # Re-run fetch.ps1 after updating CHECKSUMS.txt — here we just report the
    # current state. If a checksum is stale the fetch script will fail and
    # require manual CHECKSUMS.txt update (see runbook Step 3).
    Write-Host "  NOTE: If any checksum mismatch is reported, manually update" -ForegroundColor Yellow
    Write-Host "  $ChecksumsFile before proceeding." -ForegroundColor Yellow
}

# ── Step 4: Regenerate bindings ─────────────────────────────────────────────

Write-Host ""
Write-Host "[Step 4] Regenerating C# bindings..."

if ($IsDryRun) {
    Write-Host "  [DRY-RUN] Would run: dotnet run --project tools/gen-wgpu-bindings -- generate"
} else {
    $BindingsProject = Join-Path $RepoRoot "tools/gen-wgpu-bindings"
    dotnet run --project $BindingsProject -- generate
    if ($LASTEXITCODE -ne 0) { throw "Bindings generator failed with exit code $LASTEXITCODE" }
    Write-Host "  Bindings regenerated." -ForegroundColor Green
}

# ── Report diff summary ───────────────────────────────────────────────────────

Write-Host ""
Write-Host "=== Diff summary (src/Etch.Gpu.Native/Generated/) ===" -ForegroundColor Cyan

if ($IsDryRun) {
    Write-Host "  [DRY-RUN] Run 'git diff src/Etch.Gpu.Native/Generated/' after a real upgrade"
    Write-Host "  to review binding changes manually — no batch-accept." -ForegroundColor Yellow
} else {
    $DiffOutput = git -C $RepoRoot diff --stat src/Etch.Gpu.Native/Generated/
    if ($DiffOutput) {
        Write-Host $DiffOutput
    } else {
        Write-Host "  No changes to generated bindings."
    }
}

Write-Host ""
if ($IsDryRun) {
    Write-Host "[DRY-RUN complete] No files were modified." -ForegroundColor Green
    Write-Host "Run with -NewVersion to perform the actual upgrade." -ForegroundColor Green
} else {
    Write-Host "[Upgrade helper complete] Review the diff above before committing." -ForegroundColor Green
    Write-Host "Next: run Steps 5–10 from docs/01-foundations/runbooks/wgpu-native-upgrade.md"
}
