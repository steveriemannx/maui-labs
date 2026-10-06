#!/usr/bin/env bash
# Build + install the DUI toolkit for the *host* platform, so the PolluxOS.DUI backend
# and its native bridge have a `dui::dui` CMake package to link against.
#
#   platforms/PolluxOS.DUI/scripts/build-dui.sh [dui-root]
#
# The backend itself is platform-neutral (net10.0); this script is what makes a given
# host able to run it. FreeBSD/polluxos is the primary target and is detected
# automatically (bmake + the Wayland backend, the configuration polluxdesk uses);
# macOS and Linux work too.
#
# Environment overrides:
#   POLLUXOS_DUI_ROOT           DUI source tree (default: the sibling worktree if present)
#   POLLUXOS_DUI_BUILD_DIR      CMake build dir        (default: <product>/artifacts/dui-build)
#   POLLUXOS_DUI_INSTALL_DIR    Install prefix         (default: <product>/artifacts/dui-install)
#   POLLUXOS_DUI_CMAKE          CMake to use           (default: cmake4, then cmake)
#   POLLUXOS_DUI_BACKEND        wayland | x11          (default: wayland on FreeBSD/Linux, native on macOS)
#   POLLUXOS_DUI_OSX_TARGET     macOS deployment target (default: 14.0)
#   POLLUXOS_DUI_PREBUILT_SKIA  Existing Skia build tree (…/lib/release). DUI skips its
#                               gn+ninja Skia build when <build>/lib/release/libskia.a
#                               exists, turning ~12 minutes into a few.
#
# Why PIC is forced: DUI only ever produces STATIC archives, and our bridge is a shared
# library that links them — without CMAKE_POSITION_INDEPENDENT_CODE the link fails.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PRODUCT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
OS="$(uname -s)"

DUI_ROOT="${1:-${POLLUXOS_DUI_ROOT:-}}"
if [[ -z "${DUI_ROOT}" ]]; then
  for candidate in \
      "${PRODUCT_DIR}/../../../dui-dev-mac/.worktrees/worktree-dui-backend" \
      "${PRODUCT_DIR}/../../../dui-dev" \
      "${PRODUCT_DIR}/../../../dui"; do
    [[ -f "${candidate}/CMakeLists.txt" ]] && { DUI_ROOT="$(cd "${candidate}" && pwd)"; break; }
  done
fi
BUILD_DIR="${POLLUXOS_DUI_BUILD_DIR:-${PRODUCT_DIR}/artifacts/dui-build}"
INSTALL_DIR="${POLLUXOS_DUI_INSTALL_DIR:-${PRODUCT_DIR}/artifacts/dui-install}"
BACKEND="${POLLUXOS_DUI_BACKEND:-}"
OSX_TARGET="${POLLUXOS_DUI_OSX_TARGET:-14.0}"

if [[ -z "${DUI_ROOT}" || ! -f "${DUI_ROOT}/CMakeLists.txt" ]]; then
  echo "error: DUI source tree not found. Pass it as \$1 or set POLLUXOS_DUI_ROOT." >&2
  exit 2
fi

# DUI requires CMake >= 4.0; FreeBSD ports currently ship 3.31, and the polluxos box has
# a user-local 4.x as `cmake4`.
CMAKE_BIN="${POLLUXOS_DUI_CMAKE:-}"
if [[ -z "${CMAKE_BIN}" ]]; then
  for candidate in cmake4 cmake; do
    if command -v "${candidate}" >/dev/null; then
      version="$("${candidate}" --version | head -1 | grep -o '[0-9]*\.[0-9]*' | head -1)"
      if [[ "${version%%.*}" -ge 4 ]]; then CMAKE_BIN="${candidate}"; break; fi
    fi
  done
fi
if [[ -z "${CMAKE_BIN}" ]]; then
  echo "error: DUI needs CMake 4.0+; none found (looked for cmake4 and cmake)." >&2
  exit 2
fi

# Per-platform generator + backend. DUI's make generators on FreeBSD/macOS run on bmake.
case "${OS}" in
  FreeBSD)
    GENERATOR=(-G "Unix Makefiles" -DCMAKE_MAKE_PROGRAM=/usr/bin/bmake)
    BACKEND="${BACKEND:-wayland}"
    BACKEND_FLAGS=(-DDUI_ENABLE_WAYLAND=ON)
    [[ "${BACKEND}" == "x11" ]] && BACKEND_FLAGS=(-DDUI_ENABLE_WAYLAND=OFF)
    ;;
  Darwin)
    GENERATOR=(-G Ninja)
    BACKEND="${BACKEND:-native}"
    BACKEND_FLAGS=(-DCMAKE_OSX_DEPLOYMENT_TARGET="${OSX_TARGET}")
    ;;
  *)
    GENERATOR=(-G Ninja)
    BACKEND="${BACKEND:-wayland}"
    BACKEND_FLAGS=(-DDUI_ENABLE_WAYLAND=ON)
    [[ "${BACKEND}" == "x11" ]] && BACKEND_FLAGS=(-DDUI_ENABLE_WAYLAND=OFF)
    ;;
esac

command -v "${CMAKE_BIN}" >/dev/null || { echo "error: ${CMAKE_BIN} not found" >&2; exit 2; }
case "${OS}" in
  FreeBSD) command -v bmake >/dev/null || { echo "error: bmake not found" >&2; exit 2; } ;;
  *)       command -v ninja >/dev/null || { echo "error: ninja not found" >&2; exit 2; } ;;
esac

echo "host        : ${OS} (backend: ${BACKEND})"
echo "cmake       : ${CMAKE_BIN} $("${CMAKE_BIN}" --version | head -1)"
echo "DUI source  : ${DUI_ROOT}"
echo "Build dir   : ${BUILD_DIR}"
echo "Install dir : ${INSTALL_DIR}"

# Reuse an already-compiled Skia tree when one is offered (DUI checks for
# <build>/lib/release/libskia.a at configure time and skips gn+ninja when it is there).
if [[ -n "${POLLUXOS_DUI_PREBUILT_SKIA:-}" ]]; then
  if [[ ! -f "${POLLUXOS_DUI_PREBUILT_SKIA}/libskia.a" ]]; then
    echo "error: POLLUXOS_DUI_PREBUILT_SKIA does not contain libskia.a: ${POLLUXOS_DUI_PREBUILT_SKIA}" >&2
    exit 2
  fi
  echo "Prebuilt Skia: ${POLLUXOS_DUI_PREBUILT_SKIA} (skipping the gn+ninja Skia build)"
  mkdir -p "${BUILD_DIR}/lib/release"
  cp -RL "${POLLUXOS_DUI_PREBUILT_SKIA}/." "${BUILD_DIR}/lib/release/"
fi

"${CMAKE_BIN}" -S "${DUI_ROOT}" -B "${BUILD_DIR}" \
  "${GENERATOR[@]}" \
  "${BACKEND_FLAGS[@]}" \
  -DCMAKE_BUILD_TYPE=Release \
  -DCMAKE_POSITION_INDEPENDENT_CODE=ON \
  -DCMAKE_INSTALL_PREFIX="${INSTALL_DIR}" \
  -DDUI_BUILD_EXAMPLES=OFF \
  -DDUI_BUILD_TESTS=OFF \
  -DDUI_BUILD_CEF_EXAMPLES=OFF \
  -DDUI_BUILD_WEBVIEW2_EXAMPLES=OFF \
  -DDUI_ENABLE_CEF=OFF \
  -DDUI_ENABLE_MVVM=OFF

# `dui` is the static library; `dui_entry` is required by the install rules.
"${CMAKE_BIN}" --build "${BUILD_DIR}" --target dui dui_entry -j "$(getconf _NPROCESSORS_ONLN 2>/dev/null || echo 4)"
"${CMAKE_BIN}" --install "${BUILD_DIR}" --prefix "${INSTALL_DIR}"

echo
echo "DUI installed to ${INSTALL_DIR}"
echo "Next: platforms/PolluxOS.DUI/scripts/build-native.sh"
