# .NET MAUI for PolluxOS (DUI backend)

A .NET MAUI platform backend that renders through **DUI** — the MIT-licensed,
cross-platform C++ toolkit (`dui`, the English port of
[nim_duilib](https://github.com/rhett-lee/nim_duilib)) that describes UI in XML and
draws it with Skia.

The backend is **platform-neutral** (`net10.0`), not tied to a platform SDK TFM: DUI
supplies the windowing and rendering, and a small native bridge supplies the C ABI. The
primary target is **polluxos/FreeBSD**, where no MAUI head exists — macOS already has the
AppKit backend in [`platforms/MacOS`](../MacOS/), so running this one there is only a
convenience for development.

## Status

| Layer | State |
|---|---|
| Native bridge (`libdui_shim.so` / `.dylib`) | ✅ builds and links on **FreeBSD 15 / Wayland** (bmake, CMake 4) and **macOS 26 / arm64**; 31 exported `dui_shim_*` entries; on macOS it links system frameworks only |
| C interop smoke (`native/dui_shim/tests/abi_smoke.c`) | ✅ SMOKE OK on both (window, Label + Button, tree dump, clean shutdown) |
| C# interop layer (`tests/PolluxOS.DUI.Interop.Smoke`) | ✅ SMOKE OK on both, including a DUI click reaching a managed callback |
| MAUI backend + host (`samples/PolluxOS.DUI.Sample`) | ✅ the **MAUI app runs on polluxos/FreeBSD** and its page is realized as DUI controls (verified with a desktop screenshot + widget-tree dump) |
| Layout fidelity | ⚠️ MAUI's cross-platform layout is not bridged into the DUI container yet, so children currently share one rectangle — see *Known gaps* |
| Handler coverage | ⚠️ Application, Window, ContentPage/ContentView, Layout, Label, Button |

## Platform support

| Host | DUI backend | Notes |
|---|---|---|
| polluxos / FreeBSD 15 | Windowing: Wayland (bmake) | Primary target. Needs CMake 4+, a user-local .NET 10 SDK, `wayland-client/egl/cursor`, `xkbcommon`, `epoll-shim` |
| macOS 26 / arm64 | Windowing: AppKit | Development convenience; the AppKit backend is the "native" macOS story |
| Linux / Windows | X11/Wayland, Win32 | DUI supports them; the bridge compiles per platform, untested here |

The managed side has no platform TFM, so the same assemblies run everywhere; only the
native bridge is per-host.

## Architecture

```
MAUI app  (net10.0, Microsoft.Maui.Controls(.Core) from NuGet)
   │  handlers / mappers
   ▼
Microsoft.Maui.Platforms.PolluxOS.DUI          (C#)
   │  P/Invoke over a stable C ABI (LibraryImport, UTF-8, opaque handles)
   ▼
libdui_shim.(so|dylib)                         native/dui_shim   (C++20)
   │  try/catch firewall; UI-thread command queue; idle callback
   ▼
DUI: ui::GlobalManager / ui::WindowImplBase / ui::Control   (C++20, static)
   ▼
Skia (CPU raster / OpenGL / Metal)
```

`dui_shim` is not optional plumbing — it is the only way in:

* DUI has **no C API** (zero `extern "C"` in `include/`/`src/`) and only ever produces
  **static archives** with `-fvisibility=hidden`, so there is no shared object to load:
  the bridge links DUI's archives and is the loadable artifact. DUI must therefore be
  built with `CMAKE_POSITION_INDEPENDENT_CODE=ON`.
* DUI is single-threaded with assert-only thread checks → every call is queued and
  drained on DUI's UI thread. `dui_shim_set_idle_handler` also lets managed code run
  *on* that thread, which is what gives MAUI a real dispatcher on hosts that have none
  (this is the piece that makes polluxos work).
* Windows and controls self-delete (`Window::OnFinalMessage`, `RequestDeleteControl`);
  the shim closes/removes and never `delete`s toolkit objects.
* DUI has no control-tree serializer, so the tree dump (used for automation/evidence)
  is built in the shim from `Box::GetItemCount/GetItemAt` + `GetType/GetName/GetPos`,
  with text read through the `LabelOwner` interface.
* Containers own their children's rectangles: absolute placement uses duilib's
  `float` + fixed size + **margin** (`Layout::GetFloatPos`), and code-built windows get
  a root container attached by the bridge (`Window::AttachBox`).
* `Window::CreateControl` is a virtual hook that returns `nullptr` in the base, so the
  bridge instantiates concrete types itself from the `DUI_CTR_*` names.
* DUI's installed CMake package references `PkgConfig::WAYLAND_*`/`X11`/`EGL`/`GLESV2`
  without creating them; the bridge recreates whichever the host provides (upstream
  packaging gap).

## Repository layout

```
platforms/PolluxOS.DUI/
├── Directory.Build.props            # MIT package metadata + DUI paths (platform-neutral)
├── LICENSE / PolluxOS.DUI.slnx / README.md
├── native/dui_shim/                 # C ABI bridge: header + implementation + CMake + tests
├── scripts/
│   ├── build-dui.sh                 # build + install DUI for the host OS (bmake/Ninja, PIC)
│   └── build-native.sh              # build the bridge, stage it in artifacts/native
├── src/PolluxOS.DUI/                # handlers, hosting, interop, dispatcher → NuGet package
├── src/PolluxOS.DUI.Essentials/     # Preferences (+ MAUI defaults for the rest)
├── samples/PolluxOS.DUI.Sample/     # MAUI-on-DUI host (standalone SDK project)
├── tests/PolluxOS.DUI.Interop.Smoke/# C# interop end-to-end smoke (no MAUI dependency)
├── tests/PolluxOS.DUI.Tests/        # unit tests (parent-stack contract, bridge presence)
└── templates/polluxos-dui-app/      # dotnet new maui-polluxos-dui
```

The sample, the C# smoke test and the template are **standalone SDK projects** (own
shadowed `Directory.Build.props/targets/packages` + `NuGet.config`): they build with a
bare .NET SDK and nuget.org, which is what lets them run on the polluxos box (user-local
SDK, no Arcade, no reachable internal feeds). The shipping `src/` projects keep the
repository's conventions.

## Build and run — polluxos / FreeBSD (primary)

Prerequisites: CMake 4+ (`pkg` has 3.31; the polluxos box keeps 4.x as `cmake4`), bmake,
a .NET 10 SDK (user-local is fine, e.g. `~/dotnet10`), and DUI's Wayland dependencies.

```bash
D=~/dotnet10/dotnet
P=~/projects-main/maui-labs/platforms/PolluxOS.DUI

# 1. DUI itself (Wayland backend, bmake). ~12 min the first time (Skia from source);
#    pass POLLUXOS_DUI_PREBUILT_SKIA=<other-build>/lib/release to reuse a Skia tree.
platforms/PolluxOS.DUI/scripts/build-dui.sh /path/to/dui

# 2. The bridge → platforms/PolluxOS.DUI/artifacts/native/libdui_shim.so
platforms/PolluxOS.DUI/scripts/build-native.sh

# 3. Run a MAUI app on the DUI backend
$D build $P/samples/PolluxOS.DUI.Sample/PolluxOS.DUI.Sample.csproj -c Release
cd $P/samples/PolluxOS.DUI.Sample/bin/Release/net10.0
XDG_RUNTIME_DIR=/var/run/xdg/$USER WAYLAND_DISPLAY=wayland-0 \
  $D PolluxOS.DUI.Sample.dll <dui-install>/share/dui/resources 5
#   the optional trailing number = seconds after which the host dumps the widget tree,
#   captures, and closes itself (used for remote/headless runs)
```

`scripts/build-dui.sh` detects the host: FreeBSD → `Unix Makefiles` + bmake + Wayland;
macOS → Ninja + AppKit with `CMAKE_OSX_DEPLOYMENT_TARGET=14.0`; Linux → Ninja + Wayland.
`scripts/build-native.sh` stages `libdui_shim.*` into `artifacts/native` (override with
`POLLUXOS_DUI_NATIVE_DIR`); managed projects pick it up automatically
(`-p:PolluxOSDuiNativeDir=…`).

macOS uses the same commands (DUI install prefix differs only in that it has no
`share/dui/resources` unless you install one).

## Verification

Remote (polluxos, FreeBSD 15 / Wayland) evidence:

* `grim` desktop capture showing the DUI window with `hello from dui_shim` / `Click me`
  laid out where the code asked, then the **MAUI** app window (`PolluxOS.DUI Sample`)
  with its page text.
* Widget-tree dump from the running MAUI app:
  `window → ShadowBox → Box(maui-root) → Box(ContentPage) → Box(ContentView) →
  Label(TitleLabel) + Label(CounterLabel) + Button(CounterButton)` — `AutomationId`
  becomes the DUI control name, which is what a DevFlow-style agent would query.

Local (macOS): the same C, C# and MAUI hosts build and run; DUI's own
`ScreenCapture::CaptureBitmap` returns null on macOS, so captures there need
`screencapture` (which requires the Screen Recording permission).

## Known gaps / next steps

1. **Bridge MAUI's cross-platform layout into DUI.** The layout container should
   implement `ICrossPlatformLayout` (`CrossPlatformMeasure` / `CrossPlatformArrange`
   forwarding to the virtual view, as the Avalonia MAUI backend's layout panel does) so
   every child handler receives a real rectangle. Today children of a layout share one
   rect, which is why the screenshot shows overlapping text.
2. Wider handler coverage — see
   [`../references/PLATFORM_BACKEND_IMPLEMENTATION.md`](../references/PLATFORM_BACKEND_IMPLEMENTATION.md)
   (23 areas; the AppKit backend is ~131 files for comparison).
3. Essentials beyond `Preferences`, and a `BlazorWebView` story (DUI has CEF/WebView2
   integration, both off by default).
4. DevFlow-style automation: the bridge already exposes tree dump, control lookup,
   click injection and `ScreenCapture`; a capture path that works on macOS is missing.
5. Runtime theme switching: DUI selects its theme (there is a `polluxos` theme next to
   `windows11`/`macos26`/…) from the resource root at startup and exposes no runtime
   switch.

## Licensing

The backend, the bridge and the samples are **MIT** (`PackageLicenseExpression=MIT`,
[LICENSE](LICENSE)). DUI is **MIT** (`Copyright (c) 2023 rhett-lee`) and is consumed as a
source build, so nothing of it is redistributed here.

DUI's vendored libraries are permissive (Skia BSD-3-Clause, libpng, zlib, libwebp,
libjpeg-turbo, giflib, stb_image, nanosvg, pugixml, udis86, libcef, WebView2 SDK,
ConvertUTF). The one exception is **`third_party/libpag`** (lz4 GPL-2.0 for
programs/tests/examples, ffavc + FFmpeg LGPL-2.1, Qt components LGPL-3.0): Windows-only,
disabled by default and referenced by no DUI CMake target, so a default build never links
it — do not enable it without legal review. See `THIRD-PARTY-NOTICES.txt`.

## Tests and CI

* `native/dui_shim/tests/abi_smoke.c` → the `dui_shim_abi_smoke` target: headless checks
  plus a real window run (tree dump verification, capture, clean shutdown).
* `tests/PolluxOS.DUI.Interop.Smoke`: the same flow through the shipping C# interop code.
* `tests/PolluxOS.DUI.Tests`: parent-stack contract + bridge-presence smoke tests.
* `.github/workflows/ci-polluxos-dui.yml`: managed build/tests on macOS plus a cheap
  contract check that every `dui_shim_*` entry point exists in both
  `native/dui_shim/include/dui_shim.h` and `Interop/DuiNative.cs` (they must move
  together). The native build is not part of every PR: it needs CMake 4+, a DUI checkout
  and a Skia build.
