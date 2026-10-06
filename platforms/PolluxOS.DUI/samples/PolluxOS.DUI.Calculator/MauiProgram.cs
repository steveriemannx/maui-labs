using Microsoft.Maui.Platforms.PolluxOS.DUI.Essentials;
using Microsoft.Maui.Platforms.PolluxOS.DUI.Hosting;

namespace PolluxOS.DUI.Calculator;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UsePolluxOSDui<CalculatorApp>();
        builder.UsePolluxOSDuiEssentials();
        return builder.Build();
    }
}

public class CalculatorApp : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
        => new(new CalculatorPage()) { Title = "PolluxOS Calculator" };
}
