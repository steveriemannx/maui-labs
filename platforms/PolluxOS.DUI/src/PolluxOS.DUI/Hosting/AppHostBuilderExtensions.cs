using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.PolluxOS.DUI.Handlers;
using Microsoft.Maui.Platforms.PolluxOS.DUI.Interop;
using Microsoft.Maui.Platforms.PolluxOS.DUI.Platform;

namespace Microsoft.Maui.Platforms.PolluxOS.DUI.Hosting;

/// <summary>
/// Entry point for apps that render through the DUI toolkit.
/// </summary>
/// <remarks>
/// Usage (macOS bring-up):
/// <code>
/// DuiRuntime.Startup(resourceRoot);
/// var app = MauiProgram.CreateMauiApp();      // builder.UsePolluxOSDui&lt;App&gt;()
/// var window = new Window(new MainPage()) { Title = "Hello" };
/// app.Services.GetRequiredService&lt;IApplication&gt;().OpenWindow(window);
/// DuiRuntime.Run();                           // blocks on the DUI message loop
/// </code>
/// </remarks>
public static partial class AppHostBuilderExtensions
{
    public static MauiAppBuilder UsePolluxOSDui<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TApp>(
        this MauiAppBuilder builder)
        where TApp : class, IApplication
    {
        builder.UseMauiApp<TApp>();
        builder.SetupDefaults();
        return builder;
    }

    static MauiAppBuilder SetupDefaults(this MauiAppBuilder builder)
    {
        builder.Services.AddSingleton<DuiPlatformContext>();
        builder.Services.AddSingleton<IDispatcherProvider>(_ => new DuiDispatcherProvider());

        // Give MAUI a UI thread: DUI's idle callback drains the dispatcher queue on the
        // toolkit's own thread (this is the piece that makes the backend work on hosts
        // where no platform dispatcher exists, e.g. FreeBSD/polluxos).
        DuiRuntime.UseDispatcher();

        builder.Services.AddScoped(svc =>
        {
            var provider = svc.GetRequiredService<IDispatcherProvider>();
            if (DispatcherProvider.SetCurrent(provider))
                svc.GetService<ILogger<Dispatcher>>()?.LogWarning("Replaced an existing DispatcherProvider.");

            return Dispatcher.GetForCurrentThread()!;
        });

        builder.ConfigureMauiHandlers(handlers =>
        {
            handlers.AddHandler<Application, ApplicationHandler>();
            handlers.AddHandler<Microsoft.Maui.Controls.Window, WindowHandler>();
            handlers.AddHandler<ContentPage, ContentPageHandler>();
            handlers.AddHandler<ContentView, ContentViewHandler>();
            handlers.AddHandler<Layout, LayoutHandler>();
            handlers.AddHandler<Microsoft.Maui.Controls.Label, LabelHandler>();
            handlers.AddHandler<Microsoft.Maui.Controls.Button, ButtonHandler>();
        });

        return builder;
    }
}
