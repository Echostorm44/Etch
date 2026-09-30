#!/usr/bin/env pwsh
#Requires -Version 7
<#
.SYNOPSIS
    Builds wgpu-native from the commit pinned in SOURCE and packages it like an upstream release.

.DESCRIPTION
    Clones (or reuses) the pinned wgpu-native checkout, builds the cdylib for the host RID with the
    feature set from SOURCE, and writes <OutDir>/<archive>.zip laid out exactly like the upstream
    release archives (include/webgpu/*.h, lib/*, wgpu-native-meta/*), so fetch.ps1 and the packaging
    targets consume it unchanged. The static library is not shipped (D-008: never statically linked).

    Needs: git, the Rust toolchain (rustup honours wgpu-native's rust-toolchain.toml) and libclang for
    bindgen. On Windows, point LIBCLANG_PATH at Visual Studio's VC\Tools\Llvm\x64\bin if it is not
    found automatically.

.PARAMETER OutDir
    Where the archive is written. Defaults to native/wgpu-native/out.

.PARAMETER WorkDir
    Where the wgpu-native checkout lives. Defaults to native/wgpu-native/src (gitignored).

.PARAMETER Install
    Also unpack the archive into native/wgpu-native/<rid>/ for local development.
#>
param(
    [string]$OutDir = (Join-Path $PSScriptRoot 'out'),
    [string]$WorkDir = (Join-Path $PSScriptRoot 'src'),
    [switch]$Install
)

$ErrorActionPreference = 'Stop'

function Read-Source {
    $values = @{}
    foreach ($line in Get-Content (Join-Path $PSScriptRoot 'SOURCE')) {
        if ($line -match '^\s*#' -or $line -notmatch '=') {
            continue
        }
        $key, $value = $line -split '=', 2
        $values[$key.Trim()] = $value.Trim()
    }
    return $values
}

$rid, $archive, $libraries = if ($IsWindows) {
    'win-x64', 'wgpu-windows-x86_64-msvc-release', @('wgpu_native.dll', 'wgpu_native.dll.lib', 'wgpu_native.pdb')
} elseif ($IsMacOS) {
    'osx-arm64', 'wgpu-macos-aarch64-release', @('libwgpu_native.dylib')
} else {
    'linux-x64', 'wgpu-linux-x86_64-release', @('libwgpu_native.so')
}

$source = Read-Source
$commit = $source['commit']
$features = $source["features.$rid"]
if (-not $commit -or -not $features) {
    throw "SOURCE must define 'commit' and 'features.$rid'."
}

if (-not (Test-Path (Join-Path $WorkDir '.git'))) {
    git clone --filter=blob:none $source['repo'] $WorkDir
}
git -C $WorkDir fetch --depth 1 origin $commit
git -C $WorkDir checkout --force --quiet $commit
git -C $WorkDir submodule update --init --depth 1
if ($LASTEXITCODE -ne 0) {
    throw "Could not check out wgpu-native $commit."
}

if ($IsWindows -and -not $env:LIBCLANG_PATH) {
    $vsLlvm = Get-ChildItem 'C:\Program Files\Microsoft Visual Studio' -Directory -ErrorAction SilentlyContinue |
        ForEach-Object { Get-ChildItem $_.FullName -Directory } |
        ForEach-Object { Join-Path $_.FullName 'VC\Tools\Llvm\x64\bin' } |
        Where-Object { Test-Path (Join-Path $_ 'libclang.dll') } |
        Select-Object -First 1
    if ($vsLlvm) {
        $env:LIBCLANG_PATH = $vsLlvm
    }
}

Write-Host "Building wgpu-native $($commit.Substring(0, 10)) for $rid with features [$features]..."
Push-Location $WorkDir
try {
    cargo build --release --no-default-features --features $features
    if ($LASTEXITCODE -ne 0) {
        throw 'cargo build failed.'
    }
}
finally {
    Pop-Location
}

$stage = Join-Path $OutDir $archive
if (Test-Path $stage) {
    Remove-Item $stage -Recurse -Force
}
New-Item -ItemType Directory -Force (Join-Path $stage 'include/webgpu'), (Join-Path $stage 'lib'), (Join-Path $stage 'wgpu-native-meta') | Out-Null

Copy-Item (Join-Path $WorkDir 'ffi/webgpu-headers/webgpu.h') (Join-Path $stage 'include/webgpu/')
Copy-Item (Join-Path $WorkDir 'ffi/wgpu.h') (Join-Path $stage 'include/webgpu/')
Copy-Item (Join-Path $WorkDir 'ffi/webgpu-headers/webgpu.yml') (Join-Path $stage 'wgpu-native-meta/')
foreach ($library in $libraries) {
    Copy-Item (Join-Path $WorkDir "target/release/$library") (Join-Path $stage 'lib/')
}
Set-Content (Join-Path $stage 'wgpu-native-meta/wgpu-native-git-tag') $commit -NoNewline
Set-Content (Join-Path $stage 'wgpu-native-meta/features') $features -NoNewline

$zip = Join-Path $OutDir "$archive.zip"
if (Test-Path $zip) {
    Remove-Item $zip -Force
}
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
$size = (Get-Item (Join-Path $stage "lib/$($libraries[0])")).Length
Write-Host ("  {0}: {1:N1} MB  sha256 {2}" -f $libraries[0], ($size / 1MB), $hash)

if ($Install) {
    $dest = Join-Path $PSScriptRoot $rid
    if (Test-Path $dest) {
        Remove-Item $dest -Recurse -Force
    }
    Expand-Archive $zip -DestinationPath $dest
    Write-Host "  Installed to $dest"
}
