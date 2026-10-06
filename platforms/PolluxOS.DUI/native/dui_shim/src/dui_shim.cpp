// dui_shim.cpp — C ABI bridge over the DUI C++ toolkit (MIT, nim_duilib lineage).
//
// Why this file exists:
//   DUI has no C API at all (zero `extern "C"` in include/ and src/), it only ever
//   produces STATIC archives (`libdui.a`, no BUILD_SHARED_LIBS, DUI_API empty on
//   macOS/Linux), and it is compiled with -fvisibility=hidden. A .NET host
//   therefore cannot P/Invoke DUI: this shim is the ABI boundary, and the shared
//   library that comes out of it is the only loadable artifact.
//
// Ground rules encoded here (all verified against the DUI headers):
//   * Every entry point is a try/catch firewall. DUI throws (std::runtime_error,
//     std::bad_alloc, ...) and an exception escaping into managed code would
//     terminate the .NET process.
//   * DUI is single-threaded: GlobalManager/WindowManager/FontManager are
//     unsynchronized and GlobalManager::AssertUIThread() only fires in Debug.
//     Calls from other threads are queued and drained on the UI thread in
//     FrameworkThread::OnMessageLoopIdle().
//   * Windows and controls are self-deleting (`Window::OnFinalMessage()` deletes
//     the window; controls are deleted through Window::RequestDeleteControl).
//     The shim closes/removes, it never `delete`s toolkit objects.
//   * DUI is C++20 and must be compiled with CMAKE_POSITION_INDEPENDENT_CODE=ON
//     so its static archives can be linked into this shared library.
//
// Verified against the dui 0.1.0 headers (build 2026-10): startup/shutdown,
// window create/show/close/title/size, control create/bounds/text/visibility/
// enabled/attributes/click, the tree walk and the message-loop pump all compile
// and link. One entry point is still a stub:
//   TODO(1) dui_shim_widget_measure — DUI measures inside its own layout pass and
//           exposes no "measure this control for WxH" call; it returns
//           "unavailable" so the managed side falls back to MAUI measurement.

#include "dui_shim.h"

#include "dui/dui.h"

// Screen capture (dui_shim_window_capture_ppm) is not part of the dui.h umbrella.
#include "dui/Utils/ScreenCapture.h"

#if defined(__APPLE__)
// Platform message loops are not part of the dui.h umbrella: each one is guarded
// by its DUI_BUILD_FOR_* macro (defined in dui_config.h), so include it explicitly.
#include "dui/Core/MessageLoop_MacOS.h"
#endif

#include <atomic>
#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <deque>
#include <functional>
#include <future>
#include <memory>
#include <mutex>
#include <string>
#include <vector>

#if defined(__APPLE__)
#include <CoreFoundation/CoreFoundation.h>
#endif

namespace {

constexpr const char* kVersion = "0.1.0";

// DUI uses UTF-8 std::string everywhere (include/dui/dui_string.h).
thread_local std::string g_last_error;

// Errors raised by queued (deferred) commands run on the UI thread, where the
// caller's thread-local error is invisible. Kept here so dui_shim_last_error() can
// report why a handle (e.g. a widget created before the loop started) never
// materialized.
std::mutex g_async_error_mutex;
std::string g_async_error;

// True while the toolkit's message loop runs. Sync reads use it to fail fast instead of
// waiting for a drain that cannot happen yet (a host inspecting the tree before run()).
std::atomic<bool> g_loop_running{false};

void SetError(const std::string& message)
{
    g_last_error = message;
}

/// Opt-in tracing (`DUI_SHIM_DEBUG=1`): the deferred commands run on the UI thread,
/// so their diagnostics have to be observable somehow while bringing a platform up.
bool DebugEnabled()
{
    static const bool enabled = std::getenv("DUI_SHIM_DEBUG") != nullptr;
    return enabled;
}

void Trace(const std::string& message)
{
    if (DebugEnabled())
        std::fprintf(stderr, "[dui_shim] %s\n", message.c_str());
}

/// Reports a failure that happened on the UI thread: visible to the UI thread and
/// to every later dui_shim_last_error() caller on any thread.
void SetAsyncError(const std::string& message)
{
    g_last_error = message;
    std::lock_guard<std::mutex> lock(g_async_error_mutex);
    g_async_error = message;
}

void ClearError()
{
    g_last_error.clear();
}

std::string Utf8(const char* text)
{
    return text != nullptr ? std::string(text) : std::string();
}

// Exception firewall. DUI throws; nothing may cross the ABI boundary.
#define DUI_SHIM_GUARD_BEGIN try {
#define DUI_SHIM_GUARD_END(fallback)                     \
    }                                                    \
    catch (const std::exception& ex) {                   \
        SetError(std::string("dui: ") + ex.what());      \
        return fallback;                                 \
    }                                                    \
    catch (...) {                                        \
        SetError("dui: unknown C++ exception");          \
        return fallback;                                 \
    }

// ---------------------------------------------------------------------------
// UI-thread marshalling
// ---------------------------------------------------------------------------

class CommandQueue
{
public:
    void Post(std::function<void()> command)
    {
        std::lock_guard<std::mutex> lock(m_mutex);
        m_commands.push_back(std::move(command));
    }

    // Called from FrameworkThread::OnMessageLoopIdle() — i.e. on the UI thread,
    // which is the only place DUI state may be touched.
    void Drain()
    {
        std::deque<std::function<void()>> drained;
        {
            std::lock_guard<std::mutex> lock(m_mutex);
            if (m_commands.empty())
                return;
            drained.swap(m_commands);
        }
        for (auto& command : drained)
            command();
    }

private:
    std::mutex m_mutex;
    std::deque<std::function<void()>> m_commands;
};

} // namespace

// ---------------------------------------------------------------------------
// Handles
// ---------------------------------------------------------------------------

struct dui_shim_widget
{
    // Resolved on the UI thread. nullptr means "queued, not created yet": hosts
    // (and the MAUI backend in particular) build their widget tree before the
    // message loop starts, so handles must exist before the DUI controls do.
    ui::Control* control = nullptr;
    dui_shim_window* owner = nullptr;
    dui_shim_widget* parent = nullptr;
    std::string class_name;
    std::string name;
    bool destroyed = false;
};

struct dui_shim_window
{
    // Owned by DUI: Window::OnFinalMessage() deletes it. Never `delete` this.
    ui::WindowImplBase* window = nullptr;
    std::string name;
    std::string title;
    std::string skin_folder;
    std::string skin_file;
    int32_t width = 800;
    int32_t height = 600;
    bool show_requested = false;
    bool shown = false;
    dui_shim_event_cb handler = nullptr;
    void* handler_user_data = nullptr;
    dui_shim_widget root;
    std::vector<dui_shim_widget*> owned_widgets;

    /// Host hook for client-area changes (see dui_shim_window_set_size_handler).
    dui_shim_size_cb size_cb = nullptr;
    void* size_user = nullptr;

    /// Whether the toolkit should attach its window shadow/decoration (default: yes).
    bool shadow_attached = true;
};

namespace {

// FrameworkThread("name", kThreadUI): DUI's own UI-thread base class. OnInit runs
// on that thread before the loop starts, OnCleanup after it exits, and
// OnMessageLoopIdle ticks inside the loop (FrameworkThread.h:54,121,129,133).
class DuiHost : public ui::FrameworkThread
{
public:
    DuiHost() : ui::FrameworkThread("PolluxOS.DUI.Shim", ui::kThreadUI) {}

    static DuiHost& Instance()
    {
        static DuiHost host;
        return host;
    }

    bool Start(const std::string& resource_root, const std::string& locale)
    {
        m_resource_root = resource_root;
        m_locale = locale;
        m_started = true;
        return true;
    }

    void Shutdown()
    {
        m_started = false;
    }

    bool Started() const { return m_started; }

    /// Host hook: the idle handler runs on the UI thread inside the toolkit's loop.
    void SetIdleHandler(dui_shim_idle_cb handler, void* user_data)
    {
        m_idle_handler = handler;
        m_idle_user_data = user_data;
    }

    CommandQueue& Commands() { return m_commands; }

    void AddWindow(dui_shim_window* handle) { m_windows.push_back(handle); }

    /// Closes every tracked window (runs on the UI thread). Closing the last one
    /// ends the loop: every window is created with PostQuitMsgWhenClosed(true).
    void CloseAllWindows()
    {
        for (dui_shim_window* window : m_windows)
        {
            if (window->window != nullptr)
            {
                window->window->CloseWnd();
                window->window = nullptr; // the toolkit deletes it on the final message
            }
        }
    }

    void RemoveWindow(dui_shim_window* handle)
    {
        for (auto it = m_windows.begin(); it != m_windows.end(); ++it)
        {
            if (*it == handle)
            {
                m_windows.erase(it);
                return;
            }
        }
    }

protected:
    void OnInit() override;
    void OnCleanup() override;
    void OnMessageLoopIdle() override;

private:
    bool m_started = false;
    std::string m_resource_root;
    std::string m_locale;
    CommandQueue m_commands;
    std::vector<dui_shim_window*> m_windows;
    dui_shim_idle_cb m_idle_handler = nullptr;
    void* m_idle_user_data = nullptr;
};

// Applies a host-requested window size. `Resize(..., bContainShadow=false)` makes the
// requested size the *client* area, so the host's layout matches the window exactly;
// `SetWindowSize()` would fold DUI's shadow corner into it (the host then draws its tree
// inside a larger decorated surface, which reads as a window inside a window).
#if defined(__APPLE__)
// Implemented in macos_chrome.mm: dui hides the AppKit title bar and the traffic lights
// for windows that draw their own caption, so the host restores them.
extern "C" void* dui_shim_macos_find_window(const char* title);
extern "C" void dui_shim_macos_show_system_chrome(void* nsWindow, const char* title);
extern "C" int dui_shim_macos_describe_chrome(void* nsWindow, int* styleMask, int* buttonsHidden, int* titleVisible);
#endif

ui::UiSize UsableClientSize(dui_shim_window* window)
{
    ui::UiRect rc;
    window->window->GetClientRect(rc);

    int32_t width = rc.Width();
    int32_t height = rc.Height();
    if (window->root.control != nullptr)
    {
        const ui::UiRect rootPos = window->root.control->GetPos();
        width -= rootPos.left * 2;
        height -= rootPos.top * 2;
    }

    return ui::UiSize(width > 0 ? width : 0, height > 0 ? height : 0);
}

void ApplyClientSize(ui::Window* window, int width, int height)
{
    if (window == nullptr || width <= 0 || height <= 0)
        return;

    window->Resize(width, height, false, false);
    window->InvalidateAll();
    window->UpdateWindow();
}

// Runs on the UI thread. Mirrors ui::RunWindow() (include/dui/Utils/UiBuilder.h):
// GlobalManager::Startup -> new WindowImplBase -> CreateWnd -> PostQuitMsgWhenClosed
// -> InvalidateAll/UpdateWindow/ShowWindow, with GlobalManager::Shutdown in cleanup.
void DuiHost::OnInit()
{
    if (!m_resource_root.empty())
    {
        if (!ui::GlobalManager::Instance().Startup(ui::LocalFilesResParam(ui::FilePath(m_resource_root))))
            SetError("dui: GlobalManager::Startup failed for resource root '" + m_resource_root + "'");
    }

    for (dui_shim_window* handle : m_windows)
    {
        if (handle->window != nullptr)
            continue;

        auto* window = new ui::WindowImplBase();
        if (!handle->skin_file.empty())
        {
            // XML mode: the window parses resources/themes/<theme>/<skin>/<file>.xml
            window->InitSkin(handle->skin_folder, handle->skin_file);
        }

        if (!window->CreateWnd(nullptr, ui::WindowCreateParam(handle->title, true)))
        {
            SetAsyncError("dui: WindowImplBase::CreateWnd failed for window '" + handle->name + "'");
            // Do not delete: the toolkit owns window lifetime.
            continue;
        }

        // The decoration must be chosen *after* CreateWnd: Window::PreInitWindow (run
        // inside it) is what creates the shadow object, and SetShadowAttached before that
        // is a no-op. Window::AttachBox wraps the root in a ShadowBox only while the shadow
        // is attached, so setting it here is what decides whether the host's content fills
        // the window or sits inside a decoration frame. On macOS the attached shadow is
        // also what makes AppKit give the window its title bar and traffic lights.
        window->SetUseDefaultShadowAttached(false);
        window->SetShadowAttached(handle->shadow_attached);

#if defined(__APPLE__)
        // Restore the macOS title bar + traffic lights (see macos_chrome.mm).
        if (void* nsWindow = dui_shim_macos_find_window(handle->title.c_str()))
        {
            dui_shim_macos_show_system_chrome(nsWindow, handle->title.c_str());

            int styleMask = 0;
            int buttonsHidden = 0;
            int titleVisible = 0;
            if (dui_shim_macos_describe_chrome(nsWindow, &styleMask, &buttonsHidden, &titleVisible) != 0)
            {
                Trace(std::string("macOS chrome: styleMask=") + std::to_string(styleMask)
                      + " trafficLightsHidden=" + (buttonsHidden ? "1" : "0")
                      + " titleVisible=" + (titleVisible ? "1" : "0"));
            }
        }
#endif

        window->PostQuitMsgWhenClosed(true);
        handle->window = window;

        // Size changes (user resize, compositor configure) are forwarded to the host so it
        // can re-lay out, instead of keeping whatever size the app started with.
        window->AttachWindowSizeMsg([handle](const ui::EventArgs&) {
            if (handle->size_cb != nullptr)
            {
                const ui::UiSize size = UsableClientSize(handle);
                if (size.cx > 0 && size.cy > 0)
                    handle->size_cb(handle->size_user, size.cx, size.cy);
            }
            return true;
        });

        // WindowCreateParam carries no size, so apply the requested one explicitly:
        // without this DUI falls back to its skin/default size (800x600), which is why a
        // 380x560 calculator window came up at the wrong size with its layout in a corner.
        ApplyClientSize(window, handle->width, handle->height);

        // A pure-code window has no root container until one is attached: the XML
        // path creates it through WindowBuilder, while the code path expects the host
        // to call AttachBox (see the sequence documented in include/dui/Core/Window.h).
        // Hosts parent their tree to dui_shim_window_root(), so the bridge attaches a
        // vertical container; MAUI positions children explicitly.
        if (window->GetRoot() == nullptr)
        {
            // Plain Box, not VBox: MAUI computes every child's rectangle itself, so the
            // root must not run a layout that would override SetPos/SetFixedWidth.
            auto* root = new ui::Box(window);
            root->SetName("maui-root");
            if (!window->AttachBox(root))
                SetAsyncError("dui: Window::AttachBox failed for window '" + handle->name + "'");
            else
            {
                // Track the container explicitly: Window::GetRoot() returns the shadow
                // wrapper on some backends (a ShadowBox on Wayland), and hosts must
                // parent to the container we attached, not to the decoration.
                handle->root.control = root;
                Trace("attached root Box to window '" + handle->name + "'");
            }
        }

        if (handle->show_requested)
        {
            window->InvalidateAll();
            window->UpdateWindow();
            window->ShowWindow(ui::kSW_SHOW_NORMAL);
            handle->shown = true;

            // Some backends (Wayland) only honour a resize once the surface is mapped, so
            // re-apply the requested size here; without it the window keeps DUI's default
            // 800x600 and the host's layout ends up in a corner.
            ApplyClientSize(window, handle->width, handle->height);
        }
    }

    // From here the idle loop services the command queue, so synchronous reads
    // (tree dump, control lookup) may wait for the UI thread.
    g_loop_running.store(true);
}

void DuiHost::OnCleanup()
{
    g_loop_running.store(false);
    for (dui_shim_window* handle : m_windows)
        handle->window = nullptr; // closed by the toolkit on final message
    m_windows.clear();
    ui::GlobalManager::Instance().Shutdown();
}

void DuiHost::OnMessageLoopIdle()
{
    m_commands.Drain();

    // Host hook: this runs on the UI thread inside the toolkit's loop, the only place
    // a managed dispatcher can execute with the right thread affinity.
    if (m_idle_handler != nullptr)
        m_idle_handler(m_idle_user_data);
}

DuiHost& Host()
{
    return DuiHost::Instance();
}

void OnUiThread(std::function<void()> action)
{
    Host().Commands().Post(std::move(action));
}

// Runs `action` on the UI thread and waits for it. For read paths that must touch
// the toolkit from a host thread (control lookup, tree dump). Returns false — instead of
// blocking forever — when the message loop is not running yet or the toolkit does not
// service the queue in time. Must NOT be called from the UI thread itself.
bool RunOnUiThreadSync(std::function<void()> action)
{
    if (!g_loop_running.load())
    {
        SetError("dui: the message loop is not running yet (call dui_shim_run first)");
        return false;
    }

    std::promise<void> done;
    auto future = done.get_future();
    OnUiThread([&action, &done]() {
        try
        {
            action();
        }
        catch (const std::exception& ex)
        {
            SetAsyncError(std::string("dui: ") + ex.what());
        }
        catch (...)
        {
            SetAsyncError("dui: unknown C++ exception");
        }
        done.set_value();
    });

    if (future.wait_for(std::chrono::seconds(5)) != std::future_status::ready)
    {
        SetError("dui: timed out waiting for the toolkit's UI thread");
        return false;
    }
    return true;
}

ui::Box* RootBox(dui_shim_window* handle)
{
    if (handle == nullptr || handle->window == nullptr)
        return nullptr;
    return handle->window->GetRoot();
}

dui_shim_widget* WrapWidget(dui_shim_window* owner, ui::Control* control)
{
    if (control == nullptr)
        return nullptr;
    auto* widget = new dui_shim_widget();
    widget->control = control;
    widget->owner = owner;
    if (owner != nullptr)
        owner->owned_widgets.push_back(widget);
    return widget;
}
void UnwrapWidget(dui_shim_widget* widget)
{
    if (widget == nullptr || widget->owner == nullptr)
        return;
    auto& owned = widget->owner->owned_widgets;
    for (auto it = owned.begin(); it != owned.end(); ++it)
    {
        if (*it == widget)
        {
            owned.erase(it);
            break;
        }
    }
}

// --- tree serialization -----------------------------------------------------
//
// DUI has no control-tree serializer (no Dump/ToXml/EnumControls anywhere in the
// headers), so the automation-facing snapshot is built here from the primitives
// the headers do expose: Box::GetItemCount/GetItemAt, PlaceHolder::GetType/GetName,
// Control::GetPos, PlaceHolder::IsVisible/IsEnabled.

// --- control factory --------------------------------------------------------
//
// DUI's pure-code path builds controls with `new T(window)` (see ui::Create<T> in
// include/dui/Utils/UiBuilder.h). Window::CreateControl is a virtual hook that the
// base class leaves returning nullptr — it exists for WindowBuilder's XML path
// (src/Core/Window.cpp:228) — so the bridge maps the DUI_CTR_* class names from
// include/dui/dui_defs.h to the concrete types itself.
ui::Control* InstantiateControl(ui::Window* window, const std::string& class_name)
{
    if (class_name == DUI_CTR_LABEL)    return new ui::Label(window);
    if (class_name == DUI_CTR_BUTTON)   return new ui::Button(window);
    if (class_name == DUI_CTR_CHECKBOX) return new ui::CheckBox(window);
    if (class_name == DUI_CTR_OPTION)   return new ui::Option(window);
    if (class_name == DUI_CTR_PROGRESS) return new ui::Progress(window);
    if (class_name == DUI_CTR_SLIDER)   return new ui::Slider(window);
    if (class_name == DUI_CTR_BOX)      return new ui::Box(window);
    if (class_name == DUI_CTR_VBOX)     return new ui::VBox(window);
    if (class_name == DUI_CTR_HBOX)     return new ui::HBox(window);
    return nullptr;
}

void AppendXmlAttribute(std::string& out, const char* name, const std::string& value){
    out += ' ';
    out += name;
    out += "=\"";
    for (char c : value)
    {
        switch (c)
        {
        case '&': out += "&amp;"; break;
        case '<': out += "&lt;"; break;
        case '>': out += "&gt;"; break;
        case '"': out += "&quot;"; break;
        default: out += c; break;
        }
    }
    out += '"';
}

void WalkControl(ui::Control* control, int depth, std::string& out)
{
    if (control == nullptr)
        return;

    out.append(static_cast<size_t>(depth) * 2, ' ');
    out += "<control";
    AppendXmlAttribute(out, "type", control->GetType());
    AppendXmlAttribute(out, "name", control->GetName());
    const ui::UiRect rect = control->GetPos();
    AppendXmlAttribute(out, "bounds", std::to_string(rect.left) + "," + std::to_string(rect.top) + "," +
                                          std::to_string(rect.right - rect.left) + "," + std::to_string(rect.bottom - rect.top));
    AppendXmlAttribute(out, "visible", control->IsVisible() ? "true" : "false");
    AppendXmlAttribute(out, "enabled", control->IsEnabled() ? "true" : "false");

    // Control has no GetText — text lives on the LabelOwner interface (Label/Button/
    // RichEdit/…), so read it through the interface when the control implements it.
    if (auto* label = dynamic_cast<ui::LabelOwner*>(control); label != nullptr)
    {
        const std::string text = label->GetText();
        if (!text.empty())
            AppendXmlAttribute(out, "text", text);
    }

    auto* box = dynamic_cast<ui::Box*>(control);
    if (box == nullptr || box->GetItemCount() == 0)
    {
        out += " />\n";
        return;
    }

    out += ">\n";
    for (size_t i = 0; i < box->GetItemCount(); ++i)
        WalkControl(box->GetItemAt(i), depth + 1, out);
    out.append(static_cast<size_t>(depth) * 2, ' ');
    out += "</control>\n";
}

} // namespace

// ---------------------------------------------------------------------------
// Runtime
// ---------------------------------------------------------------------------

extern "C" {

int32_t dui_shim_startup(const char* resource_root_utf8, const char* locale_utf8)
{
    DUI_SHIM_GUARD_BEGIN
    ClearError();
    const bool ok = Host().Start(Utf8(resource_root_utf8), Utf8(locale_utf8));
    return ok ? 0 : 1;
    DUI_SHIM_GUARD_END(1)
}

void dui_shim_shutdown(void)
{
    DUI_SHIM_GUARD_BEGIN
    Host().Shutdown();
    DUI_SHIM_GUARD_END()
}

int32_t dui_shim_run(void)
{
    DUI_SHIM_GUARD_BEGIN
    ClearError();
#if defined(__APPLE__)
    // macOS needs NSApplication's run loop to exist before the first window is
    // created — DUI's own app-entry macros do this too (AppEntry.h:80-84).
    CFRunLoopRunInMode(kCFRunLoopDefaultMode, 0, false);
#endif
    // Blocks until the last window requests quit (PostQuitMsgWhenClosed(true)).
    Host().RunMessageLoop(true);
    return 0;
    DUI_SHIM_GUARD_END(1)
}

void dui_shim_post_quit(int32_t exit_code)
{
    DUI_SHIM_GUARD_BEGIN
    // Portable quit: closing every window ends the loop, because the shim creates
    // each window with Window::PostQuitMsgWhenClosed(true). DUI's platform loops
    // expose no portable PostQuitMsg (Wayland/X11 only offer RunUserLoop and
    // PostUserEvent), so on macOS we additionally post the native quit.
#if defined(__APPLE__)
    ui::MessageLoop_MacOS::PostQuitMsg(exit_code);
#else
    (void)exit_code;
#endif
    OnUiThread([]() { Host().CloseAllWindows(); });
    DUI_SHIM_GUARD_END()
}

const char* dui_shim_last_error(void)
{
    if (!g_last_error.empty())
        return g_last_error.c_str();

    // Fall back to the last error reported by a deferred (UI-thread) command.
    thread_local std::string s_async_copy;
    std::lock_guard<std::mutex> lock(g_async_error_mutex);
    s_async_copy = g_async_error;
    return s_async_copy.c_str();
}

const char* dui_shim_version(void)
{
    return kVersion;
}

void dui_shim_set_idle_handler(dui_shim_idle_cb callback, void* user_data)
{
    DUI_SHIM_GUARD_BEGIN
    Host().SetIdleHandler(callback, user_data);
    DUI_SHIM_GUARD_END()
}

void dui_shim_set_theme(int32_t dark)
{
    DUI_SHIM_GUARD_BEGIN
    // DUI has no runtime theme switch in its public API: the theme is chosen at
    // Startup from the resource root (resources/themes/<theme> — the toolkit ships
    // a `polluxos` theme next to windows11/macos26/gnome). GlobalManager exposes
    // GetThemeDefaultPath() to read it back. Reported rather than silently ignored.
    SetError(dark != 0 ? "dui: runtime dark theme is not supported — select the theme at Startup"
                       : "dui: runtime light theme is not supported — select the theme at Startup");
    DUI_SHIM_GUARD_END()
}

// ---------------------------------------------------------------------------
// Windows
// ---------------------------------------------------------------------------

dui_shim_window* dui_shim_window_create(const char* name_utf8, const char* title_utf8,
                                       int32_t width_dip, int32_t height_dip,
                                       const char* skin_folder_utf8, const char* skin_file_utf8)
{
    DUI_SHIM_GUARD_BEGIN
    ClearError();
    auto* handle = new dui_shim_window();
    handle->name = Utf8(name_utf8);
    handle->title = Utf8(title_utf8);
    handle->width = width_dip;
    handle->height = height_dip;
    handle->skin_folder = Utf8(skin_folder_utf8);
    handle->skin_file = Utf8(skin_file_utf8);
    handle->root.owner = handle;
    Host().AddWindow(handle);
    return handle;
    DUI_SHIM_GUARD_END(nullptr)
}

void dui_shim_window_show(dui_shim_window* window, int32_t show)
{
    DUI_SHIM_GUARD_BEGIN
    if (window == nullptr)
        return;
    window->show_requested = show != 0;
    OnUiThread([window, show]() {
        if (window->window == nullptr)
            return; // OnInit() replays the show request
        if (show != 0 && !window->shown)
        {
            window->window->InvalidateAll();
            window->window->UpdateWindow();
            window->window->ShowWindow(ui::kSW_SHOW_NORMAL);
            window->shown = true;

            // See OnInit: apply the requested size after the surface is mapped.
            ApplyClientSize(window->window, window->width, window->height);
        }
        else if (show == 0 && window->shown)
        {
            window->window->ShowWindow(ui::kSW_HIDE);
            window->shown = false;
        }
    });
    DUI_SHIM_GUARD_END()
}

void dui_shim_window_close(dui_shim_window* window)
{
    DUI_SHIM_GUARD_BEGIN
    if (window == nullptr)
        return;
    OnUiThread([window]() {
        if (window->window != nullptr)
        {
            window->window->CloseWnd();
            window->window = nullptr; // toolkit deletes it on the final message
        }
        if (window->handler != nullptr)
            window->handler(window->handler_user_data, nullptr, DUI_SHIM_EVENT_CLOSED);
    });
    Host().RemoveWindow(window);
    DUI_SHIM_GUARD_END()
}

void dui_shim_window_set_title(dui_shim_window* window, const char* title_utf8)
{
    DUI_SHIM_GUARD_BEGIN
    if (window == nullptr)
        return;
    const std::string title = Utf8(title_utf8);
    window->title = title;
    OnUiThread([window, title]() {
        if (window->window != nullptr)
            window->window->SetText(title);
    });
    DUI_SHIM_GUARD_END()
}

// Usable client area: the window surface minus the decoration inset the host's container
// is attached in (WindowBase::GetShadowCorner is protected, but the toolkit has already
// laid the container out at that inset, so its position is the inset).

int32_t dui_shim_window_get_client_size(dui_shim_window* window, int32_t* width, int32_t* height)
{
    if (window == nullptr || window->window == nullptr)
    {
        SetError("dui: null window");
        return 0;
    }

    int32_t client_width = 0;
    int32_t client_height = 0;
    const bool read = RunOnUiThreadSync([&]() {
        const ui::UiSize size = UsableClientSize(window);
        client_width = size.cx;
        client_height = size.cy;
    });

    if (width != nullptr)
        *width = client_width;
    if (height != nullptr)
        *height = client_height;

    return read && client_width > 0 && client_height > 0 ? 1 : 0;
}

void dui_shim_window_set_size(dui_shim_window* window, int32_t width_dip, int32_t height_dip)
{
    DUI_SHIM_GUARD_BEGIN
    if (window == nullptr)
        return;
    window->width = width_dip;
    window->height = height_dip;
    OnUiThread([window, width_dip, height_dip]() {
        if (window->window == nullptr)
            return;
        // The managed side passes device-independent pixels, same units as the layout.
        ApplyClientSize(window->window, width_dip, height_dip);
    });
    DUI_SHIM_GUARD_END()
}

void dui_shim_window_get_bounds(dui_shim_window* window, double* x_dip, double* y_dip, double* width_dip, double* height_dip)
{
    DUI_SHIM_GUARD_BEGIN
    if (window == nullptr || window->window == nullptr)
        return;
    const ui::UiRect rect = window->window->GetWindowPos(false);
    if (x_dip != nullptr) *x_dip = rect.left;
    if (y_dip != nullptr) *y_dip = rect.top;
    if (width_dip != nullptr) *width_dip = rect.right - rect.left;
    if (height_dip != nullptr) *height_dip = rect.bottom - rect.top;
    DUI_SHIM_GUARD_END()
}

dui_shim_widget* dui_shim_window_root(dui_shim_window* window)
{
    DUI_SHIM_GUARD_BEGIN
    if (window == nullptr)
        return nullptr;
    // Always returns a usable handle: before OnInit() creates the native window the
    // control is null and resolves on the UI thread; children created against this
    // handle are parented to the container the shim attached.
    window->root.owner = window;
    window->root.parent = nullptr;
    if (window->root.control == nullptr)
        window->root.control = RootBox(window);
    return &window->root;
    DUI_SHIM_GUARD_END(nullptr)
}

dui_shim_widget* dui_shim_window_find_widget(dui_shim_window* window, const char* name_utf8)
{
    DUI_SHIM_GUARD_BEGIN
    if (window == nullptr || window->window == nullptr)
        return nullptr;
    // Control lookup walks the toolkit tree, so it runs on the UI thread.
    const std::string name = Utf8(name_utf8);
    ui::Control* found = nullptr;
    RunOnUiThreadSync([&]() {
        if (window->window != nullptr)
            found = window->window->FindControl(name);
    });
    return WrapWidget(window, found);
    DUI_SHIM_GUARD_END(nullptr)
}

void dui_shim_window_set_event_handler(dui_shim_window* window, dui_shim_event_cb callback, void* user_data)
{
    DUI_SHIM_GUARD_BEGIN
    if (window == nullptr)
        return;
    window->handler = callback;
    window->handler_user_data = user_data;
    DUI_SHIM_GUARD_END()
}

char* dui_shim_window_dump_xml(dui_shim_window* window, size_t* out_len)
{
    DUI_SHIM_GUARD_BEGIN
    if (out_len != nullptr)
        *out_len = 0;
    if (window == nullptr)
    {
        SetError("dui: no window");
        return nullptr;
    }

    // The walk touches toolkit state, so it happens on the UI thread.
    std::string xml;
    bool built = false;
    RunOnUiThreadSync([&]() {
        if (window->window == nullptr)
        {
            SetError("dui: window is not created yet");
            return;
        }

        xml = "<window";
        AppendXmlAttribute(xml, "name", window->name);
        AppendXmlAttribute(xml, "title", window->title);
        xml += ">\n";
        WalkControl(window->window->GetRoot(), 1, xml);
        xml += "</window>\n";
        built = true;
    });

    if (!built)
        return nullptr;

    auto* buffer = new char[xml.size() + 1];
    std::copy(xml.begin(), xml.end(), buffer);
    buffer[xml.size()] = '\0';
    if (out_len != nullptr)
        *out_len = xml.size();
    return buffer;
    DUI_SHIM_GUARD_END(nullptr)
}

void dui_shim_string_free(char* text)
{
    delete[] text;
}

int32_t dui_shim_window_capture_ppm(dui_shim_window* window, const char* path_utf8)
{
    DUI_SHIM_GUARD_BEGIN
    if (window == nullptr || path_utf8 == nullptr)
    {
        SetError("dui: capture needs a window and a path");
        return 1;
    }

    const std::string path = Utf8(path_utf8);
    int32_t result = 1;

    // Capture touches the toolkit's render state, so it runs on the UI thread.
    RunOnUiThreadSync([&]() {
        if (window->window == nullptr)
        {
            SetAsyncError("dui: capture: the window is not created yet");
            return;
        }

        std::shared_ptr<ui::IBitmap> bitmap = ui::ScreenCapture::CaptureBitmap(window->window);
        if (!bitmap)
        {
            SetAsyncError("dui: ScreenCapture::CaptureBitmap returned null");
            return;
        }

        const uint32_t width = bitmap->GetWidth();
        const uint32_t height = bitmap->GetHeight();
        void* pixels = bitmap->LockPixelBits();
        if (pixels == nullptr || width == 0 || height == 0)
        {
            if (pixels != nullptr)
                bitmap->UnLockPixelBits();
            SetAsyncError("dui: capture: the bitmap has no pixels");
            return;
        }

        std::FILE* file = std::fopen(path.c_str(), "wb");
        if (file == nullptr)
        {
            bitmap->UnLockPixelBits();
            SetAsyncError("dui: capture: cannot open '" + path + "'");
            return;
        }

        std::fprintf(file, "P6\n%u %u\n255\n", width, height);

        // Skia N32 on little-endian: BGRA, premultiplied. Flatten alpha onto black.
        const uint8_t* source = static_cast<const uint8_t*>(pixels);
        std::vector<uint8_t> row(static_cast<size_t>(width) * 3);
        for (uint32_t y = 0; y < height; ++y)
        {
            const uint8_t* line = source + static_cast<size_t>(y) * width * 4;
            for (uint32_t x = 0; x < width; ++x)
            {
                const uint8_t a = line[x * 4 + 3];
                row[x * 3 + 0] = static_cast<uint8_t>(a == 0 ? 0 : (line[x * 4 + 2] * 255) / a);
                row[x * 3 + 1] = static_cast<uint8_t>(a == 0 ? 0 : (line[x * 4 + 1] * 255) / a);
                row[x * 3 + 2] = static_cast<uint8_t>(a == 0 ? 0 : (line[x * 4 + 0] * 255) / a);
            }
            std::fwrite(row.data(), 1, row.size(), file);
        }

        std::fclose(file);
        bitmap->UnLockPixelBits();
        Trace("captured " + std::to_string(width) + "x" + std::to_string(height) + " -> " + path);
        result = 0;
    });

    return result;
    DUI_SHIM_GUARD_END(1)
}

// ---------------------------------------------------------------------------
// Widgets
// ---------------------------------------------------------------------------

dui_shim_widget* dui_shim_widget_create(dui_shim_widget* parent, const char* class_name_utf8, const char* name_utf8)
{
    DUI_SHIM_GUARD_BEGIN
    ClearError();
    if (parent == nullptr)
    {
        SetError("dui: widget_create needs a parent widget");
        return nullptr;
    }

    // Handle first, control later: hosts build the tree before the message loop
    // starts, so the DUI control is created from the UI-thread queue. Commands run
    // FIFO, so a parent always resolves before its children.
    auto* widget = new dui_shim_widget();
    widget->parent = parent;
    widget->owner = parent->owner;
    widget->class_name = Utf8(class_name_utf8);
    widget->name = Utf8(name_utf8);
    if (widget->owner != nullptr)
        widget->owner->owned_widgets.push_back(widget);

    OnUiThread([widget]() {
        dui_shim_window* owner = widget->owner;
        Trace("create widget class='" + widget->class_name + "' name='" + widget->name + "'");
        if (owner == nullptr || owner->window == nullptr)
        {
            SetAsyncError("dui: widget_create: the window does not exist yet");
            return;
        }

        ui::Control* parent_control = widget->parent != nullptr ? widget->parent->control : nullptr;
        if (parent_control == nullptr && widget->parent == &owner->root)
            parent_control = owner->root.control; // the container the shim attached
        if (parent_control == nullptr)
            parent_control = owner->window->GetRoot(); // last resort: the toolkit's root
        Trace(std::string("  parent control=") + (parent_control != nullptr ? "ok" : "null"));

        auto* box = dynamic_cast<ui::Box*>(parent_control);
        if (box == nullptr)
        {
            SetAsyncError("dui: widget_create: parent is not a container control");
            return;
        }
        Trace(std::string("  box items before=") + std::to_string(box->GetItemCount()));

        ui::Control* control = InstantiateControl(owner->window, widget->class_name);
        if (control == nullptr)
        {
            SetAsyncError("dui: unknown control class '" + widget->class_name +
                          "' (add it to InstantiateControl)");
            return;
        }
        Trace(std::string("  CreateControl -> type='") + control->GetType() + "'");

        if (!widget->name.empty())
            control->SetName(widget->name);

        if (!box->AddItem(control))
        {
            SetAsyncError("dui: widget_create: AddItem failed for '" + widget->class_name + "'");
            return;
        }

        Trace(std::string("  AddItem ok, box items now=") + std::to_string(box->GetItemCount()));

        // Interactive controls must accept the pointer explicitly: the toolkit's defaults
        // differ per control class, and a control with an explicit rectangle should be
        // hit-testable regardless.
        control->SetMouseEnabled(true);
        widget->control = control;
    });

    return widget;
    DUI_SHIM_GUARD_END(nullptr)
}

void dui_shim_widget_destroy(dui_shim_widget* widget)
{
    DUI_SHIM_GUARD_BEGIN
    if (widget == nullptr)
        return;
    widget->destroyed = true;

    // Removal happens on the UI thread; the handle itself is freed there too so a
    // queued operation cannot touch a dangling pointer.
    OnUiThread([widget]() {
        ui::Control* control = widget->control;
        dui_shim_window* owner = widget->owner;
        if (control != nullptr)
        {
            if (auto* parent = control->GetParent())
                parent->RemoveItem(control);
            // Controls are deleted by the window, never by the shim.
            if (owner != nullptr && owner->window != nullptr)
                owner->window->RequestDeleteControl(control);
        }
        UnwrapWidget(widget);
        delete widget;
    });
    DUI_SHIM_GUARD_END()
}

void dui_shim_widget_set_bounds(dui_shim_widget* widget, double x_dip, double y_dip, double width_dip, double height_dip)
{
    DUI_SHIM_GUARD_BEGIN
    if (widget == nullptr)
        return;
    OnUiThread([widget, x_dip, y_dip, width_dip, height_dip]() {
        ui::Control* control = widget->control;
        if (control == nullptr)
        {
            SetAsyncError("dui: set_bounds: the widget has no control yet");
            return;
        }
        // A container's layout owns its children's rectangles, so MAUI's arranged
        // rectangle is expressed the way duilib expects for an absolutely positioned
        // control: float + fixed size + margin (Layout::GetFloatPos derives the child's
        // position from the margin, and ArrangeChildren only keeps an externally set
        // position for controls that are floating — see src/Layout/Layout.cpp:160).
        const auto x = static_cast<int32_t>(x_dip);
        const auto y = static_cast<int32_t>(y_dip);
        const auto w = static_cast<int32_t>(width_dip);
        const auto h = static_cast<int32_t>(height_dip);

        control->SetFloat(true);
        // bNeedDpiScale=false everywhere: the managed side always hands us DIP.
        control->SetFixedWidth(ui::UiFixedInt::MakeInt(w), true, false);
        control->SetFixedHeight(ui::UiFixedInt::MakeInt(h), true, false);
        control->SetMargin(ui::UiMargin(x, y, 0, 0), false);
        control->Invalidate();
        Trace("set_bounds " + widget->name + " -> " + std::to_string(x) + "," + std::to_string(y) +
              " " + std::to_string(w) + "x" + std::to_string(h));
    });
    DUI_SHIM_GUARD_END()
}

void dui_shim_widget_get_bounds(dui_shim_widget* widget, double* x_dip, double* y_dip, double* width_dip, double* height_dip)
{
    DUI_SHIM_GUARD_BEGIN
    if (widget == nullptr || widget->control == nullptr)
        return;
    const ui::UiRect rect = widget->control->GetPos();
    if (x_dip != nullptr) *x_dip = rect.left;
    if (y_dip != nullptr) *y_dip = rect.top;
    if (width_dip != nullptr) *width_dip = rect.right - rect.left;
    if (height_dip != nullptr) *height_dip = rect.bottom - rect.top;
    DUI_SHIM_GUARD_END()
}

void dui_shim_widget_set_attribute(dui_shim_widget* widget, const char* name_utf8, const char* value_utf8)
{
    DUI_SHIM_GUARD_BEGIN
    if (widget == nullptr)
        return;
    const std::string name = Utf8(name_utf8);
    const std::string value = Utf8(value_utf8);
    OnUiThread([widget, name, value]() {
        ui::Control* control = widget->control;
        if (control == nullptr)
        {
            SetAsyncError("dui: set_attribute: the widget has no control yet");
            return;
        }
        control->SetAttribute(name, value);
        control->Invalidate();
    });
    DUI_SHIM_GUARD_END()
}

void dui_shim_widget_set_text(dui_shim_widget* widget, const char* text_utf8)
{
    DUI_SHIM_GUARD_BEGIN
    if (widget == nullptr)
        return;
    const std::string text = Utf8(text_utf8);
    OnUiThread([widget, text]() {
        ui::Control* control = widget->control;
        if (control == nullptr)
        {
            SetAsyncError("dui: set_text: the widget has no control yet");
            return;
        }
        // Control has no SetText: text lives on the LabelOwner interface, so the
        // universal XML attribute is the type-agnostic route.
        control->SetAttribute("text", text);
        control->Invalidate();
    });
    DUI_SHIM_GUARD_END()
}

void dui_shim_widget_set_visible(dui_shim_widget* widget, int32_t visible)
{
    DUI_SHIM_GUARD_BEGIN
    if (widget == nullptr)
        return;
    OnUiThread([widget, visible]() {
        ui::Control* control = widget->control;
        if (control == nullptr)
            return;
        control->SetVisible(visible != 0);
        control->Invalidate();
    });
    DUI_SHIM_GUARD_END()
}

void dui_shim_widget_set_enabled(dui_shim_widget* widget, int32_t enabled)
{
    DUI_SHIM_GUARD_BEGIN
    if (widget == nullptr)
        return;
    OnUiThread([widget, enabled]() {
        ui::Control* control = widget->control;
        if (control == nullptr)
            return;
        control->SetEnabled(enabled != 0);
        control->Invalidate();
    });
    DUI_SHIM_GUARD_END()
}

int32_t dui_shim_widget_measure(dui_shim_widget* widget, double available_width_dip, double available_height_dip,
                               double* out_width_dip, double* out_height_dip)
{
    DUI_SHIM_GUARD_BEGIN
    // TODO(1): DUI measures during its layout pass; the public equivalent of
    // "measure this control for 300x∞" is not exposed by the headers. Until it is
    // wired, the managed side measures with MAUI's own IView measure and this
    // returns "unavailable" so callers can tell the difference.
    (void)widget;
    if (out_width_dip != nullptr) *out_width_dip = available_width_dip;
    if (out_height_dip != nullptr) *out_height_dip = available_height_dip;
    return 1;
    DUI_SHIM_GUARD_END(1)
}

void dui_shim_widget_set_event_handler(dui_shim_widget* widget, int32_t event_id, dui_shim_event_cb callback, void* user_data)
{
    DUI_SHIM_GUARD_BEGIN
    if (widget == nullptr)
        return;
    if (event_id != DUI_SHIM_EVENT_CLICK || callback == nullptr)
        return; // other events are not bridged yet

    dui_shim_widget* handle = widget;
    OnUiThread([handle, event_id, callback, user_data]() {
        ui::Control* control = handle->control;
        if (control == nullptr)
        {
            SetAsyncError("dui: set_event_handler: the widget has no control yet");
            return;
        }
        control->AttachClick([handle, event_id, callback, user_data](const ui::EventArgs&) {
            Trace("click: control '" + handle->name + "' -> managed callback");
            callback(user_data, handle, event_id);
            return true;
        });
    });
    DUI_SHIM_GUARD_END()
}

int32_t dui_shim_window_simulate_click(dui_shim_window* window, int32_t x, int32_t y)
{
    DUI_SHIM_GUARD_BEGIN
    if (window == nullptr || window->window == nullptr)
    {
        SetError("dui: null window");
        return 0;
    }

    OnUiThread([window, x, y]() {
        // WindowBase implements INativeWindow; these are the calls the Wayland (and other)
        // backends make when the compositor delivers a pointer click, so this exercises
        // the toolkit's hit testing and control-notification path.
        auto* owner = static_cast<ui::INativeWindow*>(window->window);
        if (owner == nullptr)
        {
            SetAsyncError("dui: simulate_click: window has no native owner");
            return;
        }

        const ui::UiPoint pt(x, y);
        bool handled = false;
        owner->OnNativeMouseMoveMsg(pt, 0, false, ui::NativeMsg(0, 0, 0), handled);
        owner->OnNativeMouseLButtonDownMsg(pt, 0, ui::NativeMsg(0, 0, 0), handled);
        owner->OnNativeMouseLButtonUpMsg(pt, 0, ui::NativeMsg(0, 0, 0), handled);
        Trace("simulate_click at " + std::to_string(x) + "," + std::to_string(y)
              + " (down handled=" + (handled ? "1" : "0") + ")");
    });

    return 1;
    DUI_SHIM_GUARD_END(0)
}

void dui_shim_window_set_shadow(dui_shim_window* window, int32_t attached)
{
    DUI_SHIM_GUARD_BEGIN
    if (window == nullptr)
    {
        SetError("dui: null window");
        return;
    }

    // The windows are created when the message loop starts, so the flag is read there.
    window->shadow_attached = attached != 0;
    DUI_SHIM_GUARD_END()
}

void dui_shim_window_set_size_handler(dui_shim_window* window, dui_shim_size_cb callback, void* user_data)
{
    DUI_SHIM_GUARD_BEGIN
    if (window == nullptr)
    {
        SetError("dui: null window");
        return;
    }

    OnUiThread([window, callback, user_data]() {
        window->size_cb = callback;
        window->size_user = user_data;
    });
    DUI_SHIM_GUARD_END()
}

int32_t dui_shim_widget_activate(dui_shim_widget* widget)
{
    DUI_SHIM_GUARD_BEGIN
    if (widget == nullptr)
    {
        SetError("dui: null widget");
        return 0;
    }

    OnUiThread([widget]() {
        if (widget->control == nullptr)
        {
            SetAsyncError("dui: activate: the widget has no control yet");
            return;
        }

        // Buttons turn Activate() into kEventClick, i.e. exactly what a real pointer click
        // produces (ButtonTemplate::Activate).
        if (auto* button = dynamic_cast<ui::Button*>(widget->control))
        {
            button->Activate(nullptr);
            return;
        }

        widget->control->SendEvent(ui::kEventClick);
    });

    return 1;
    DUI_SHIM_GUARD_END(0)
}

void dui_shim_widget_invalidate(dui_shim_widget* widget)
{
    DUI_SHIM_GUARD_BEGIN
    if (widget == nullptr)
        return;
    OnUiThread([widget]() {
        if (widget->control != nullptr)
            widget->control->Invalidate();
    });
    DUI_SHIM_GUARD_END()
}

} // extern "C"
