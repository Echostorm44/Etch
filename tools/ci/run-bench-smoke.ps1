#!/usr/bin/env pwsh
param(
    [string]$Project = "bench/Etch.Primitives.Bench",
    [string]$Filter = "*SpanWriterHotLoop*",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$ProjectPath = if ([System.IO.Path]::IsPathRooted($Project)) {
    $Project
} else {
    Join-Path $PSScriptRoot "..\..\$Project"
}

if (-not (Test-Path $ProjectPath)) {
    Write-Error "Project not found: $ProjectPath"
    exit 1
}

$start = Get-Date
Write-Host "=== Bench Smoke: $ProjectPath ===" -ForegroundColor Cyan
Write-Host "Filter: $Filter"
Write-Host "Configuration: $Configuration"
Write-Host ""

$output = dotnet run -c $Configuration --project $ProjectPath -- --filter $Filter 2>&1
$exitCode = $LASTEXITCODE

$resultsLog = Join-Path $PSScriptRoot "..\..\bench-results.log"
$output | Out-File -FilePath $resultsLog -Encoding utf8

$allocatedPattern = '\|\s*(\S+)\s*\|\s*[\d.]+\s*ns\s*\|\s*[\d.]+\s*ns\s*\|\s*[\d.]+\s*ns\s*\|\s*(\S+)\s*\|'
$benchmarks = @{}

$lines = $output -split "`n"
foreach ($line in $lines) {
    if ($line -match $allocatedPattern) {
        $name = $Matches[1]
        $allocated = $Matches[2]
        $benchmarks[$name] = $allocated
    }
}

$failCount = 0
foreach ($name in $benchmarks.Keys) {
    $allocated = $benchmarks[$name]
    if ($allocated -eq "-" -or $allocated -eq "0") {
        Write-Host "[PASS] $name : Allocated = $allocated B" -ForegroundColor Green
    } else {
        Write-Host "[FAIL] $name : Allocated = $allocated B (exceeds budget)" -ForegroundColor Red
        $failCount++
    }
}

$elapsed = (Get-Date) - $start
Write-Host ""
Write-Host "Completed in $($elapsed.TotalMinutes) minutes ($([int]$elapsed.TotalSeconds) seconds)"

if ($failCount -gt 0 -or $exitCode -ne 0) {
    Write-Host "FAILED" -ForegroundColor Red
    exit 1
}

Write-Host "SUCCESS" -ForegroundColor Green
exit 0
