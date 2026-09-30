# Runs every TUnit test project under tests/ against an existing Release build.
# On software-GPU environments (ETCH_SOFTWARE_GPU=1, i.e. headless Linux CI) the suites that
# open a wgpu device are skipped: wgpu offscreen rendering segfaults against lavapipe there, and
# the real GPU path is covered by the Windows (WARP/hardware) and macOS (Metal) legs.
param(
    [string]$Configuration = "Release",
    [string[]]$Exclude = @()
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent

# Suites that need a working wgpu device for most of their tests.
$gpuDeviceSuites = @(
    "Etch.Gpu.Tests",
    "Etch.Gpu.Compositor.Tests",
    "Etch.PixelParity.Tests"
)

# Helper libraries that import Test.props but contain no tests of their own.
$notSuites = @("Etch.Geometry.Oracle")

$softwareGpu = $env:ETCH_SOFTWARE_GPU -eq "1"
$failed = [System.Collections.Generic.List[string]]::new()

$projects = Get-ChildItem -Path (Join-Path $RepoRoot "tests") -Filter *.csproj -Recurse -Depth 1 |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
    Sort-Object Name

foreach ($project in $projects) {
    $name = $project.BaseName
    if ($notSuites -contains $name -or $Exclude -contains $name) {
        continue
    }

    if ($softwareGpu -and $gpuDeviceSuites -contains $name) {
        Write-Host "::notice::Skipping $name on software-GPU environment"
        continue
    }

    Write-Host "=== $name ==="
    Push-Location $RepoRoot
    try {
        dotnet run --project $project.FullName -c $Configuration --no-build
        if ($LASTEXITCODE -ne 0) {
            $failed.Add($name)
        }
    }
    finally {
        Pop-Location
    }
}

if ($failed.Count -gt 0) {
    Write-Host "Failed suites: $($failed -join ', ')"
    exit 1
}

Write-Host "All suites passed."
