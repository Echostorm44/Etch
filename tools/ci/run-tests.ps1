param([string]$Rid = "win-x64")

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path $PSScriptRoot -Parent -Parent

dotnet test "$RepoRoot/tests/Etch.Abstractions.Tests" -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet publish "$RepoRoot/tests/Etch.Abstractions.Tests" -c Release -r $Rid --self-contained -p:PublishAot=true
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& "$RepoRoot/tests/Etch.Abstractions.Tests/bin/Release/net10.0/$Rid/publish/Etch.Abstractions.Tests.exe"
exit $LASTEXITCODE
