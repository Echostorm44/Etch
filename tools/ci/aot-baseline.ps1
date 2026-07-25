param([string]$Rid = "win-x64")

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path $PSScriptRoot -Parent -Parent

Write-Host "Running AOT baseline for $Rid..."
$start = Get-Date

dotnet publish "$RepoRoot/src/Etch.Abstractions" -c Release -r $Rid --self-contained -p:PublishAot=true -o "$RepoRoot/aot-baseline/$Rid"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$elapsed = ((Get-Date) - $start).TotalSeconds
$size = (Get-ChildItem "$RepoRoot/aot-baseline/$Rid/Etch.Abstractions.exe" -ErrorAction SilentlyContinue).Length / 1MB

Write-Host "Published in ${elapsed}s, binary size: ${size}MB"
Write-Host "Baseline complete."
