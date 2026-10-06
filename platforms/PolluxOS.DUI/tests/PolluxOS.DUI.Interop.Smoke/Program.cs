using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platforms.PolluxOS.DUI.Interop;

// C# end-to-end smoke test for the dui_shim bridge.
//
// Unlike samples/PolluxOS.DUI.Sample (a MAUI app, net10.0-macos only) this is a plain
// SDK console project with no MAUI dependency, so the same binary runs anywhere the
// native bridge exists — including the polluxos/FreeBSD box, where run with a
// user-local .NET SDK:
//
//   cd ~/projects-main/maui-labs/platforms/PolluxOS.DUI
//   ~/dotnet10/dotnet run --project tests/PolluxOS.DUI.Interop.Smoke -- \
//       ~/dui-freebsd-wl-install/share/dui/resources 8
//
// It drives the shipping interop code (compiled in from src/PolluxOS.DUI/Interop).

var resources = args.Length > 0
    ? args[0]
    : Environment.GetEnvironmentVariable("POLLUXOS_DUI_RESOURCES") ?? string.Empty;
var seconds = args.Length > 1 ? int.Parse(args[1]) : 6;

Console.WriteLine($"dui_shim version : {DuiRuntime.Version}");

DuiRuntime.Startup(resources);
Console.WriteLine($"startup          : ok ({resources})");

var window = DuiWindow.Create(
    title: "dui_shim C# smoke test",
    width: 480,
    height: 320,
    name: "csharp-smoke");
Console.WriteLine("window_create    : ok");

var root = window.Root;
root.SetAttribute("bkcolor", "#FFFFFFFF");

var label = DuiWidget.Create(root, "Label", "smokeLabel");
label.SetBounds(new Rect(24, 24, 400, 48));
label.SetText("hello from C#");
label.SetAttribute("textcolor", "#FF000000");

var button = DuiWidget.Create(root, "Button", "smokeButton");
button.SetBounds(new Rect(24, 96, 160, 40));
button.SetText("Click me");
button.SetAttribute("textcolor", "#FF000000");
button.SetClickHandler(() => Console.WriteLine("click            : Button click reached the C# callback"));

Console.WriteLine("widgets          : Label + Button queued");

window.Show();
Console.WriteLine("window_show      : requested");

var closed = new ManualResetEventSlim(false);
window.Closed += (_, _) => closed.Set();

// Worker thread: inspect the tree, capture, then close — closing the last window ends
// the loop, which is what unblocks DuiRuntime.Run() on the main thread.
var worker = new Thread(() =>
{
    Thread.Sleep(TimeSpan.FromSeconds(seconds));

    var tree = window.TryDumpXml();
    Console.WriteLine($"tree             : {(tree is null ? "(dump unavailable)" : $"{tree.Length} bytes")}");
    if (tree is not null)
        Console.WriteLine(tree);

    Console.WriteLine(
        window.TryCapturePpm("interop_smoke.ppm")
            ? "capture          : ok (interop_smoke.ppm)"
            : "capture          : unavailable on this platform");

    window.Close();
})
{
    IsBackground = true,
    Name = "interop-smoke-worker",
};
worker.Start();

Console.WriteLine($"message loop     : running for {seconds} s ...");
var exitCode = DuiRuntime.Run();
closed.Wait(TimeSpan.FromSeconds(5));
Console.WriteLine($"message loop     : exited ({exitCode})");

DuiRuntime.Shutdown();
Console.WriteLine("SMOKE OK");
return 0;
