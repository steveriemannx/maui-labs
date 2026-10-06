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

        var platformWindow = DuiWindow.Create(window.Title ?? "PolluxOS.DUI", width, height, name: window.Id.ToString());

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
        // DuiPlatformContext; the root is then sized to the window.
        content.ToPlatform(MauiContext);

        var width = VirtualView.Width > 0 ? VirtualView.Width : 800;
        var height = VirtualView.Height > 0 ? VirtualView.Height : 600;
        platformView.Root.SetBounds(new Rect(0, 0, width, height));
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
        }
    }
}
