# Installs the pinned naga-cli (build/NagaCli.props <NagaCliVersion>) into the cargo bin directory.
# gfx-rs/naga publishes no prebuilt CLI binaries, so it is built from crates.io with cargo; Rust is
# preinstalled on GitHub runners. Idempotent: a naga already on PATH or in ~/.cargo/bin at the pinned
# version (e.g. restored from the CI cache) is kept, any other version is replaced.
#   -PrintVersion  writes the pinned version and exits (CI uses it in the cache key).
param(
    [switch]$PrintVersion
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent

$propsPath = Join-Path $RepoRoot "build/NagaCli.props"
$pinnedVersion = (Select-Xml -Path $propsPath -XPath "//NagaCliVersion").Node.InnerText.Trim()
if (-not $pinnedVersion) {
    throw "NagaCliVersion not found in $propsPath"
}

if ($PrintVersion) {
    Write-Output $pinnedVersion
    exit 0
}

$cargoBin = Join-Path $HOME ".cargo/bin"
$nagaFileName = if ($IsWindows) { "naga.exe" } else { "naga" }

function Find-Naga {
    $onPath = Get-Command naga -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($onPath) {
        return $onPath.Source
    }
    $inCargoBin = Join-Path $cargoBin $nagaFileName
    if (Test-Path $inCargoBin) {
        return $inCargoBin
    }
    return $null
}

$existing = Find-Naga
if ($existing) {
    # `naga --version` prints "<version>" on its own line (e.g. "29.0.0").
    $installedVersion = (& $existing --version 2>$null | Select-Object -First 1)
    if ($installedVersion -and $installedVersion.Trim().EndsWith($pinnedVersion)) {
        Write-Host "naga-cli $pinnedVersion already installed: $existing"
        exit 0
    }
    Write-Host "Found naga-cli '$installedVersion' at $existing; installing pinned $pinnedVersion"
}

Write-Host "Installing naga-cli $pinnedVersion via cargo..."
cargo install naga-cli --version $pinnedVersion --locked --force
if ($LASTEXITCODE -ne 0) {
    throw "cargo install naga-cli $pinnedVersion failed with exit code $LASTEXITCODE"
}

$installed = Find-Naga
if (-not $installed) {
    throw "naga-cli installed but not found on PATH or in $cargoBin"
}
Write-Host "naga-cli installed: $installed ($(& $installed --version))"
