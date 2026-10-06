#!/usr/bin/env bash
# Build + install the DUI toolkit for macOS so the PolluxOS.DUI backend and its
# native shim have a `dui::dui` CMake package to link against.
#
# Usage:
#   platforms/PolluxOS.DUI/scripts/build-dui-macos.sh [dui-root]
#
# Environment overrides:
#   POLLUXOS_DUI_ROOT              DUI source tree (default: the local worktree)
#   POLLUXOS_DUI_BUILD_DIR         CMake build dir (default: <product>/artifacts/dui-build)
#   POLLUXOS_DUI_INSTALL_DIR       Install prefix  (default: <product>/artifacts/dui-install)
#   POLLUXOS_DUI_OSX_TARGET        macOS deployment target (default: 14.0 — must match
#                                  dui_shim, or the link reports min-OS mismatches)
#   POLLUXOS_DUI_PREBUILT_SKIA     Path to an existing Skia build tree
#                                  (…/<dui-build>/lib/release). DUI skips its whole
#                                  gn+ninja Skia build when <build>/lib/release/libskia.a
#                                  already exists, which turns the ~12 minute first
#                                  build into a few minutes.
#
# Why the flags below are mandatory:
#   * CMAKE_POSITION_INDEPENDENT_CODE=ON — DUI only ever produces STATIC archives
#     (no BUILD_SHARED_LIBS anywhere), and our shim is a dylib that links them.
#     Without PIC the link fails.
#   * DUI_BUILD_EXAMPLES=OFF — the default `all` target builds 55 example apps.
#   * DUI_BUILD_TESTS=OFF — not needed for the bridge; keeps the build short.
#   * Ninja — DUI's make generators on macOS/FreeBSD run on bmake.
#
# Cost: DUI's configure step downloads Skia (~70 MB) into <dui-root>/third_party
# and builds it from source with gn+ninja; budget ~12+ minutes on first run.
# CMake 4.0+ is required by the DUI project itself.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PRODUCT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"

DUI_ROOT="${1:-${POLLUXOS_DUI_ROOT:-$(cd "${PRODUCT_DIR}/../../../dui-dev-mac/.worktrees/worktree-dui-backend" 2>/dev/null && pwd || true)}}"
BUILD_DIR="${POLLUXOS_DUI_BUILD_DIR:-${PRODUCT_DIR}/artifacts/dui-build}"
INSTALL_DIR="${POLLUXOS_DUI_INSTALL_DIR:-${PRODUCT_DIR}/artifacts/dui-install}"
OSX_TARGET="${POLLUXOS_DUI_OSX_TARGET:-14.0}"

if [[ -z "${DUI_ROOT}" || ! -f "${DUI_ROOT}/CMakeLists.txt" ]]; then
  echo "error: DUI source tree not found. Pass it as \$1 or set POLLUXOS_DUI_ROOT." >&2
  echo "       tried: '${DUI_ROOT:-<unset>}'" >&2
  exit 2
fi

command -v cmake >/dev/null || { echo "error: cmake not found" >&2; exit 2; }
command -v ninja >/dev/null || { echo "error: ninja not found (brew install ninja)" >&2; exit 2; }

echo "DUI source : ${DUI_ROOT}"
echo "Build dir  : ${BUILD_DIR}"
echo "Install dir: ${INSTALL_DIR}"
echo "OSX target : ${OSX_TARGET}"

# Reuse an already-compiled Skia tree when one is offered: DUI checks for
# <build>/lib/release/libskia.a at configure time and skips gn+ninja if it is there.
if [[ -n "${POLLUXOS_DUI_PREBUILT_SKIA:-}" ]]; then
  if [[ ! -f "${POLLUXOS_DUI_PREBUILT_SKIA}/libskia.a" ]]; then
    echo "error: POLLUXOS_DUI_PREBUILT_SKIA does not contain libskia.a: ${POLLUXOS_DUI_PREBUILT_SKIA}" >&2
    exit 2
  fi
  echo "Prebuilt Skia: ${POLLUXOS_DUI_PREBUILT_SKIA} (skipping the gn+ninja Skia build)"
  mkdir -p "${BUILD_DIR}/lib/release"
  rsync -a "${POLLUXOS_DUI_PREBUILT_SKIA}/" "${BUILD_DIR}/lib/release/"
fi

# The DUI sources must be present for the configure-time guard; Skia itself is
# extracted from third_party/downloads by DUI when it does build Skia.
cmake -S "${DUI_ROOT}" -B "${BUILD_DIR}" \
  -G Ninja \
  -DCMAKE_BUILD_TYPE=Release \
  -DCMAKE_OSX_DEPLOYMENT_TARGET="${OSX_TARGET}" \
  -DCMAKE_POSITION_INDEPENDENT_CODE=ON \
  -DCMAKE_INSTALL_PREFIX="${INSTALL_DIR}" \
  -DDUI_BUILD_EXAMPLES=OFF \
  -DDUI_BUILD_TESTS=OFF \
  -DDUI_BUILD_CEF_EXAMPLES=OFF \
  -DDUI_BUILD_WEBVIEW2_EXAMPLES=OFF \
  -DDUI_ENABLE_CEF=OFF \
  -DDUI_ENABLE_MVVM=OFF

# `dui` is the static library; `dui_entry` is required by the install rules.
cmake --build "${BUILD_DIR}" --target dui dui_entry
cmake --install "${BUILD_DIR}"

echo
echo "DUI installed to ${INSTALL_DIR}"
echo "Next: platforms/PolluxOS.DUI/scripts/build-native-macos.sh"
