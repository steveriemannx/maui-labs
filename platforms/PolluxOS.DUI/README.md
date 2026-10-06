# .NET MAUI for PolluxOS (DUI backend)

A .NET MAUI platform backend that renders through **DUI** — the MIT-licensed,
cross-platform C++ toolkit (`dui`, the English port of
[nim_duilib](https://github.com/rhett-lee/nim_duilib)) that describes UI in XML and
draws it with Skia.

> **Status: scaffold / bring-up.** The native bridge, managed backend, sample,
> template, tests and CI wiring are in place.
>
> * **Native half: built and linked (verified on macOS 26 / arm64, dui 0.1.0).**
>   `libdui_shim.dylib` compiles against the DUI headers and links DUI's static
>   archives; it exports exactly the 29 `dui_shim_*` entry points and depends only
>   on system frameworks/libraries (AppKit, Foundation, Cocoa, Metal, QuartzCore,
>   OpenGL, CoreGraphics, CoreText, libz, libc++) — no Homebrew, no X11.
> * **Managed half: not compiled yet** — the repository pins an SDK
>   (`global.json`) that is not installed everywhere, so the C# side has not been
>   through a compiler in this checkout.
> * **Handler set is a starting subset** (Application, Window,
>   ContentPage/ContentView, Layout, Label, Button).

## Why macOS first

DUI already runs on Windows, Linux (X11 + Wayland), macOS and FreeBSD, and its
macOS/Cocoa backend is its most developed one (56 of its commits touch macOS files
vs 28 Windows, 18 Wayland, 14 X11; its own `Progress.md` names macOS the current
verification platform). So macOS is where the bridge can be built and exercised
today without waiting for a PolluxOS toolchain. A PolluxOS target framework is
added once that platform has a .NET runtime/TFM to compile against.

## Architecture

```
MAUI app  (net10.0-macos)
   │  handlers / mappers
   ▼
Microsoft.Maui.Platforms.PolluxOS.DUI          (C#)
   │  P/Invoke over a stable C ABI (LibraryImport, UTF-8, opaque handles)
   ▼
libdui_shim.dylib                              native/dui_shim   (C++20)
   │  try/catch firewall; marshals to the DUI UI thread
   ▼
DUI: ui::GlobalManager / ui::WindowImplBase / ui::Control   (C++20, static)
   ▼
Skia (CPU / OpenGL / Metal)
```

`dui_shim` is not optional plumbing — it is the only way in:

* DUI has **no C API**: zero `extern "C"` in `include/` and `src/`.
* DUI only ever produces **static archives** (`BUILD_SHARED_LIBS` is never set;
  `libdui.a` + `libdui_entry.a` + vendored archives), with `DUI_API` empty on
  macOS/Linux and `-fvisibility=hidden` in the build. There is no shared object to
  load, and none would export symbols. Hence: our dylib links DUI's archives, which
  is why DUI must be configured with `CMAKE_POSITION_INDEPENDENT_CODE=ON`.

The shim also absorbs DUI's sharper edges:

| DUI behaviour | How the bridge handles it |
|---|---|
| Throws C++ exceptions (`std::runtime_error`, `std::bad_alloc`, …) | every entry point is a `try/catch(...)` firewall; errors surface via `dui_shim_last_error()` |
| Single-threaded; `AssertUIThread()` compiles out in Release | calls are queued and drained in `FrameworkThread::OnMessageLoopIdle()` on the UI thread |
| Windows and controls **self-delete** (`Window::OnFinalMessage`, `RequestDeleteControl`) | the shim closes/removes, never `delete`s toolkit objects |
| No control-tree serializer, no `GetAttribute` | the shim walks `Box::GetItemCount/GetItemAt` + `GetType/GetName/GetPos` and emits XML |
| `GetText`/`SetText` are not on `Control` (they live on `LabelOwner`) | text is set through the universal `SetAttribute("text", …)` |
| macOS needs the AppKit run loop to exist before the first window | `dui_shim_run()` pre-warms with `CFRunLoopRunInMode` (like DUI's own `AppEntry.h`) |

## Repository layout

```
platforms/PolluxOS.DUI/
├── Directory.Build.props            # MIT package metadata + DUI path properties
├── LICENSE                          # MIT
├── PolluxOS.DUI.slnx
├── native/dui_shim/                 # C ABI bridge (header + implementation + CMake)
├── scripts/
│   ├── build-dui-macos.sh           # build + install DUI (Ninja, PIC, no examples)
│   └── build-native-macos.sh        # build libdui_shim.dylib against that install
├── src/PolluxOS.DUI/                # handlers, hosting, interop  → NuGet package
├── src/PolluxOS.DUI.Essentials/     # Preferences (+ defaults for the rest)
├── samples/PolluxOS.DUI.Sample/     # runnable bring-up host
├── templates/polluxos-dui-app/      # dotnet new maui-polluxos-dui
└── tests/PolluxOS.DUI.Tests/        # parent-stack contract + bridge smoke tests
```

## Build and run (macOS)

Prerequisites: .NET SDK pinned by `global.json`, the `maui` workload, CMake **4.0+**
(DUI requires it), and Ninja. DUI's configure step downloads Skia (~70 MB) into its
own `third_party/` and builds it from source with gn+ninja — budget ~12+ minutes on
the first run.

```bash
# 1. DUI itself (source tree in, install prefix out)
platforms/PolluxOS.DUI/scripts/build-dui-macos.sh \
  /path/to/dui                      # defaults to the local worktree

# 2. The bridge → platforms/PolluxOS.DUI/artifacts/native/macos/libdui_shim.dylib
platforms/PolluxOS.DUI/scripts/build-native-macos.sh

# 3. Managed backend + tests + template
dotnet build platforms/PolluxOS.DUI/PolluxOS.DUI.slnx

# 4. Run the sample (DUI needs its resources/ directory at runtime)
POLLUXOS_DUI_RESOURCES=<dui-install>/share/dui/resources \
  dotnet run --project platforms/PolluxOS.DUI/samples/PolluxOS.DUI.Sample
```

Path properties (all overridable, defaults live in `Directory.Build.props`):
`PolluxOSDuiRoot`, `PolluxOSDuiInstallDir`, `PolluxOSDuiNativeDir`,
`PolluxOSDuiResourcesDir`.

A managed build deliberately succeeds **without** the native library — the bridge is
needed to run, not to compile. `dotnet build` with `-p:PolluxOSDuiNativeDir=…` copies
the staged dylibs to the output.

## Handler coverage

| MAUI | DUI | State |
|---|---|---|
| `IApplication` | windows collection + theme hint | Open/CloseWindow scaffolded; runtime theming not wired |
| `IWindow` | `WindowImplBase` | create/show/close/title/size |
| `ContentPage`, `ContentView` | `Box` | content realized inside the container |
| `Layout` (StackLayout/Grid/…) | `Box` | Add/Remove/Clear; insert ordering is append-only |
| `Label` | `Label` control | text, colour, font size/family, alignment |
| `Button` | `Button` control | text, colour, padding, click → `IButton.SendClicked()` |
| everything else | — | not implemented yet |

Layout is bridged by writing MAUI's arranged rectangle into the DUI control
(`SetPos` + width/height attributes). Measurement is **not** bridged yet: DUI sizes
controls in its own layout pass, so `dui_shim_widget_measure` reports
"unavailable" and MAUI's explicit sizes win, with a per-control default as fallback.

## Licensing

The backend, the shim and the sample are **MIT** (`PackageLicenseExpression=MIT`,
root `LICENSE`). DUI is **MIT** (`Copyright (c) 2023 rhett-lee`) and is consumed as a
source build, so nothing of it is redistributed here.

Two things to keep in mind when packaging:

* DUI's vendored libraries are permissive (Skia BSD-3-Clause, libpng, zlib, libwebp,
  libjpeg-turbo, giflib, stb_image, nanosvg, pugixml, udis86, libcef, WebView2 SDK,
  ConvertUTF).
* **`third_party/libpag` is GPL/LGPL** (lz4 GPL-2.0 for programs/tests/examples,
  ffavc + FFmpeg LGPL-2.1, Qt components LGPL-3.0). It is Windows-only, disabled by
  default and referenced by no DUI CMake target, so a default build never links it —
  do not enable it without legal review. See `THIRD-PARTY-NOTICES.txt`.

## Known gaps / next steps

1. **Bridge measurement** is the one remaining stub: DUI measures inside its own
   layout pass and exposes no "measure this control for WxH" call, so
   `dui_shim_widget_measure` reports unavailable and MAUI's explicit sizes win.
2. **Keep the deployment targets in step.** DUI's archives and the shim must be
   built with the same `CMAKE_OSX_DEPLOYMENT_TARGET` (the scripts default to 14.0);
   otherwise the link reports min-OS mismatches. DUI's Skia gn args do not pin
   `mac_deployment_target` — worth fixing upstream.
3. **Parenting on XML windows.** `Window::GetRoot()` is used for code-built windows;
   XML windows (`InitSkin`) additionally need `Window::AttachBox`/`GetXmlRoot`.
4. **Wider handler coverage** — the checklist in
   [`../references/PLATFORM_BACKEND_IMPLEMENTATION.md`](../references/PLATFORM_BACKEND_IMPLEMENTATION.md)
   is the authoritative list (23 areas; the AppKit backend is ~131 files for
   comparison).
5. **Essentials** beyond `Preferences`, and a `BlazorWebView` story (DUI has
   CEF/WebView2 integration, both off by default).
6. **Automation/DevFlow**: the shim already exposes a tree dump; DUI also has
   `ScreenCapture::CaptureBitmap` for screenshots, `Window::FindControl` for lookup
   and `Control::AttachClick`/`SetAttribute` for input — the raw material for a
   DevFlow-style agent on this backend.

## Tests and CI

`tests/PolluxOS.DUI.Tests` covers the managed parent-stack contract (no native
library needed) plus bridge smoke tests that report the missing prerequisite instead
of failing when `libdui_shim.dylib` is absent.

`.github/workflows/ci-polluxos-dui.yml` builds and tests the managed solution on
macOS and runs a cheap contract check that every `dui_shim_*` entry point exists in
both `native/dui_shim/include/dui_shim.h` and `Interop/DuiNative.cs` — the two files
must move together. The native build is intentionally not part of every PR: it needs
CMake 4.0+, a Skia build from source, and a DUI checkout.
