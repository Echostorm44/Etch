#!/usr/bin/env pwsh
#Requires -Version 7
<#
.SYNOPSIS
    Fetches the wgpu-native binaries for the pinned VERSION.

.DESCRIPTION
    Downloads the wgpu-native archives that .github/workflows/wgpu-native.yml built from the commit
    pinned in SOURCE plus patches/*.patch (release VERSION on this repository), verifies SHA-256
    checksums against CHECKSUMS.txt, and unpacks to native/wgpu-native/<rid>/.

    CHECKSUMS.txt records which release its checksums belong to (`version`) and the build inputs
    that release was built from (`inputs`, see -InputsHash). Until a release for the current VERSION
    and build inputs is published and recorded there, the binaries are "unpublished": fetching
    fails, and CI builds them from source instead (.github/actions/wgpu-native).

.PARAMETER Rid
    Target runtime identifier: win-x64, linux-x64, osx-arm64, or all (default).

.PARAMETER Status
    Prints "published" or "unpublished" (with the reason on the next line) and exits.

.PARAMETER InputsHash
    Prints the hash of the build inputs (SOURCE's commit and features, and the patches) and exits.
    Record it as `inputs` in CHECKSUMS.txt together with a new release's checksums.

.EXAMPLE
    ./fetch.ps1 all
    # Fetches all three RIDs

.EXAMPLE
    ./fetch.ps1 win-x64
    # Fetches only the Windows x64 binary
#>
param(
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64', 'all')]
    [string]$Rid = 'all',
    [switch]$Status,
    [switch]$InputsHash
)

$ErrorActionPreference = 'Stop'
$Version = (Get-Content "$PSScriptRoot/VERSION" -Raw).Trim()
$BaseUrl = "https://github.com/Echostorm44/Etch/releases/download/$Version"
$ScriptDir = $PSScriptRoot

$Map = @{
    'win-x64'    = 'wgpu-windows-x86_64-msvc-release.zip';
    'linux-x64'  = 'wgpu-linux-x86_64-release.zip';
    'osx-arm64'  = 'wgpu-macos-aarch64-release.zip';
}

# `key = value` lines, comments skipped.
function Read-KeyValues([string]$Path) {
    $values = [ordered]@{}
    foreach ($line in Get-Content $Path) {
        if ($line -match '^\s*#' -or $line -notmatch '=') {
            continue
        }
        $key, $value = $line -split '=', 2
        $values[$key.Trim()] = $value.Trim()
    }
    return $values
}

# What the binaries are built from: the pinned commit, the per-RID features and the patches.
# Comments and build.ps1 itself are left out, so editing them does not unpublish a release.
function Get-BuildInputsHash {
    $source = Read-KeyValues (Join-Path $ScriptDir 'SOURCE')
    $text = [System.Text.StringBuilder]::new()
    foreach ($key in ($source.Keys | Where-Object { $_ -eq 'commit' -or $_ -like 'features.*' } | Sort-Object)) {
        [void]$text.Append("$key=$($source[$key])`n")
    }
    $patches = Get-ChildItem (Join-Path $ScriptDir 'patches') -Filter '*.patch' -ErrorAction SilentlyContinue | Sort-Object Name
    foreach ($patch in $patches) {
        [void]$text.Append("patch $($patch.Name)`n")
        [void]$text.Append(((Get-Content $patch.FullName -Raw) -replace "`r`n", "`n"))
    }
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($text.ToString())
    return [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

$Checksums = Read-KeyValues "$ScriptDir/CHECKSUMS.txt"
$CurrentInputs = Get-BuildInputsHash

if ($InputsHash) {
    Write-Output $CurrentInputs
    return
}

$UnpublishedReason = if ($Checksums['version'] -ne $Version) {
    "CHECKSUMS.txt is for '$($Checksums['version'])', VERSION is '$Version'"
} elseif ($Checksums['inputs'] -ne $CurrentInputs) {
    "release $Version was built from inputs $($Checksums['inputs']); SOURCE and patches/ now hash to $CurrentInputs (bump VERSION)"
} else {
    $null
}

if ($Status) {
    if ($UnpublishedReason) {
        Write-Output 'unpublished'
        Write-Output $UnpublishedReason
    } else {
        Write-Output 'published'
    }
    return
}

if ($UnpublishedReason) {
    throw "wgpu-native $Version is not published: $UnpublishedReason. Build it with native/wgpu-native/build.ps1 -Install, or publish it (.github/workflows/wgpu-native.yml) and record its checksums."
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

    $ExpectedHash = $Checksums[$ArchiveName]

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
