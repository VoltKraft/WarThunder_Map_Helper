#!/usr/bin/env bash
# Optional WSL check: download display dependencies into artifacts, without installing system packages.
set -euo pipefail
cd "$(dirname "$0")/.."
root="$PWD"
version="${1:-$(python3 scripts/release_metadata.py --format version)}"
case "$(uname -m)" in
    x86_64) runtime=linux-x64; multiarch=x86_64-linux-gnu ;;
    aarch64) runtime=linux-arm64; multiarch=aarch64-linux-gnu ;;
    *) echo 'Unsupported Linux architecture.' >&2; exit 1 ;;
esac
deps="$root/artifacts/linux-deps"
mkdir -p "$deps/lists/partial" "$deps/cache/archives/partial" "$deps/download" "$deps/root"
apt-get -o Dir::State::lists="$deps/lists" -o Dir::Cache="$deps/cache" -o Debug::NoLocking=true update -qq
cd "$deps/download"
apt-get -o Dir::State::lists="$deps/lists" -o Dir::Cache="$deps/cache" download \
    libfontconfig1 fontconfig-config libfreetype6 libbrotli1 libpng16-16 libice6 libsm6 \
    libxcursor1 libxrandr2 libxi6 libxrender1 libxfixes3
for package in ./*.deb; do dpkg-deb -x "$package" "$deps/root"; done
cd "$root"
export LD_LIBRARY_PATH="$deps/root/usr/lib/$multiarch:${LD_LIBRARY_PATH:-}"
export FONTCONFIG_PATH="$deps/root/etc/fonts"
export FONTCONFIG_FILE="$deps/root/etc/fonts/fonts.conf"
mkdir -p artifacts/screenshots
timeout 120 "./artifacts/publish/$runtime-$version/WarThunderMapHelper" \
    --demo --smoke-test --screenshot="artifacts/screenshots/$runtime.png" --data-dir=artifacts/linux-smoke-data
test -s "artifacts/screenshots/$runtime.png"
