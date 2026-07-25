#!/usr/bin/env pwsh
#Requires -Version 7
<#
.SYNOPSIS
    Fetches prebuilt wgpu-native binaries for the pinned VERSION.

.DESCRIPTION
    Downloads prebuilt wgpu-native binaries from GitHub releases, verifies SHA-256
    checksums against CHECKSUMS.txt, and unpacks to native/wgpu-native/<rid>/.

.PARAMETER Rid
    Target runtime identifier: win-x64, linux-x64, osx-arm64, or all (default).

.EXAMPLE
    ./fetch.ps1 all
    # Fetches all three RIDs

.EXAMPLE
    ./fetch.ps1 win-x64
    # Fetches only the Windows x64 binary
#>
param(
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64', 'all')]
    [string]$Rid = 'all'
)

$ErrorActionPreference = 'Stop'
$Version = (Get-Content "$PSScriptRoot/VERSION" -Raw).Trim()
$BaseUrl = "https://github.com/gfx-rs/wgpu-native/releases/download/$Version"
$ScriptDir = $PSScriptRoot

$Map = @{
    'win-x64'    = 'wgpu-windows-x86_64-msvc-release.zip';
    'linux-x64'  = 'wgpu-linux-x86_64-release.zip';
    'osx-arm64'  = 'wgpu-macos-aarch64-release.zip';
}

$Rids = if ($Rid -eq 'all') { @('win-x64', 'linux-x64', 'osx-arm64') } else { @($Rid) }

foreach ($TargetRid in $Rids) {
    $ArchiveName = $Map[$TargetRid]
    $Url = "$BaseUrl/$ArchiveName"
    $DestDir = Join-Path $ScriptDir $TargetRid
    $ZipPath = "$DestDir.zip"

    Write-Host "Fetching $TargetRid from $Url..."

    if (Test-Path $ZipPath) {
        Remove-Item $ZipPath -Force
    }

    Invoke-WebRequest -Uri $Url -OutFile $ZipPath

    $ExpectedHash = Select-String -Path "$ScriptDir/CHECKSUMS.txt" -Pattern "^$ArchiveName\s*=\s*" |
        ForEach-Object { ($_ -replace ".*=\s*", '').Trim() }

    if (-not $ExpectedHash) {
        throw "Checksum not found for $ArchiveName in CHECKSUMS.txt"
    }

    $ActualHash = (Get-FileHash -Path $ZipPath -Algorithm SHA256).Hash.ToLower()

    if ($ExpectedHash -ne $ActualHash) {
        Remove-Item $ZipPath -Force
        throw "Checksum mismatch for $ArchiveName`nExpected: $ExpectedHash`nActual:   $ActualHash"
    }

    Write-Host "  SHA-256 verified: $ActualHash"

    if (Test-Path $DestDir) {
        Remove-Item $DestDir -Recurse -Force
    }

    Expand-Archive -Path $ZipPath -DestinationPath $DestDir -Force
    Remove-Item $ZipPath -Force

    Write-Host "  Extracted to $DestDir"
}

Write-Host "Done."