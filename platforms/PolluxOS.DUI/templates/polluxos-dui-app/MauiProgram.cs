using Microsoft.Maui.Platforms.PolluxOS.DUI.Hosting;

namespace PolluxOSDuiApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UsePolluxOSDui<App>();
        return builder.Build();
    }
}
