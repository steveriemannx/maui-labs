#!/usr/bin/env bash
# Build the dui_shim bridge for the *host* platform and stage it in
# <product>/artifacts/native, where the sample/test/host projects pick it up.
#
#   platforms/PolluxOS.DUI/scripts/build-native.sh
#
# Run scripts/build-dui.sh first (or point POLLUXOS_DUI_INSTALL_DIR at an existing DUI
# install prefix). Works on FreeBSD/polluxos (bmake + Wayland), macOS and Linux: the
# bridge itself is platform-neutral C++ with #if branches for the platform message loop.
#
# Note: DUI is static-only, so the produced library (libdui_shim.so / .dylib / .dll) is
# self-contained; only the C++ runtime and the modules DUI's backend needs remain.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PRODUCT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
OS="$(uname -s)"

INSTALL_DIR="${POLLUXOS_DUI_INSTALL_DIR:-${PRODUCT_DIR}/artifacts/dui-install}"
BUILD_DIR="${POLLUXOS_DUI_SHIM_BUILD_DIR:-${PRODUCT_DIR}/artifacts/dui-shim-build}"
OUT_DIR="${POLLUXOS_DUI_NATIVE_DIR:-${PRODUCT_DIR}/artifacts/native}"

if [[ ! -f "${INSTALL_DIR}/lib/cmake/dui/duiConfig.cmake" ]]; then
  echo "error: DUI CMake package not found under '${INSTALL_DIR}'." >&2
  echo "       Run scripts/build-dui.sh first." >&2
  exit 2
fi

CMAKE_BIN="${POLLUXOS_DUI_CMAKE:-}"
if [[ -z "${CMAKE_BIN}" ]]; then
  for candidate in cmake4 cmake; do
    if command -v "${candidate}" >/dev/null; then
      version="$("${candidate}" --version | head -1 | grep -o '[0-9]*\.[0-9]*' | head -1)"
      if [[ "${version%%.*}" -ge 4 ]]; then CMAKE_BIN="${candidate}"; break; fi
    fi
  done
fi
[[ -n "${CMAKE_BIN}" ]] || { echo "error: CMake 4.0+ not found (cmake4/cmake)" >&2; exit 2; }

case "${OS}" in
  FreeBSD) GENERATOR=(-G "Unix Makefiles" -DCMAKE_MAKE_PROGRAM=/usr/bin/bmake) ;;
  *)       GENERATOR=(-G Ninja) ;;
esac

"${CMAKE_BIN}" -S "${PRODUCT_DIR}/native/dui_shim" -B "${BUILD_DIR}" \
  "${GENERATOR[@]}" \
  -DCMAKE_BUILD_TYPE=Release \
  -DCMAKE_PREFIX_PATH="${INSTALL_DIR}"
"${CMAKE_BIN}" --build "${BUILD_DIR}" -j "$(getconf _NPROCESSORS_ONLN 2>/dev/null || echo 4)"

mkdir -p "${OUT_DIR}"
find "${BUILD_DIR}" -maxdepth 1 -name 'libdui_shim.*' -exec cp -f {} "${OUT_DIR}/" \;

echo
echo "Staged the bridge in ${OUT_DIR}:"
ls -1 "${OUT_DIR}"
echo
echo "Managed projects pick this up with -p:PolluxOSDuiNativeDir=${OUT_DIR} (the default)."
echo "Optional ABI smoke test: ${BUILD_DIR}/dui_shim_abi_smoke <dui-resources-dir> [seconds]"
