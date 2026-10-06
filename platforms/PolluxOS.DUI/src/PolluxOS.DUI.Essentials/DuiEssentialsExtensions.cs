using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Hosting;

namespace Microsoft.Maui.Platforms.PolluxOS.DUI.Essentials;

/// <summary>
/// Registers the Essentials implementations that exist for the DUI backend.
/// </summary>
/// <remarks>
/// Only the services with a working implementation are replaced; everything else
/// keeps MAUI's default, which throws <see cref="NotSupportedException"/> until a
/// PolluxOS/DUI implementation is written. That is deliberate: a stub that silently
/// returns default values hides missing platform support.
/// </remarks>
public static class DuiEssentialsExtensions
{
    public static MauiAppBuilder UsePolluxOSDuiEssentials(this MauiAppBuilder builder)
    {
        builder.Services.AddSingleton<IPreferences, DuiPreferences>();
        return builder;
    }
}
