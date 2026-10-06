using System.Runtime.InteropServices;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.Platforms.PolluxOS.DUI.Interop;

/// <summary>DUI event ids; must match <c>dui_shim_event</c> in dui_shim.h.</summary>
internal enum DuiEvent
{
    Click = 1,
    Selected = 2,
    TextChanged = 3,
    Closed = 4,
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void DuiEventCallback(nint userData, nint widget, int eventId);

/// <summary>DUI control class names understood by the bridge.</summary>
internal static class DuiControlClass
{
    public const string Box = "Box";
    public const string VBox = "VBox";
    public const string HBox = "HBox";
    public const string Label = "Label";
    public const string Button = "Button";
    public const string RichEdit = "RichEdit";
}

/// <summary>
/// Managed wrapper around a DUI control (<c>dui_shim_widget*</c>).
/// </summary>
/// <remarks>
/// The wrapper does not own the toolkit: <see cref="Dispose"/> asks the shim to
/// detach and destroy the control. Handles are only valid on the DUI UI thread;
/// the shim queues calls made from other threads.
/// </remarks>
public class DuiWidget : IDisposable
{
    DuiEventCallback? _clickCallback;
    GCHandle _clickHandle;
    bool _disposed;

    internal DuiWidget(nint handle)
    {
        Handle = handle;
    }

    internal nint Handle { get; }

    public bool IsValid => !_disposed && Handle != 0;

    /// <summary>Creates a child control of <paramref name="parent"/>.</summary>
    public static DuiWidget Create(DuiWidget parent, string controlClass, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(parent);
        var handle = DuiNative.OrThrow(
            DuiNative.dui_shim_widget_create(parent.Handle, controlClass, name),
            $"create DUI control '{controlClass}'");
        return new DuiWidget(handle);
    }

    public void SetBounds(Rect bounds)
    {
        ThrowIfDisposed();
        DuiNative.dui_shim_widget_set_bounds(Handle, bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    public Rect GetBounds()
    {
        ThrowIfDisposed();
        DuiNative.dui_shim_widget_get_bounds(Handle, out var x, out var y, out var width, out var height);
        return new Rect(x, y, width, height);
    }

    public void SetAttribute(string name, string? value)
    {
        ThrowIfDisposed();
        DuiNative.dui_shim_widget_set_attribute(Handle, name, value);
    }

    public void SetText(string? text)
    {
        ThrowIfDisposed();
        DuiNative.dui_shim_widget_set_text(Handle, text);
    }

    public void SetVisible(bool visible)
    {
        ThrowIfDisposed();
        DuiNative.dui_shim_widget_set_visible(Handle, visible ? 1 : 0);
    }

    public void SetEnabled(bool enabled)
    {
        ThrowIfDisposed();
        DuiNative.dui_shim_widget_set_enabled(Handle, enabled ? 1 : 0);
    }

    /// <summary>Asks DUI for a desired size. Returns <c>false</c> when the bridge
    /// cannot measure (this revision), so callers fall back to MAUI measurement.</summary>
    public bool TryMeasure(double availableWidth, double availableHeight, out Size size)
    {
        ThrowIfDisposed();
        var result = DuiNative.dui_shim_widget_measure(Handle, availableWidth, availableHeight, out var width, out var height);
        size = result == 0 ? new Size(width, height) : Size.Zero;
        return result == 0;
    }

    public void Invalidate()
    {
        ThrowIfDisposed();
        DuiNative.dui_shim_widget_invalidate(Handle);
    }

    /// <summary>Attaches (or clears) the click handler for this control.</summary>
    /// <summary>
    /// Activates the control (a button click) exactly as the toolkit would on a pointer
    /// click, so a host or test can drive the UI without input devices.
    /// </summary>
    public bool Activate()
    {
        ThrowIfDisposed();
        return DuiNative.dui_shim_widget_activate(Handle) != 0;
    }

    public void SetClickHandler(Action? handler)
    {
        ThrowIfDisposed();

        if (_clickHandle.IsAllocated)
        {
            _clickHandle.Free();
            _clickHandle = default;
        }

        if (handler is null)
        {
            _clickCallback = null;
            DuiNative.dui_shim_widget_set_event_handler(Handle, (int)DuiEvent.Click, 0, 0);
            return;
        }

        _clickCallback = (_, _, eventId) =>
        {
            if (eventId == (int)DuiEvent.Click)
                handler();
        };
        _clickHandle = GCHandle.Alloc(_clickCallback);
        DuiNative.dui_shim_widget_set_event_handler(
            Handle,
            (int)DuiEvent.Click,
            Marshal.GetFunctionPointerForDelegate(_clickCallback),
            GCHandle.ToIntPtr(_clickHandle));
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_clickHandle.IsAllocated)
        {
            _clickHandle.Free();
            _clickHandle = default;
        }
        _clickCallback = null;

        if (Handle != 0)
            DuiNative.dui_shim_widget_destroy(Handle);
    }

    void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}

/// <summary>A DUI container control (<c>Box</c>/<c>VBox</c>/<c>HBox</c>).</summary>
public sealed class DuiBox : DuiWidget
{
    internal DuiBox(nint handle) : base(handle)
    {
    }

    public static new DuiBox Create(DuiWidget parent, string controlClass = DuiControlClass.Box, string? name = null)
    {
        var handle = DuiNative.OrThrow(
            DuiNative.dui_shim_widget_create(parent.Handle, controlClass, name),
            $"create DUI container '{controlClass}'");
        return new DuiBox(handle);
    }
}

/// <summary>A DUI <c>Label</c> control.</summary>
public sealed class DuiLabel : DuiWidget
{
    internal DuiLabel(nint handle) : base(handle)
    {
    }
}

/// <summary>A DUI <c>Button</c> control.</summary>
public sealed class DuiButton : DuiWidget
{
    internal DuiButton(nint handle) : base(handle)
    {
    }
}
