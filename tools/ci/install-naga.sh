#!/bin/bash
set -e
mkdir -p "$HOME/.local/bin"
if command -v naga &> /dev/null; then
    echo "naga already installed"
    exit 0
fi
curl -sL https://github.com/gfx-rs/naga/releases/download/v0.22.1/naga-v0.22.1-x86_64-unknown-linux-gnu.tar.gz | tar xz -C "$HOME/.local/bin" naga
echo "naga installed to $HOME/.local/bin/naga"
