namespace PolluxOSDuiApp;

public class MainPage : ContentPage
{
    public MainPage()
    {
        Content = new VerticalStackLayout
        {
            Padding = 24,
            Spacing = 12,
            Children =
            {
                new Label { Text = "Hello, DUI!", FontSize = 20, AutomationId = "TitleLabel" },
                new Button { Text = "Click me", AutomationId = "HelloButton" },
            },
        };
    }
}
