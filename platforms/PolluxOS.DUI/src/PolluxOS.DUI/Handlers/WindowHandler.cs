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

        var width = VirtualView.Width > 0 ? VirtualView.Width : 800;
        var height = VirtualView.Height > 0 ? VirtualView.Height : 600;
        platformView.Root.SetBounds(new Rect(0, 0, width, height));
        ArrangeContent(width, height);
        WatchClientSize(platformView);
    }

    /// <summary>
    /// Re-lays out the content to the window's real client area once the toolkit's message
    /// loop is running. Needed because some backends (Wayland) size the surface themselves
    /// and ignore a requested resize — without this the page would sit in one corner of a
    /// window that is larger than the size the app asked for.
    /// </summary>
    void WatchClientSize(DuiWindow platformView)
    {
        // Dispatcher lives on Element, not on the IWindow interface.
        var dispatcher = VirtualView is Microsoft.Maui.Controls.Element element
            ? element.Dispatcher
            : null;

        var thread = new Thread(() =>
        {
            for (var attempt = 0; attempt < 60; attempt++)
            {
                Thread.Sleep(50);

                if (!platformView.TryGetClientSize(out var size))
                    continue;

                void Apply()
                {
                    platformView.Root.SetBounds(new Rect(0, 0, size.Width, size.Height));
                    ArrangeContent(size.Width, size.Height);
                }

                if (dispatcher is not null && dispatcher.IsDispatchRequired)
                    dispatcher.Dispatch(Apply);
                else
                    Apply();

                return;
            }
        })
        {
            IsBackground = true,
            Name = "polluxos-dui-client-size",
        };

        thread.Start();
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
            platformView.Root.SetBounds(new Rect(0, 0, window.Width, window.Height));
            handler.ArrangeContent(window.Width, window.Height);
        }
    }
}
