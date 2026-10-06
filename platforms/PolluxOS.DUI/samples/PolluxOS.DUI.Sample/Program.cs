using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.PolluxOS.DUI.Handlers;
using Microsoft.Maui.Platforms.PolluxOS.DUI.Interop;
using PolluxOS.DUI.Sample;

// Bring-up host for the PolluxOS.DUI backend.
//
// The flow is deliberately explicit — the backend has no OS lifecycle callback yet:
//   1. start the DUI toolkit (it needs its resources/ tree on disk),
//   2. build the MAUI app and register the DUI handlers,
//   3. create the MAUI window, which creates the DUI window through WindowHandler,
//   4. hand the main thread to DUI's message loop (blocks until the window closes).
//
// Note: window creation goes through WindowHandler directly. ApplicationHandler
// (OpenWindow/CloseWindow) is scaffolded for the full MAUI lifecycle but is not
// exercised by this bring-up path yet.

var resourceRoot = ResolveResourceRoot();
Console.WriteLine($"DUI resources: {resourceRoot}");
Console.WriteLine($"dui_shim version: {DuiRuntime.Version}");

DuiRuntime.Startup(resourceRoot);

try
{
    var mauiApp = MauiProgram.CreateMauiApp();
    var context = new MauiContext(mauiApp.Services);

    var application = mauiApp.Services.GetRequiredService<IApplication>();
    var applicationHandler = new ApplicationHandler();
    ((IElementHandler)applicationHandler).SetMauiContext(context);
    ((IElementHandler)applicationHandler).SetVirtualView(application);

    var window = new Microsoft.Maui.Controls.Window(new MainPage())
    {
        Title = "PolluxOS.DUI Sample",
        Width = 900,
        Height = 600,
    };

    var windowHandler = new WindowHandler();
    ((IElementHandler)windowHandler).SetMauiContext(context);
    ((IElementHandler)windowHandler).SetVirtualView(window);
    window.Handler = windowHandler;

    if (windowHandler.PlatformView is not DuiWindow duiWindow)
        throw new InvalidOperationException("WindowHandler did not produce a DUI window.");

    duiWindow.Closed += (_, _) =>
    {
        // DUI quits the loop itself once the last window closes
        // (PostQuitMsgWhenClosed). A cross-thread quit is not wired in the bridge yet.
        Console.WriteLine("DUI window closed.");
    };
    duiWindow.Show();

    Console.WriteLine("Widget tree at startup:");
    Console.WriteLine(duiWindow.TryDumpXml() ?? "(bridge could not serialize the tree)");

    return DuiRuntime.Run();
}
finally
{
    DuiRuntime.Shutdown();
}

static string ResolveResourceRoot()
{
    // The sample csproj stamps the resolved directory into assembly metadata; fall
    // back to an explicit override for ad-hoc runs.
    var fromEnvironment = Environment.GetEnvironmentVariable("POLLUXOS_DUI_RESOURCES");
    if (!string.IsNullOrWhiteSpace(fromEnvironment))
        return fromEnvironment;

    var metadata = System.Reflection.Assembly.GetEntryAssembly()
        ?.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "PolluxOSDuiResourcesDir")?.Value;

    if (!string.IsNullOrWhiteSpace(metadata))
        return metadata;

    throw new InvalidOperationException(
        "DUI resource directory not found. Build DUI first (scripts/build-dui-macos.sh) or set POLLUXOS_DUI_RESOURCES.");
}
