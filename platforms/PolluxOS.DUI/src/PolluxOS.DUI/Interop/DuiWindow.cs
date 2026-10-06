using System.Runtime.InteropServices;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.Platforms.PolluxOS.DUI.Interop;

/// <summary>
/// Managed wrapper around a DUI window (<c>dui_shim_window*</c>).
/// </summary>
/// <remarks>
/// Window lifetime is owned by the toolkit: DUI deletes the window when its final
/// message arrives, so <see cref="Dispose"/> closes (never destroys) it.
/// </remarks>
public sealed class DuiWindow : IDisposable
{
    nint _handle;
    DuiWidget? _root;
    DuiEventCallback? _closeCallback;
    GCHandle _closeHandle;
    bool _disposed;

    DuiWindow(nint handle)
    {
        _handle = handle;
    }

    public bool IsValid => !_disposed && _handle != 0;

    /// <summary>Raised when DUI reports the window closed.</summary>
    public event EventHandler? Closed;

    /// <summary>Creates a DUI window. <paramref name="skinFile"/> selects XML mode
    /// (resources/themes/&lt;theme&gt;/&lt;folder&gt;/&lt;file&gt;.xml); leave it null
    /// for a pure-code window.</summary>
    public static DuiWindow Create(
        string title,
        int width = 800,
        int height = 600,
        string? name = null,
        string? skinFolder = null,
        string? skinFile = null)
    {
        var handle = DuiNative.OrThrow(
            DuiNative.dui_shim_window_create(name, title, width, height, skinFolder, skinFile),
            "create DUI window");

        var window = new DuiWindow(handle);
        window.AttachCloseHandler();
        return window;
    }

    /// <summary>Root container of the window; every MAUI handler parents to it.</summary>
    public DuiWidget Root
    {
        get
        {
            ThrowIfDisposed();
            if (_root is null || !_root.IsValid)
            {
                var rootHandle = DuiNative.OrThrow(
                    DuiNative.dui_shim_window_root(_handle),
                    "get DUI window root (is the window created yet?)");
                _root = new DuiWidget(rootHandle);
            }
            return _root;
        }
    }

    public void Show() => DuiNative.dui_shim_window_show(_handle, 1);

    public void Hide() => DuiNative.dui_shim_window_show(_handle, 0);

    public void Close()
    {
        if (_disposed)
            return;
        DuiNative.dui_shim_window_close(_handle);
    }

    public void SetTitle(string title)
    {
        ThrowIfDisposed();
        DuiNative.dui_shim_window_set_title(_handle, title);
    }

    public void SetSize(int width, int height)
    {
        ThrowIfDisposed();
        DuiNative.dui_shim_window_set_size(_handle, width, height);
    }

    public Rect GetBounds()
    {
        ThrowIfDisposed();
        DuiNative.dui_shim_window_get_bounds(_handle, out var x, out var y, out var width, out var height);
        return new Rect(x, y, width, height);
    }

    /// <summary>Finds a widget by name (the DUI equivalent of AutomationId).</summary>
    public DuiWidget? FindWidget(string name)
    {
        ThrowIfDisposed();
        var handle = DuiNative.dui_shim_window_find_widget(_handle, name);
        return handle == 0 ? null : new DuiWidget(handle);
    }

    /// <summary>XML snapshot of the widget tree, for inspection/automation tooling.
    /// DUI ships no serializer, so the shim builds this from the control tree.</summary>
    public string? TryDumpXml()
    {
        ThrowIfDisposed();
        var pointer = DuiNative.dui_shim_window_dump_xml(_handle, out var length);
        if (pointer == 0)
            return null;

        try
        {
            return Marshal.PtrToStringUTF8(pointer, (int)length);
        }
        finally
        {
            DuiNative.dui_shim_string_free(pointer);
        }
    }

    /// <summary>Captures the window to a binary PPM (P6) file using DUI's own
    /// renderer capture. Useful where the windowing system cannot be grabbed
    /// (headless Xvfb, Wayland) — e.g. CI evidence.</summary>
    public bool TryCapturePpm(string path)
    {
        ThrowIfDisposed();
        return DuiNative.dui_shim_window_capture_ppm(_handle, path) == 0;
    }

    public void Dispose()
    {        if (_disposed)
            return;
        _disposed = true;

        if (_closeHandle.IsAllocated)
        {
            _closeHandle.Free();
            _closeHandle = default;
        }
        _closeCallback = null;

        if (_handle != 0)
        {
            DuiNative.dui_shim_window_close(_handle);
            _handle = 0;
        }
    }

    void AttachCloseHandler()
    {
        _closeCallback = (_, _, eventId) =>
        {
            if (eventId == (int)DuiEvent.Closed)
                Closed?.Invoke(this, EventArgs.Empty);
        };
        _closeHandle = GCHandle.Alloc(_closeCallback);
        DuiNative.dui_shim_window_set_event_handler(
            _handle,
            Marshal.GetFunctionPointerForDelegate(_closeCallback),
            GCHandle.ToIntPtr(_closeHandle));
    }

    void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
