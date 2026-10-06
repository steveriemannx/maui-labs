using Microsoft.Maui.Platforms.PolluxOS.DUI.Handlers;
using Microsoft.Maui.Platforms.PolluxOS.DUI.Interop;

// Bring-up host: the DUI backend has no OS lifecycle callback yet, so the app
// starts the toolkit, builds its MAUI window, and then hands the main thread to
// DUI's message loop.
var resourceRoot = Environment.GetEnvironmentVariable("POLLUXOS_DUI_RESOURCES")
    ?? throw new InvalidOperationException("Set POLLUXOS_DUI_RESOURCES to DUI's resources/ directory.");

DuiRuntime.Startup(resourceRoot);

try
{
    var mauiApp = MauiProgram.CreateMauiApp();
    var context = new MauiContext(mauiApp.Services);

    var window = new Microsoft.Maui.Controls.Window(new MainPage()) { Title = "PolluxOSDuiApp" };
    var windowHandler = new WindowHandler();
    ((IElementHandler)windowHandler).SetMauiContext(context);
    ((IElementHandler)windowHandler).SetVirtualView(window);
    window.Handler = windowHandler;

    if (windowHandler.PlatformView is not DuiWindow duiWindow)
        throw new InvalidOperationException("WindowHandler did not produce a DUI window.");

    duiWindow.Show();

    return DuiRuntime.Run();
}
finally
{
    DuiRuntime.Shutdown();
}
