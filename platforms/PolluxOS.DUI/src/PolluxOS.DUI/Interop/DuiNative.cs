using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Microsoft.Maui.Platforms.PolluxOS.DUI.Interop;

/// <summary>
/// P/Invoke surface of <c>dui_shim</c> — the C ABI bridge to the DUI toolkit
/// (see <c>platforms/PolluxOS.DUI/native/dui_shim/include/dui_shim.h</c>).
/// </summary>
/// <remarks>
/// DUI is a C++ library with no C entry points, so every call here goes through the
/// shim. All strings cross the boundary as UTF-8, <c>int</c> is <c>int32_t</c> and
/// <c>nint</c> is an opaque shim handle. Nothing in this type validates state: the
/// wrapper types (<see cref="DuiWidget"/>, <see cref="DuiWindow"/>) do that.
/// </remarks>
internal static partial class DuiNative
{
    internal const string LibraryName = "dui_shim";

    // ------------------------------------------------------------- runtime

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int dui_shim_startup(string? resourceRoot, string? locale);

    [LibraryImport(LibraryName)]
    internal static partial void dui_shim_shutdown();

    [LibraryImport(LibraryName)]
    internal static partial int dui_shim_run();

    [LibraryImport(LibraryName)]
    internal static partial void dui_shim_post_quit(int exitCode);

    [LibraryImport(LibraryName)]
    internal static partial nint dui_shim_last_error();

    [LibraryImport(LibraryName)]
    internal static partial nint dui_shim_version();

    [LibraryImport(LibraryName)]
    internal static partial void dui_shim_set_theme(int dark);

    /// <summary>Installs the idle handler: DUI calls it on its UI thread whenever the
    /// message queue is empty (the only place managed code can run with the toolkit's
    /// thread affinity).</summary>
    [LibraryImport(LibraryName)]
    internal static partial void dui_shim_set_idle_handler(nint callback, nint userData);

    // -------------------------------------------------------------- window

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint dui_shim_window_create(
        string? name, string? title, int widthDip, int heightDip, string? skinFolder, string? skinFile);

    [LibraryImport(LibraryName)]
    internal static partial void dui_shim_window_show(nint window, int show);

    [LibraryImport(LibraryName)]
    internal static partial void dui_shim_window_close(nint window);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void dui_shim_window_set_title(nint window, string? title);

    [LibraryImport(LibraryName)]
    internal static partial void dui_shim_window_set_size(nint window, int widthDip, int heightDip);

    /// <summary>Client area of the window (0 when the toolkit cannot report it yet).</summary>
    [LibraryImport(LibraryName)]
    internal static partial int dui_shim_window_get_client_size(nint window, out int width, out int height);

    [LibraryImport(LibraryName)]
    internal static partial void dui_shim_window_get_bounds(nint window, out double x, out double y, out double width, out double height);

    /// <summary>Feeds a left click at (x, y) in window coordinates into the toolkit
    /// (hit testing included). Returns 1 when the click was queued.</summary>
    [LibraryImport(LibraryName)]
    internal static partial int dui_shim_window_simulate_click(nint window, int x, int y);

    /// <summary>Subscribes to client-area changes (callback runs on the toolkit's UI
    /// thread); pass zero to unsubscribe.</summary>
    [LibraryImport(LibraryName)]
    internal static partial void dui_shim_window_set_size_handler(nint window, nint callback, nint userData);

    /// <summary>Attaches (1) or removes (0) the toolkit's window shadow/decoration.</summary>
    [LibraryImport(LibraryName)]
    internal static partial void dui_shim_window_set_shadow(nint window, int attached);

    [LibraryImport(LibraryName)]
    internal static partial nint dui_shim_window_root(nint window);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint dui_shim_window_find_widget(nint window, string name);

    [LibraryImport(LibraryName)]
    internal static partial void dui_shim_window_set_event_handler(nint window, nint callback, nint userData);

    [LibraryImport(LibraryName)]
    internal static partial nint dui_shim_window_dump_xml(nint window, out nuint length);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int dui_shim_window_capture_ppm(nint window, string path);

    [LibraryImport(LibraryName)]
    internal static partial void dui_shim_string_free(nint text);

    // -------------------------------------------------------------- widget

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint dui_shim_widget_create(nint parent, string className, string? name);

    [LibraryImport(LibraryName)]
    internal static partial void dui_shim_widget_destroy(nint widget);

    [LibraryImport(LibraryName)]
    internal static partial void dui_shim_widget_set_bounds(nint widget, double x, double y, double width, double height);

    [LibraryImport(LibraryName)]
    internal static partial void dui_shim_widget_get_bounds(nint widget, out double x, out double y, out double width, out double height);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void dui_shim_widget_set_attribute(nint widget, string name, string? value);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void dui_shim_widget_set_text(nint widget, string? text);

    [LibraryImport(LibraryName)]
    internal static partial void dui_shim_widget_set_visible(nint widget, int visible);

    [LibraryImport(LibraryName)]
    internal static partial void dui_shim_widget_set_enabled(nint widget, int enabled);

    [LibraryImport(LibraryName)]
    internal static partial int dui_shim_widget_measure(nint widget, double availableWidth, double availableHeight, out double width, out double height);

    [LibraryImport(LibraryName)]
    internal static partial void dui_shim_widget_set_event_handler(nint widget, int eventId, nint callback, nint userData);

    /// <summary>Fires the control's click notification the way a real pointer click does
    /// (DUI's <c>Button::Activate</c>). Returns 1 when the request was queued.</summary>
    [LibraryImport(LibraryName)]
    internal static partial int dui_shim_widget_activate(nint widget);

    [LibraryImport(LibraryName)]
    internal static partial void dui_shim_widget_invalidate(nint widget);

    // ------------------------------------------------------------- helpers

    /// <summary>Reads and clears the shim's thread-local error string.</summary>
    internal static string? TakeLastError()
    {
        var pointer = dui_shim_last_error();
        return pointer == 0 ? null : Marshal.PtrToStringUTF8(pointer);
    }

    internal static string Version()
    {
        var pointer = dui_shim_version();
        return pointer == 0 ? "unknown" : Marshal.PtrToStringUTF8(pointer) ?? "unknown";
    }

    /// <summary>Throws when <paramref name="handle"/> is zero, surfacing the shim's message.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static nint OrThrow(nint handle, string operation)
    {
        if (handle != 0)
            return handle;

        var error = TakeLastError();
        throw new InvalidOperationException(
            error is { Length: > 0 } ? $"{operation}: {error}" : $"{operation}: the DUI bridge returned no handle.");
    }
}
