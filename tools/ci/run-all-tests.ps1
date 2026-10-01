# Runs every TUnit test project under tests/ against an existing Release build.
#
# Categories excluded from this (gating) run, via TUnit's --treenode-filter:
#   Flaky        quarantined tests (docs flaky-policy: the main gate excludes them).
#   Performance  wall-clock budgets that are defined for reference hardware (ProjectPlan perf
#                tables). Shared CI runners render on WARP (Windows) or a VM GPU (macOS) and share
#                CPU with the suite's other parallel tests, so these budgets measure the runner,
#                not Etch. They still run in a plain `dotnet run` of their suite on a dev machine;
#                pass -ExcludeCategories Flaky to include them here.
#
# Suites skipped by platform:
#   - On software-GPU environments (ETCH_SOFTWARE_GPU=1, i.e. headless Linux CI) the suites that
#     open a wgpu device: wgpu offscreen rendering segfaults against lavapipe there, and the real
#     GPU path is covered by the Windows (WARP/hardware) and macOS (Metal) legs.
#   - Off Windows, suites that target a -windows TFM (Etch.Samples.Tests references the win-x64
#     sample apps, so it cannot load them anywhere else).
param(
    [string]$Configuration = "Release",
    [string[]]$Exclude = @(),
    [string[]]$ExcludeCategories = @("Flaky", "Performance")
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

$testArguments = @()
if ($ExcludeCategories.Count -gt 0) {
    $conditions = ($ExcludeCategories | ForEach-Object { "(Category!=$_)" }) -join "&"
    $testArguments = @("--treenode-filter", "/*/*/*/*[$conditions]")
    Write-Host "Excluding categories: $($ExcludeCategories -join ', ')"
}

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

    $targetFramework = (Select-Xml -Path $project.FullName -XPath "//TargetFramework").Node.InnerText
    if (-not $IsWindows -and $targetFramework -like "*-windows*") {
        Write-Host "::notice::Skipping $name (targets $targetFramework) off Windows"
        continue
    }

    Write-Host "=== $name ==="
    Push-Location $RepoRoot
    try {
        dotnet run --project $project.FullName -c $Configuration --no-build -- @testArguments
        if ($LASTEXITCODE -ne 0) {
            # A native crash ends the process before TUnit prints anything, so the exit code is
            # the only trace of it (e.g. 139 = SIGSEGV on Unix, -1073741819 = access violation).
            Write-Host "::error::$name exited with code $LASTEXITCODE"
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
