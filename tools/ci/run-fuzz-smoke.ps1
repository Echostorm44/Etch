#!/usr/bin/env pwsh
param(
    [string]$Target = "Etch.Primitives.Fuzz",
    [int]$Seconds = 30
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path $PSScriptRoot -Parent

$fuzzDir = Join-Path $repoRoot "fuzz\$Target"
if (-not (Test-Path $fuzzDir)) {
    Write-Error "Fuzz target not found: $fuzzDir"
    exit 1
}

$corpusDir = Join-Path $fuzzDir "corpus"
$corpusArg = ""
if (Test-Path $corpusDir) {
    $corpusFiles = Get-ChildItem $corpusDir -Filter "*.bin" -File
    if ($corpusFiles.Count -gt 0) {
        $corpusArg = $corpusFiles | ForEach-Object { $_.FullName } | Join-String -Separator " "
    }
}

$start = Get-Date
Write-Host "=== Fuzz Smoke: $Target ===" -ForegroundColor Cyan
Write-Host "Duration: $Seconds seconds"
Write-Host "Corpus: $corpusDir"
Write-Host ""

$projectPath = Join-Path $fuzzDir "$Target.csproj"
$binaryPath = Join-Path $fuzzDir "bin\Release\net10.0\$Target.dll"

$runArgs = @(
    "run",
    "-c", "Release",
    "--project", $projectPath,
    "--"
)

$runOutput = & dotnet @runArgs 2>&1
$exitCode = $LASTEXITCODE

$output = $runOutput | Out-String

$elapsed = (Get-Date) - $start
$elapsedSeconds = [int]$elapsed.TotalSeconds

Write-Host ""
Write-Host "Fuzz run completed in $elapsedSeconds seconds (target: $Seconds seconds)"

if ($exitCode -ne 0) {
    Write-Host "[CRASH] Fuzz target exited with code $exitCode" -ForegroundColor Red
    Write-Host "Output:"
    Write-Host $output
    exit 1
}

Write-Host "[OK] No new crashes detected" -ForegroundColor Green
exit 0
