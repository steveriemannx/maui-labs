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

    var page = new CalculatorPage();
    var window = new Microsoft.Maui.Controls.Window(page)
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
    var selfTest = args.Any(argument => string.Equals(argument, "selftest", StringComparison.OrdinalIgnoreCase));

    if (selfTest || probeSeconds > 0)
    {
        var probe = new Thread(() =>
        {
            if (args.Any(a => string.Equals(a, "pointertest", StringComparison.OrdinalIgnoreCase)))
            {
                // Clicks through the toolkit's own hit testing (the path a real pointer
                // click takes), at the centre of each button in turn: 7 + 8 = 15.
                Thread.Sleep(TimeSpan.FromSeconds(2));
                foreach (var id in new[] { "Btn7", "BtnAdd", "Btn8", "BtnEquals" })
                {
                    if (page.TryGetButtonBounds(id, out var bounds))
                    {
                        var x = (int)Math.Round(bounds.X + (bounds.Width / 2));
                        var y = (int)Math.Round(bounds.Y + (bounds.Height / 2));
                        Console.WriteLine($"pointertest: click {id} at {x},{y} (bounds {bounds})");
                        duiWindow.SimulateClick(x, y);
                    }
                    else
                    {
                        Console.WriteLine($"pointertest: no bounds for {id}");
                    }

                    Thread.Sleep(300);
                }

                Console.WriteLine($"pointertest: display = '{page.DisplayText}' (expected 15)");
            }

            if (args.Any(a => string.Equals(a, "resizetest", StringComparison.OrdinalIgnoreCase)))
            {
                // The host follows the window's client area; resize and check that the
                // layout followed (on backends that honour a programmatic resize).
                Thread.Sleep(TimeSpan.FromSeconds(2));
                Console.WriteLine($"resizetest: before root='{duiWindow.Root.GetBounds()}' client={(duiWindow.TryGetClientSize(out var c0) ? c0.ToString() : "?")}");
                duiWindow.SetSize(600, 700);
                Thread.Sleep(TimeSpan.FromSeconds(2));
                Console.WriteLine($"resizetest: after root='{duiWindow.Root.GetBounds()}' client={(duiWindow.TryGetClientSize(out var c1) ? c1.ToString() : "?")}");
                foreach (var id in new[] { "Btn7", "BtnEquals" })
                    if (page.TryGetButtonBounds(id, out var bounds))
                        Console.WriteLine($"resizetest: {id} at {bounds}");
            }

            if (selfTest)
            {
                // Drive the buttons through DUI's own click notification (the path a
                // pointer click takes) and report the display, so the native → managed
                // wiring can be verified without input devices: 7 + 8 = 15.
                Thread.Sleep(TimeSpan.FromSeconds(2));
                foreach (var id in new[] { "Btn7", "BtnAdd", "Btn8", "BtnEquals" })
                {
                    page.PressOnPlatform(id);
                    Thread.Sleep(250);
                }

                Console.WriteLine($"selftest: display = '{page.DisplayText}' (expected 15)");
            }

            if (probeSeconds > 0)
            {
                Thread.Sleep(TimeSpan.FromSeconds(probeSeconds));
                Console.WriteLine("Widget tree:");
                Console.WriteLine(duiWindow.TryDumpXml() ?? "(dump unavailable)");
                duiWindow.Close();
            }
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
