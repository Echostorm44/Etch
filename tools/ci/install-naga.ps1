param([string]$Version = "0.22.1", [string]$InstallPath = "$env:LOCALAPPDATA\naga\bin")

$ErrorActionPreference = "Stop"
if (Test-Path "$InstallPath\naga.exe") {
    Write-Host "naga already installed at $InstallPath"
    exit 0
}
$tmp = [System.IO.Path]::GetTempFileName() + ".zip"
try {
    $url = "https://github.com/gfx-rs/naga/releases/download/v$Version/naga-v$Version-x86_64-pc-windows-msvc.zip"
    Write-Host "Downloading $url"
    Invoke-WebRequest -Uri $url -OutFile $tmp
    Expand-Archive -Path $tmp -DestinationPath (Split-Path $InstallPath -Parent)
    New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
    Move-Item -Path "$InstallPath\..\naga-v$Version-x86_64-pc-windows-msvc\naga.exe" -Destination $InstallPath -Force
    Write-Host "naga installed to $InstallPath"
} finally {
    Remove-Item $tmp -ErrorAction SilentlyContinue
}
