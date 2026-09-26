#!/usr/bin/env bash
# Runs on a native Linux display (or under xvfb-run). Keeps the temporary profile for inspection.
set -euo pipefail
cd "$(dirname "$0")/.."
root="$PWD"
version="${1:-$(python3 scripts/release_metadata.py --format version)}"
runtime="${2:-linux-$(uname -m | sed -e 's/^x86_64$/x64/' -e 's/^aarch64$/arm64/')}"
case "$runtime:$(uname -m)" in
    linux-x64:x86_64) multiarch=x86_64-linux-gnu ;;
    linux-arm64:aarch64) multiarch=aarch64-linux-gnu ;;
    *) echo 'The package must be smoke-tested on its native architecture.' >&2; exit 1 ;;
esac
python3 scripts/verify-release-archives.py "$version" --runtime "$runtime"
temporary="$(mktemp -d /tmp/wt-map-helper-package.XXXXXX)"
tar -xzf "$root/artifacts/release/WarThunderMapHelper-$version-$runtime.tar.gz" -C "$temporary"
test -x "$temporary/WarThunderMapHelper"
test -s "$temporary/Assets/icons/Player.svg"
if [[ -d "$root/artifacts/linux-deps/root/usr/lib/$multiarch" ]]; then
    export LD_LIBRARY_PATH="$root/artifacts/linux-deps/root/usr/lib/$multiarch:${LD_LIBRARY_PATH:-}"
    export FONTCONFIG_PATH="$root/artifacts/linux-deps/root/etc/fonts"
    export FONTCONFIG_FILE="$FONTCONFIG_PATH/fonts.conf"
fi
mkdir -p "$root/artifacts/screenshots"
screenshot="$root/artifacts/screenshots/$runtime-package-$version.png"
timeout 120 "$temporary/WarThunderMapHelper" --demo --smoke-test \
    --screenshot="$screenshot" --data-dir="$temporary/profile"
test -s "$screenshot"
printf 'Linux archive extracted and started successfully: %s\n' "$temporary"
