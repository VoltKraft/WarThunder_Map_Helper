#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
repo_version="$(python3 scripts/release_metadata.py --format version)"
version="$repo_version"
runtime="linux-$(uname -m | sed -e 's/^x86_64$/x64/' -e 's/^aarch64$/arm64/')"
skip_tests=false
while (($#)); do
    case "$1" in
        --runtime) runtime="${2:?Missing runtime}"; shift 2 ;;
        --skip-tests) skip_tests=true; shift ;;
        --help) printf 'Usage: bash packaging/linux/build.sh [version] [--runtime linux-x64|linux-arm64] [--skip-tests]\n'; exit 0 ;;
        --*) printf 'Unknown option: %s\n' "$1" >&2; exit 1 ;;
        *) version="$1"; shift ;;
    esac
done
[[ "$version" == "$repo_version" ]] || { echo 'Version must match project metadata.' >&2; exit 1; }
case "$runtime" in linux-x64|linux-arm64) ;; *) echo 'Unsupported Linux runtime.' >&2; exit 1 ;; esac
output="$PWD/artifacts/publish/$runtime-$version"
# Only this version's generated publish directory is reset; older releases remain available.
case "$output" in "$PWD/artifacts/publish/"*) rm -rf -- "$output" ;; *) exit 1 ;; esac
if [[ "$skip_tests" != true ]]; then
    dotnet test WarThunderMapHelper.slnx -c Release
fi
dotnet publish src/MapHelper.Desktop/MapHelper.Desktop.csproj -c Release -r "$runtime" \
    --self-contained true -p:DebugType=None -p:DebugSymbols=false -o "$output"
python3 scripts/check_dependency_licenses.py
cp ./*.md LICENSE "$output/"
cp -R docs "$output/docs"
mkdir -p "$output/packaging/flatpak/screenshots"
cp packaging/flatpak/screenshots/map.png "$output/packaging/flatpak/screenshots/"
python3 tools/install-flatpak-license-notices.py \
    --assets-file src/MapHelper.Desktop/obj/project.assets.json \
    --output-dir "$output/THIRD_PARTY_LICENSES" --supplemental-dir packaging/flatpak/licenses
chmod +x "$output/WarThunderMapHelper"
mkdir -p artifacts/release
tar -czf "artifacts/release/WarThunderMapHelper-$version-$runtime.tar.gz" -C "$output" .
python3 scripts/verify-release-archives.py "$version" --runtime "$runtime"
(cd artifacts/release && sha256sum "WarThunderMapHelper-$version-$runtime.tar.gz" > "SHA256SUMS-$version-$runtime.txt")
cat "artifacts/release/SHA256SUMS-$version-$runtime.txt"
