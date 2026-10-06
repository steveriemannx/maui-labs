namespace PolluxOSDuiApp;

public class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
        => new(new MainPage()) { Title = "PolluxOSDuiApp" };
}
