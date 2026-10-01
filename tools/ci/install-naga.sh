#!/bin/bash
# The pinned, idempotent installer lives in install-naga.ps1 (pwsh is on every CI runner and dev box);
# this wrapper keeps the bash entry point.
set -e
exec pwsh -NoProfile -File "$(dirname "$0")/install-naga.ps1" "$@"
