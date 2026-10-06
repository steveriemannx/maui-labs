/*
 * dui_shim.h — stable C ABI that lets managed (.NET) code drive the DUI toolkit.
 *
 * DUI (https://github.com/rhett-lee/nim_duilib lineage) is a C++ library: its public
 * headers expose C++ classes, templates and STL types, and it has no `extern "C"`
 * entry points. A .NET backend therefore cannot P/Invoke it directly; this shim is
 * the ABI boundary.
 *
 * Rules for this header:
 *   - C only (no C++ types), so it can be consumed by P/Invoke.
 *   - UTF-8 for every string; sizes in device-independent pixels (DIP).
 *   - Handles are opaque pointers owned by the shim; the caller never frees them.
 *   - Every entry point is thread-affine to the DUI UI thread unless documented
 *     otherwise; calls from other threads are queued and executed on the UI thread.
 */

#ifndef POLLUXOS_DUI_SHIM_H_
#define POLLUXOS_DUI_SHIM_H_

#include <stddef.h>
#include <stdint.h>

#if defined(_WIN32)
#  define DUI_SHIM_EXPORT __declspec(dllexport)
#else
#  define DUI_SHIM_EXPORT __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct dui_shim_runtime dui_shim_runtime;
typedef struct dui_shim_window dui_shim_window;
typedef struct dui_shim_widget dui_shim_widget;

/** Event ids delivered to a widget callback. */
enum dui_shim_event {
    DUI_SHIM_EVENT_CLICK = 1,
    DUI_SHIM_EVENT_SELECTED = 2,
    DUI_SHIM_EVENT_TEXT_CHANGED = 3,
    DUI_SHIM_EVENT_CLOSED = 4
};

typedef void (*dui_shim_event_cb)(void* user_data, dui_shim_widget* widget, int32_t event_id);

/** Idle callback: invoked on the DUI UI thread whenever the toolkit's message queue is
 *  empty. This is the only place a host can run managed code with the toolkit's thread
 *  affinity, which is what a MAUI dispatcher needs (see dui_shim_set_idle_handler). */
typedef void (*dui_shim_idle_cb)(void* user_data);

/** Window client-area callback: invoked on the toolkit's UI thread whenever the window
 *  size changes (user resize, compositor configure). */
typedef void (*dui_shim_size_cb)(void* user_data, int32_t width, int32_t height);

/* ------------------------------------------------------------------ runtime */

/** Start the toolkit: loads resources from `resource_root_utf8` (directory holding
 *  `themes/`, `lang/`, ...). Returns 0 on success, non-zero on failure. Must be
 *  called on the thread that will own the UI (the "UI thread"). */
DUI_SHIM_EXPORT int32_t dui_shim_startup(const char* resource_root_utf8, const char* locale_utf8);

/** Tear the toolkit down. Safe to call when not started. */
DUI_SHIM_EXPORT void dui_shim_shutdown(void);

/** Run the message loop until a window requests quit. Must run on the UI thread;
 *  drives queued cross-thread work and buffers the MAUI frame rectangle. */
DUI_SHIM_EXPORT int32_t dui_shim_run(void);

/** Ask the message loop to return (usable from any thread). */
DUI_SHIM_EXPORT void dui_shim_post_quit(int32_t exit_code);

/** Last error as UTF-8; valid until the next shim call on the same thread. */
DUI_SHIM_EXPORT const char* dui_shim_last_error(void);

/** Shim ABI/semantic version, e.g. "0.1.0". */
DUI_SHIM_EXPORT const char* dui_shim_version(void);

/** Install (or clear, with callback == NULL) the idle handler. The toolkit calls it on
 *  the UI thread while the message loop runs; a host drains its own work queue there. */
DUI_SHIM_EXPORT void dui_shim_set_idle_handler(dui_shim_idle_cb callback, void* user_data);

/** Application-wide theme (light/dark). DUI applies theme attributes itself;
 *  this is the MAUI-side hint used before a window exists. */
DUI_SHIM_EXPORT void dui_shim_set_theme(int32_t dark);

/* ------------------------------------------------------------------- window */

/** Create a DUI window. `name` identifies the window for FindControl lookups;
 *  `skin_folder`/`skin_file` may be NULL/empty for a pure-code window. */
DUI_SHIM_EXPORT dui_shim_window* dui_shim_window_create(
    const char* name_utf8,
    const char* title_utf8,
    int32_t width_dip,
    int32_t height_dip,
    const char* skin_folder_utf8,
    const char* skin_file_utf8);

/** Show the window (normally called from the queued UI thread; if called before
 *  the UI thread exists, the show is buffered and replayed after the window is
 *  created). */
DUI_SHIM_EXPORT void dui_shim_window_show(dui_shim_window* window, int32_t show);
DUI_SHIM_EXPORT void dui_shim_window_close(dui_shim_window* window);
DUI_SHIM_EXPORT void dui_shim_window_set_title(dui_shim_window* window, const char* title_utf8);
DUI_SHIM_EXPORT void dui_shim_window_set_size(dui_shim_window* window, int32_t width_dip, int32_t height_dip);

/** Reports the window's client area. Hosts lay their content out to this, because some
 *  backends (Wayland) size the surface themselves and ignore a requested resize.
 *  @return 1 on success, 0 if the size is not known yet (e.g. before dui_shim_run). */
DUI_SHIM_EXPORT int32_t dui_shim_window_get_client_size(dui_shim_window* window, int32_t* width, int32_t* height);

/** Writes the window's client area to a PNG. On macOS this renders the view hierarchy
 *  in-process (so it works without screen-recording permission); elsewhere it reports 0. */
DUI_SHIM_EXPORT int32_t dui_shim_window_capture_png(dui_shim_window* window, const char* path);

/** Subscribes to client-area changes. The callback runs on the UI thread; call with a
 *  NULL callback to unsubscribe. */
DUI_SHIM_EXPORT void dui_shim_window_set_size_handler(dui_shim_window* window, dui_shim_size_cb callback, void* user_data);

/** Attaches or removes the toolkit's window shadow/decoration.
 *
 *  Keep it ON where the platform gives the window system chrome from it (macOS: it is what
 *  turns the window into a titled, closable, resizable AppKit window) and OFF where the
 *  decoration would only inset the host's content (Wayland: the host then fills the whole
 *  window instead of showing a frame around a smaller surface). Must be called before
 *  dui_shim_run(). */
DUI_SHIM_EXPORT void dui_shim_window_set_shadow(dui_shim_window* window, int32_t attached);
DUI_SHIM_EXPORT void dui_shim_window_get_bounds(dui_shim_window* window, double* x_dip, double* y_dip, double* width_dip, double* height_dip);

/** Feeds a left-button click at (x, y) in window coordinates into the toolkit, i.e. the
 *  exact path a compositor-delivered pointer click takes (hit testing included). Used to
 *  verify input handling without a pointer. Returns 1 when the click was queued. */
DUI_SHIM_EXPORT int32_t dui_shim_window_simulate_click(dui_shim_window* window, int32_t x, int32_t y);

/** Root container widget of the window (owned by the window; do not destroy).
 *  Valid before the message loop starts: the handle exists immediately and resolves
 *  to the real container once the toolkit has created the window, so a host can
 *  build its widget tree up front and then call dui_shim_run(). */
DUI_SHIM_EXPORT dui_shim_widget* dui_shim_window_root(dui_shim_window* window);

/** Find a widget by name (`name` attribute / MAUI AutomationId). NULL when absent
 *  or when the window does not exist yet. Runs on the UI thread; do not call from
 *  the UI thread itself. */
DUI_SHIM_EXPORT dui_shim_widget* dui_shim_window_find_widget(dui_shim_window* window, const char* name_utf8);

/** Window callback sink: receives DUI_SHIM_EVENT_CLOSED etc. */
DUI_SHIM_EXPORT void dui_shim_window_set_event_handler(dui_shim_window* window, dui_shim_event_cb callback, void* user_data);

/** XML snapshot of the window's widget tree (UTF-8, `*out_len` receives the length). *  Intended for automation/inspection (DevFlow-style tooling), not for rendering.
 *  The walk runs on the UI thread; do not call this from the UI thread itself.
 *  Returns NULL when the tree cannot be serialized. Caller frees via
 *  dui_shim_string_free. */
DUI_SHIM_EXPORT char* dui_shim_window_dump_xml(dui_shim_window* window, size_t* out_len);

/** Free a string returned by this ABI. */
DUI_SHIM_EXPORT void dui_shim_string_free(char* text);

/** Capture the window into a binary PPM (P6) file at `path_utf8` — 24-bit RGB,
 *  alpha flattened over black. DUI renders through Skia, so this is the toolkit's
 *  own screen capture (ScreenCapture::CaptureBitmap) rather than a desktop grab.
 *  Runs on the UI thread; do not call from the UI thread itself.
 *  Returns 0 on success. */
DUI_SHIM_EXPORT int32_t dui_shim_window_capture_ppm(dui_shim_window* window, const char* path_utf8);

/* ------------------------------------------------------------------- widget */

/** Create a widget of `class_name_utf8` (DUI control class, e.g. "Label", "Button",
 *  "Box", "VBox", "HBox") as a child of `parent`. `name_utf8` may be NULL; when
 *  provided it becomes the widget's name so FindWidget/AutomationId work.
 *
 *  The returned handle is valid immediately; the DUI control itself is created on
 *  the UI thread (queued commands run FIFO, so a parent always resolves before its
 *  children). A NULL return means the request could not even be queued. */
DUI_SHIM_EXPORT dui_shim_widget* dui_shim_widget_create(dui_shim_widget* parent, const char* class_name_utf8, const char* name_utf8);

/** Detach and destroy a widget created by this ABI. */
DUI_SHIM_EXPORT void dui_shim_widget_destroy(dui_shim_widget* widget);

/** Move/resize inside the parent (DIP). */
DUI_SHIM_EXPORT void dui_shim_widget_set_bounds(dui_shim_widget* widget, double x_dip, double y_dip, double width_dip, double height_dip);
DUI_SHIM_EXPORT void dui_shim_widget_get_bounds(dui_shim_widget* widget, double* x_dip, double* y_dip, double* width_dip, double* height_dip);

/** Set a DUI XML attribute (the toolkit's universal setter: "text", "font",
 *  "textcolor", "bkcolor", "class", ...). */
DUI_SHIM_EXPORT void dui_shim_widget_set_attribute(dui_shim_widget* widget, const char* name_utf8, const char* value_utf8);

DUI_SHIM_EXPORT void dui_shim_widget_set_text(dui_shim_widget* widget, const char* text_utf8);
DUI_SHIM_EXPORT void dui_shim_widget_set_visible(dui_shim_widget* widget, int32_t visible);
DUI_SHIM_EXPORT void dui_shim_widget_set_enabled(dui_shim_widget* widget, int32_t enabled);

/** Ask the toolkit for the widget's desired size. Returns 0 when the toolkit
 *  produced a measurement, non-zero when it could not. */
DUI_SHIM_EXPORT int32_t dui_shim_widget_measure(dui_shim_widget* widget, double available_width_dip, double available_height_dip, double* out_width_dip, double* out_height_dip);

/** Click/text events for this widget. Replaces any previous handler for `event_id`. */
DUI_SHIM_EXPORT void dui_shim_widget_set_event_handler(dui_shim_widget* widget, int32_t event_id, dui_shim_event_cb callback, void* user_data);

/** Fires the control's click notification as if the user had activated it (DUI's
 *  Button::Activate). Returns 1 when the request was queued. Used by tests and by hosts
 *  that need to drive the UI without a pointer. */
DUI_SHIM_EXPORT int32_t dui_shim_widget_activate(dui_shim_widget* widget);

/** Force a repaint of the widget (and its ancestors as needed). */
DUI_SHIM_EXPORT void dui_shim_widget_invalidate(dui_shim_widget* widget);

#ifdef __cplusplus
} /* extern "C" */
#endif

#endif /* POLLUXOS_DUI_SHIM_H_ */
