namespace PolluxOS.DUI.Sample;

public class MainPage : ContentPage
{
    int _count;

    public MainPage()
    {
        var counter = new Label { Text = "0 clicks", AutomationId = "CounterLabel" };

        var button = new Button { Text = "Click me", AutomationId = "CounterButton" };
        button.Clicked += (_, _) =>
        {
            _count++;
            counter.Text = $"{_count} click{(_count == 1 ? string.Empty : "s")}";
        };

        Content = new VerticalStackLayout
        {
            Padding = 24,
            Spacing = 12,
            Children =
            {
                new Label
                {
                    Text = "PolluxOS.DUI backend",
                    AutomationId = "TitleLabel",
                    FontSize = 20,
                },
                counter,
                button,
            },
        };
    }
}
