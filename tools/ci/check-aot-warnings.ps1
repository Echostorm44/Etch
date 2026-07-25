param(
    [Parameter(Mandatory=$true)]
    [string]$Log,
    [Parameter(Mandatory=$true)]
    [string]$Namespace
)

$ErrorActionPreference = "Stop"
$content = Get-Content $Log -Raw
$lines = $content -split "`n"

$warningPattern = "(?<severity>warning|WKQ001|trim.*warning|AOT.*warning):.*$"
$etchPattern = "(?<file>src\\.*|src-gen\\.*)\($Namespace\."

$found = @()
foreach ($line in $lines) {
    if ($line -match $warningPattern -and $line -match $etchPattern) {
        $found += $line
    }
}

if ($found.Count -gt 0) {
    Write-Host "AOT warnings found in Etch namespaces:"
    $found | ForEach-Object { Write-Host $_ }
    exit 1
}

Write-Host "No AOT warnings in $Namespace namespaces."
exit 0
