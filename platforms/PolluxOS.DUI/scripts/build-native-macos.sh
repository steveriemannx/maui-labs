#!/usr/bin/env bash
# Build libdui_shim.dylib against an installed DUI tree and stage it in the
# directory the managed backend loads it from:
#   <product>/artifacts/native/macos
#
# Usage:
#   platforms/PolluxOS.DUI/scripts/build-native-macos.sh
#
# Run scripts/build-dui-macos.sh first (or point POLLUXOS_DUI_INSTALL_DIR at an
# existing DUI install prefix).
#
# Note: DUI is static-only, so this dylib is self-contained — there are no DUI
# dylibs to ship next to it. The only runtime dependency is the C++ runtime.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PRODUCT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"

INSTALL_DIR="${POLLUXOS_DUI_INSTALL_DIR:-${PRODUCT_DIR}/artifacts/dui-install}"
BUILD_DIR="${POLLUXOS_DUI_SHIM_BUILD_DIR:-${PRODUCT_DIR}/artifacts/dui-shim-build}"
OUT_DIR="${POLLUXOS_DUI_NATIVE_DIR:-${PRODUCT_DIR}/artifacts/native/macos}"

if [[ ! -f "${INSTALL_DIR}/lib/cmake/dui/duiConfig.cmake" ]]; then
  echo "error: DUI CMake package not found under '${INSTALL_DIR}'." >&2
  echo "       Run scripts/build-dui-macos.sh first." >&2
  exit 2
fi

command -v cmake >/dev/null || { echo "error: cmake not found" >&2; exit 2; }
command -v ninja >/dev/null || { echo "error: ninja not found (brew install ninja)" >&2; exit 2; }

cmake -S "${PRODUCT_DIR}/native/dui_shim" -B "${BUILD_DIR}" \
  -G Ninja \
  -DCMAKE_BUILD_TYPE=Release \
  -DCMAKE_PREFIX_PATH="${INSTALL_DIR}"
cmake --build "${BUILD_DIR}"

mkdir -p "${OUT_DIR}"
find "${BUILD_DIR}" -maxdepth 2 -name 'libdui_shim*.dylib' -exec cp -f {} "${OUT_DIR}/" \;

if [[ ! -f "${OUT_DIR}/libdui_shim.dylib" ]]; then
  echo "error: libdui_shim.dylib was not produced." >&2
  exit 1
fi

echo
echo "Staged ${OUT_DIR}/libdui_shim.dylib"
echo "Runtime dependencies:"
otool -L "${OUT_DIR}/libdui_shim.dylib" | tail -n +2 | sed 's/^/  /'
echo
echo "Managed projects pick this up via -p:PolluxOSDuiNativeDir=${OUT_DIR} (or the Directory.Build.props default)."
