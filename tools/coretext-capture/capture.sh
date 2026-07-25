#!/bin/sh
# WP-3534: build + run the CoreText reference capture on macOS.
# Produces CoreTextCorpus.json for the text-quality benchmark.
#
# Usage:
#   ./capture.sh                # downloads Roboto-Regular + captures
#   ./capture.sh /path/to.ttf   # captures with a specific font file
#
# Requires only the Command Line Tools (xcode-select --install). macOS 10.14+
# recommended (modern grayscale rendering); the OS version is recorded in the JSON.
set -e
cd "$(dirname "$0")"

FONT="${1:-Roboto-Regular.ttf}"
if [ ! -f "$FONT" ]; then
    echo "Downloading Roboto-Regular (the same font the benchmark uses)..." >&2
    curl -L "https://fonts.gstatic.com/s/roboto/v32/KFOmCnqEu92Fr1Me5Q.ttf" -o "$FONT"
fi

echo "Building coretext_capture..." >&2
clang -O2 -framework Foundation -framework CoreText -framework CoreGraphics \
    -o coretext_capture coretext_capture.m

echo "Capturing CoreText reference from $FONT ..." >&2
./coretext_capture "$FONT" > CoreTextCorpus.json
echo "Wrote CoreTextCorpus.json:" >&2
cat CoreTextCorpus.json >&2
echo "" >&2
echo "Commit it to tests/Etch.Text.Tests/TextParity/CoreTextCorpus.json to close the loop." >&2
