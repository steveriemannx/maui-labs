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

#if defined(__APPLE__)
// Platform message loops are not part of the dui.h umbrella: each one is guarded
// by its DUI_BUILD_FOR_* macro (defined in dui_config.h), so include it explicitly.
#include "dui/Core/MessageLoop_MacOS.h"
#endif

#include <deque>
#include <functional>
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

void SetError(const std::string& message)
{
    g_last_error = message;
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
    ui::Control* control = nullptr;
    dui_shim_window* owner = nullptr;
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

    CommandQueue& Commands() { return m_commands; }

    void AddWindow(dui_shim_window* handle) { m_windows.push_back(handle); }

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
};

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
            SetError("dui: WindowImplBase::CreateWnd failed for window '" + handle->name + "'");
            // Do not delete: the toolkit owns window lifetime.
            continue;
        }

        window->PostQuitMsgWhenClosed(true);
        handle->window = window;

        if (handle->show_requested)
        {
            window->InvalidateAll();
            window->UpdateWindow();
            window->ShowWindow(ui::kSW_SHOW_NORMAL);
            handle->shown = true;
        }
    }
}

void DuiHost::OnCleanup()
{
    for (dui_shim_window* handle : m_windows)
        handle->window = nullptr; // closed by the toolkit on final message
    m_windows.clear();
    ui::GlobalManager::Instance().Shutdown();
}

void DuiHost::OnMessageLoopIdle()
{
    m_commands.Drain();
}

DuiHost& Host()
{
    return DuiHost::Instance();
}

void OnUiThread(std::function<void()> action)
{
    Host().Commands().Post(std::move(action));
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

void AppendXmlAttribute(std::string& out, const char* name, const std::string& value)
{
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
#if defined(__APPLE__)
    // DUI's per-window quit is Window::PostQuitMsgWhenClosed; a cross-thread quit
    // goes through the platform loop (MessageLoop_MacOS.h:83).
    ui::MessageLoop_MacOS::PostQuitMsg(exit_code);
#else
    (void)exit_code;
    SetError("dui: dui_shim_post_quit is only wired for the macOS message loop");
#endif
    DUI_SHIM_GUARD_END()
}

const char* dui_shim_last_error(void)
{
    return g_last_error.c_str();
}

const char* dui_shim_version(void)
{
    return kVersion;
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
        window->window->SetWindowSize(width_dip, height_dip);
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
    ui::Box* root = RootBox(window);
    if (root == nullptr)
        return nullptr;
    window->root.control = root;
    window->root.owner = window;
    return &window->root;
    DUI_SHIM_GUARD_END(nullptr)
}

dui_shim_widget* dui_shim_window_find_widget(dui_shim_window* window, const char* name_utf8)
{
    DUI_SHIM_GUARD_BEGIN
    if (window == nullptr || window->window == nullptr)
        return nullptr;
    return WrapWidget(window, window->window->FindControl(Utf8(name_utf8)));
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
    if (window == nullptr || window->window == nullptr)
    {
        SetError("dui: window is not created yet");
        return nullptr;
    }

    std::string xml = "<window";
    AppendXmlAttribute(xml, "name", window->name);
    AppendXmlAttribute(xml, "title", window->title);
    xml += ">\n";
    WalkControl(window->window->GetRoot(), 1, xml);
    xml += "</window>\n";

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

// ---------------------------------------------------------------------------
// Widgets
// ---------------------------------------------------------------------------

dui_shim_widget* dui_shim_widget_create(dui_shim_widget* parent, const char* class_name_utf8, const char* name_utf8)
{
    DUI_SHIM_GUARD_BEGIN
    ClearError();
    if (parent == nullptr || parent->control == nullptr || parent->owner == nullptr || parent->owner->window == nullptr)
    {
        SetError("dui: widget_create needs a parent inside a created window");
        return nullptr;
    }

    const std::string class_name = Utf8(class_name_utf8);
    const std::string name = Utf8(name_utf8);

    // Window::CreateControl instantiates a control by its DUI class name
    // (the DUI_CTR_* values in include/dui/dui_defs.h).
    ui::Control* control = parent->owner->window->CreateControl(class_name);
    if (control == nullptr)
    {
        SetError("dui: could not create control of class '" + class_name + "'");
        return nullptr;
    }

    if (!name.empty())
        control->SetName(name);

    auto* box = dynamic_cast<ui::Box*>(parent->control);
    if (box == nullptr || !box->AddItem(control))
    {
        SetError("dui: parent control is not a container; cannot add '" + class_name + "'");
        return nullptr;
    }

    return WrapWidget(parent->owner, control);
    DUI_SHIM_GUARD_END(nullptr)
}

void dui_shim_widget_destroy(dui_shim_widget* widget)
{
    DUI_SHIM_GUARD_BEGIN
    if (widget == nullptr)
        return;
    ui::Control* control = widget->control;
    dui_shim_window* owner = widget->owner;
    UnwrapWidget(widget);
    widget->control = nullptr;
    delete widget;

    if (control != nullptr && owner != nullptr)
    {
        OnUiThread([control, owner]() {
            if (auto* parent = control->GetParent())
                parent->RemoveItem(control);
            // Controls are deleted by the window, never by the shim.
            if (owner->window != nullptr)
                owner->window->RequestDeleteControl(control);
        });
    }
    DUI_SHIM_GUARD_END()
}

void dui_shim_widget_set_bounds(dui_shim_widget* widget, double x_dip, double y_dip, double width_dip, double height_dip)
{
    DUI_SHIM_GUARD_BEGIN
    if (widget == nullptr || widget->control == nullptr)
        return;
    OnUiThread([widget, x_dip, y_dip, width_dip, height_dip]() {
        // DUI positions a control with an absolute rect inside its parent, and its
        // fixed-size pair keeps the layout from overriding the arranged size.
        widget->control->SetPos(ui::UiRect(static_cast<int32_t>(x_dip), static_cast<int32_t>(y_dip),
                                           static_cast<int32_t>(x_dip + width_dip),
                                           static_cast<int32_t>(y_dip + height_dip)));
        widget->control->SetFixedWidth(ui::UiFixedInt::MakeInt(static_cast<int32_t>(width_dip)), true, false);
        widget->control->SetFixedHeight(ui::UiFixedInt::MakeInt(static_cast<int32_t>(height_dip)), true, false);
        widget->control->Invalidate();
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
    if (widget == nullptr || widget->control == nullptr)
        return;
    const std::string name = Utf8(name_utf8);
    const std::string value = Utf8(value_utf8);
    OnUiThread([widget, name, value]() {
        widget->control->SetAttribute(name, value);
        widget->control->Invalidate();
    });
    DUI_SHIM_GUARD_END()
}

void dui_shim_widget_set_text(dui_shim_widget* widget, const char* text_utf8)
{
    DUI_SHIM_GUARD_BEGIN
    if (widget == nullptr || widget->control == nullptr)
        return;
    const std::string text = Utf8(text_utf8);
    OnUiThread([widget, text]() {
        // Control has no SetText: text lives on the LabelOwner interface, so the
        // universal XML attribute is the type-agnostic route.
        widget->control->SetAttribute("text", text);
        widget->control->Invalidate();
    });
    DUI_SHIM_GUARD_END()
}

void dui_shim_widget_set_visible(dui_shim_widget* widget, int32_t visible)
{
    DUI_SHIM_GUARD_BEGIN
    if (widget == nullptr || widget->control == nullptr)
        return;
    OnUiThread([widget, visible]() {
        widget->control->SetVisible(visible != 0);
        widget->control->Invalidate();
    });
    DUI_SHIM_GUARD_END()
}

void dui_shim_widget_set_enabled(dui_shim_widget* widget, int32_t enabled)
{
    DUI_SHIM_GUARD_BEGIN
    if (widget == nullptr || widget->control == nullptr)
        return;
    OnUiThread([widget, enabled]() {
        widget->control->SetEnabled(enabled != 0);
        widget->control->Invalidate();
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
    if (event_id != DUI_SHIM_EVENT_CLICK || callback == nullptr || widget->control == nullptr)
        return; // other events are not bridged yet

    ui::Control* control = widget->control;
    OnUiThread([control, callback, user_data]() {
        control->AttachClick([callback, user_data](const ui::EventArgs&) {
            callback(user_data, nullptr, DUI_SHIM_EVENT_CLICK);
            return true;
        });
    });
    DUI_SHIM_GUARD_END()
}

void dui_shim_widget_invalidate(dui_shim_widget* widget)
{
    DUI_SHIM_GUARD_BEGIN
    if (widget == nullptr || widget->control == nullptr)
        return;
    OnUiThread([widget]() {
        widget->control->Invalidate();
    });
    DUI_SHIM_GUARD_END()
}

} // extern "C"
