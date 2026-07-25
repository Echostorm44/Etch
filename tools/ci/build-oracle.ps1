# Builds the kurbo cdylib for all supported RIDs.
# Copies output to tests/Etch.Geometry.Oracle/runtimes/<rid>/native/
param(
    [ValidateSet("all", "win-x64", "linux-x64", "osx-arm64")]
    [string[]] $Targets = @("all")
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent

foreach ($target in $Targets) {
    if ($target -eq "all") {
        & $PSCommandPath -Targets "win-x64", "linux-x64", "osx-arm64"
        continue
    }

    Write-Host "Building kurbo oracle for $target..."

    $cargoTarget = switch ($target) {
        "win-x64"    { "x86_64-pc-windows-msvc" }
        "linux-x64"  { "x86_64-unknown-linux-gnu" }
        "osx-arm64"  { "aarch64-apple-darwin" }
    }

    $cdylibName = switch ($target) {
        "win-x64"    { "etch_kurbo_oracle.dll" }
        "linux-x64"  { "libetch_kurbo_oracle.so" }
        "osx-arm64"  { "libetch_kurbo_oracle.dylib" }
    }

    $srcDir = "$RepoRoot\tests\Etch.Geometry.Oracle.Native"
    $targetDir = "$RepoRoot\tests\Etch.Geometry.Oracle\runtimes\$target\native"

    if (-not (Test-Path $srcDir)) {
        Write-Error "Oracle native source not found at $srcDir"
    }

    $env:CARGO_TARGET_DIR = "$srcDir\target"
    cargo build --release --manifest-path "$srcDir\Cargo.toml" --target $cargoTarget 2>&1 | ForEach-Object { Write-Host "  $_" }
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Cargo build failed for $target"
    }

    $srcDll = switch ($target) {
        "win-x64"    { "$srcDir\target\$cargoTarget\release\$cdylibName" }
        "linux-x64"  { "$srcDir\target\$cargoTarget\release\$cdylibName" }
        "osx-arm64"  { "$srcDir\target\$cargoTarget\release\$cdylibName" }
    }

    if (-not (Test-Path $srcDll)) {
        Write-Error "Built cdylib not found at $srcDll"
    }

    New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
    Copy-Item $srcDll "$targetDir\$cdylibName" -Force
    Write-Host "  Copied to $targetDir\$cdylibName"
}

Write-Host "Done."
