using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.PolluxOS.DUI.Interop;
using Microsoft.Maui.Platforms.PolluxOS.DUI.Platform;

namespace Microsoft.Maui.Platforms.PolluxOS.DUI.Handlers;

/// <summary>
/// MAUI <see cref="IWindow"/> → <see cref="DuiWindow"/>.
/// </summary>
/// <remarks>
/// The DUI window is created here (not by an OS lifecycle callback), so a host
/// drives startup explicitly: build the MAUI app, open the window, then run the
/// DUI message loop.
/// </remarks>
public partial class WindowHandler : ElementHandler<IWindow, DuiWindow>
{
    static int s_windowCounter;

    public static readonly IPropertyMapper<IWindow, WindowHandler> Mapper =
        new PropertyMapper<IWindow, WindowHandler>(ElementMapper)
        {
            [nameof(IWindow.Title)] = MapTitle,
            [nameof(IWindow.Width)] = MapSize,
            [nameof(IWindow.Height)] = MapSize,
        };

    public WindowHandler() : base(Mapper)
    {
    }

    protected override DuiWindow CreatePlatformElement()
    {
        var window = VirtualView ?? throw new InvalidOperationException("WindowHandler has no virtual view.");

        var width = window.Width > 0 ? (int)Math.Round(window.Width) : 800;
        var height = window.Height > 0 ? (int)Math.Round(window.Height) : 600;

        var platformWindow = DuiWindow.Create(
            title: window.Title ?? "PolluxOS.DUI",
            width: width,
            height: height,
            name: $"window-{Interlocked.Increment(ref s_windowCounter)}");

        if (MauiContext?.Services.GetService<DuiPlatformContext>() is { } platformContext)
        {
            platformContext.SetWindow(platformWindow);
            platformWindow.Closed += (_, _) =>
            {
                if (platformContext.Window == platformWindow)
                    platformContext.Clear();
            };
        }

        return platformWindow;
    }

    protected override void ConnectHandler(DuiWindow platformView)
    {
        base.ConnectHandler(platformView);

        if (MauiContext is null || VirtualView?.Content is not IView content)
            return;

        // Realizing the page parents its DUI controls to the window root through
        // DuiPlatformContext; the root is sized to the window and the content is
        // measured/arranged so every handler below it receives a real rectangle.
        content.ToHandler(MauiContext);

        var width = (int)Math.Round(VirtualView.Width > 0 ? VirtualView.Width : 800);
        var height = (int)Math.Round(VirtualView.Height > 0 ? VirtualView.Height : 600);
        ApplyClientLayout(platformView, width, height);

        // Immediate re-layout on the toolkit's size notification, plus the poll below as a
        // safety net for backends that do not raise it.
        platformView.EnableClientSizeNotifications();
        platformView.ClientSizeChanged += OnClientSizeChanged;
        WatchClientSize(platformView);
    }

    protected override void DisconnectHandler(DuiWindow platformView)
    {
        platformView.ClientSizeChanged -= OnClientSizeChanged;
        base.DisconnectHandler(platformView);
    }

    int _lastAppliedWidth = -1;
    int _lastAppliedHeight = -1;
    long _lastClientLayoutTicks;

    /// <summary>
    /// Re-lays out when the toolkit's client area really changed.
    /// </summary>
    /// <remarks>
    /// The size is always re-read from the toolkit rather than taken from the notification:
    /// resizing our own root container raises the toolkit's size notification too, and
    /// feeding that value back in made every pass subtract the platform inset again (the
    /// layout shrank a little on each event, which is what made resizing flicker).
    /// </remarks>
    void ApplyClientAreaIfChanged(DuiWindow platformView, bool throttle)
    {
        if (!platformView.TryGetClientSize(out var size))
            return;

        var width = (int)Math.Round(size.Width);
        var height = (int)Math.Round(size.Height);
        if (width <= 0 || height <= 0)
            return;

        if (width == _lastAppliedWidth && height == _lastAppliedHeight)
            return;

        if (throttle)
        {
            // A live resize raises the notification continuously and each pass ends in a
            // full repaint, so coalesce while the user is dragging.
            var now = Environment.TickCount64;
            if (now - _lastClientLayoutTicks < 60)
                return;

            _lastClientLayoutTicks = now;
        }

        _lastAppliedWidth = width;
        _lastAppliedHeight = height;
        ApplyClientLayout(platformView, width, height);
    }

    /// <summary>Runs on the toolkit's UI thread when the window (client area) changes.</summary>
    void OnClientSizeChanged(int width, int height)
    {
        if (PlatformView is { } platformView)
            ApplyClientAreaIfChanged(platformView, throttle: true);
    }

    /// <summary>
    /// Follows the window's real client area: it re-lays out the content once the loop is
    /// running, and again whenever the toolkit reports a different size (the user resizing
    /// the window, or a backend that sizes the surface itself such as Wayland). Without
    /// this, content would stay at whatever size the app asked for.
    /// </summary>
    void WatchClientSize(DuiWindow platformView)
    {
        // Dispatcher lives on Element, not on the IWindow interface.
        var dispatcher = VirtualView is Microsoft.Maui.Controls.Element element
            ? element.Dispatcher
            : null;

        var thread = new Thread(() =>
        {
            while (VirtualView is not null && !platformView.IsDisposed)
            {
                Thread.Sleep(200);

                void Apply() => ApplyClientAreaIfChanged(platformView, throttle: false);

                if (dispatcher is not null && dispatcher.IsDispatchRequired)
                    dispatcher.Dispatch(Apply);
                else
                    Apply();
            }
        })
        {
            IsBackground = true,
            Name = "polluxos-dui-client-size",
        };

        thread.Start();
    }

    /// <summary>
    /// Sizes the root container to the client area and re-runs the content's layout pass.
    /// </summary>
    /// <remarks>
    /// Deliberately ignores the platform's content insets: on macOS the traffic lights and
    /// title float above a full-size content view, and keeping the layout clear of them made
    /// the window render black on that backend. The app gives its own top row enough height
    /// to stay readable instead.
    /// </remarks>
    void ApplyClientLayout(DuiWindow platformView, int width, int height)
    {
        platformView.Root.SetBounds(new Rect(0, 0, width, height));
        ArrangeContent(width, height);
    }

    /// <summary>Runs the content's cross-platform measure/arrange pass.</summary>
    void ArrangeContent(double width, double height)
    {
        if (VirtualView?.Content is not ICrossPlatformLayout content || width <= 0 || height <= 0)
            return;

        content.CrossPlatformMeasure(width, height);
        content.CrossPlatformArrange(new Rect(0, 0, width, height));
    }

    public static void MapTitle(WindowHandler handler, IWindow window)
        => handler.PlatformView?.SetTitle(window.Title ?? string.Empty);

    public static void MapSize(WindowHandler handler, IWindow window)
    {
        var platformView = handler.PlatformView;
        if (platformView is null)
            return;

        if (window.Width > 0 && window.Height > 0)
        {
            platformView.SetSize((int)Math.Round(window.Width), (int)Math.Round(window.Height));
            handler.ApplyClientLayout(platformView, (int)Math.Round(window.Width), (int)Math.Round(window.Height));
        }
    }
}
