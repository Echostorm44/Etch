#!/bin/bash
set -e
# gfx-rs/naga publishes no prebuilt CLI binaries (the old release-download URLs are dead),
# so install naga-cli from crates.io via cargo. Rust/cargo is preinstalled on GitHub runners
# and ~/.cargo/bin is on PATH.
if command -v naga >/dev/null 2>&1; then
    echo "naga already installed: $(command -v naga)"
    exit 0
fi
echo "Installing naga-cli via cargo..."
cargo install naga-cli --locked
echo "naga installed: $(command -v naga)"
