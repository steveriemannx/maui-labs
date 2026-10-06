using Microsoft.Maui.Platforms.PolluxOS.DUI.Essentials;
using Microsoft.Maui.Platforms.PolluxOS.DUI.Hosting;

namespace PolluxOS.DUI.Sample;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UsePolluxOSDui<App>();
        builder.UsePolluxOSDuiEssentials();
        return builder.Build();
    }
}
