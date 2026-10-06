using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.PolluxOS.DUI.Interop;
using Microsoft.Maui.Platforms.PolluxOS.DUI.Platform;

namespace Microsoft.Maui.Platforms.PolluxOS.DUI.Handlers;

/// <summary>
/// Platform side of the MAUI application: owns the DUI windows the app creates.
/// </summary>
public sealed class DuiApplication
{
    readonly List<DuiWindow> _windows = new();

    public IReadOnlyList<DuiWindow> Windows => _windows;

    public bool IsDarkTheme { get; internal set; }

    internal void Track(DuiWindow window)
    {
        if (!_windows.Contains(window))
            _windows.Add(window);
    }

    internal void Untrack(DuiWindow window) => _windows.Remove(window);
}

/// <summary>MAUI <see cref="IApplication"/> → <see cref="DuiApplication"/>.</summary>
public partial class ApplicationHandler : ElementHandler<IApplication, DuiApplication>
{
    public static readonly IPropertyMapper<IApplication, ApplicationHandler> Mapper =
        new PropertyMapper<IApplication, ApplicationHandler>(ElementMapper)
        {
            [nameof(IApplication.UserAppTheme)] = MapAppTheme,
        };

    public static readonly CommandMapper<IApplication, ApplicationHandler> AppCommandMapper =
        new(ElementCommandMapper)
        {
            [nameof(IApplication.OpenWindow)] = MapOpenWindow,
            [nameof(IApplication.CloseWindow)] = MapCloseWindow,
        };

    public ApplicationHandler() : base(Mapper, AppCommandMapper)
    {
    }

    protected override DuiApplication CreatePlatformElement() => new();

    public static void MapAppTheme(ApplicationHandler handler, IApplication application)
    {
        var dark = application.UserAppTheme == AppTheme.Dark;
        if (handler.PlatformView is not null)
            handler.PlatformView.IsDarkTheme = dark;

        // DUI picks its theme (resources/themes/<theme>, incl. a `polluxos` theme)
        // when the toolkit starts; the runtime switch is not wired in the bridge yet.
        if (!DuiRuntime.TrySetTheme(dark))
        {
            // Recorded, not fatal: the app still runs with the startup theme.
            System.Diagnostics.Debug.WriteLine("PolluxOS.DUI: runtime theme switching is not wired yet.");
        }
    }

    public static void MapOpenWindow(ApplicationHandler handler, IApplication application, object? args)
    {
        if (args is not IWindow window || handler.MauiContext is null)
            return;

        // Realizing the MAUI window runs WindowHandler, which creates the DUI window.
        if (window.ToPlatform(handler.MauiContext) is DuiWindow platformWindow)
        {
            handler.PlatformView?.Track(platformWindow);
            platformWindow.Show();
        }
    }

    public static void MapCloseWindow(ApplicationHandler handler, IApplication application, object? args)
    {
        if (args is not IWindow window)
            return;

        if (window.Handler?.PlatformView is DuiWindow platformWindow)
        {
            handler.PlatformView?.Untrack(platformWindow);
            platformWindow.Close();
        }
    }
}
