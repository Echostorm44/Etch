# gfx-rs/naga publishes no prebuilt CLI binaries (the old release-download URLs are dead),
# so install naga-cli from crates.io via cargo. Rust/cargo is preinstalled on GitHub runners
# and %USERPROFILE%\.cargo\bin is on PATH.
$ErrorActionPreference = "Stop"
if (Get-Command naga -ErrorAction SilentlyContinue) {
    Write-Host "naga already installed: $((Get-Command naga).Source)"
    exit 0
}
Write-Host "Installing naga-cli via cargo..."
cargo install naga-cli --locked
Write-Host "naga installed: $((Get-Command naga -ErrorAction SilentlyContinue).Source)"
