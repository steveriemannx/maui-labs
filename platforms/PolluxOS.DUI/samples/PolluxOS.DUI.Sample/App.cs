namespace PolluxOS.DUI.Sample;

public class App : Application
{
    public App()
    {
    }

    protected override Window CreateWindow(IActivationState? activationState)
        => new(new MainPage()) { Title = "PolluxOS.DUI Sample" };
}
