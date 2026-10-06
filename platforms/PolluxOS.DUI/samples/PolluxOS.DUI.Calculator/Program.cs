using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.PolluxOS.DUI.Handlers;
using Microsoft.Maui.Platforms.PolluxOS.DUI.Interop;
using PolluxOS.DUI.Calculator;

// Host for the calculator sample: start the toolkit, build the MAUI app, create the
// window (which creates the DUI window through WindowHandler), then hand the thread to
// DUI's message loop. Trailing seconds argument = probe mode (dump tree + close).
var resources = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0])
    ? args[0]
    : Environment.GetEnvironmentVariable("POLLUXOS_DUI_RESOURCES")
      ?? throw new InvalidOperationException("Pass DUI's resources directory (or set POLLUXOS_DUI_RESOURCES).");

Console.WriteLine($"DUI resources: {resources}");
Console.WriteLine($"dui_shim version: {DuiRuntime.Version}");

DuiRuntime.Startup(resources);

try
{
    var mauiApp = MauiProgram.CreateMauiApp();
    var context = new MauiContext(mauiApp.Services);

    var application = mauiApp.Services.GetRequiredService<IApplication>();
    var applicationHandler = new ApplicationHandler();
    ((IElementHandler)applicationHandler).SetMauiContext(context);
    ((IElementHandler)applicationHandler).SetVirtualView(application);

    var window = new Microsoft.Maui.Controls.Window(new CalculatorPage())
    {
        Title = "PolluxOS Calculator",
        Width = 380,
        Height = 560,
    };

    var windowHandler = new WindowHandler();
    ((IElementHandler)windowHandler).SetMauiContext(context);
    ((IElementHandler)windowHandler).SetVirtualView(window);
    window.Handler = windowHandler;

    if (windowHandler.PlatformView is not DuiWindow duiWindow)
        throw new InvalidOperationException("WindowHandler did not produce a DUI window.");

    duiWindow.Show();
    Console.WriteLine("window_show: requested");

    var probeSeconds = args.Length > 1 && int.TryParse(args[1], out var parsed) ? parsed : 0;
    if (probeSeconds > 0)
    {
        var probe = new Thread(() =>
        {
            Thread.Sleep(TimeSpan.FromSeconds(probeSeconds));
            Console.WriteLine("Widget tree:");
            Console.WriteLine(duiWindow.TryDumpXml() ?? "(dump unavailable)");
            duiWindow.Close();
        })
        {
            IsBackground = true,
            Name = "calculator-probe",
        };
        probe.Start();
    }

    return DuiRuntime.Run();
}
finally
{
    DuiRuntime.Shutdown();
}
