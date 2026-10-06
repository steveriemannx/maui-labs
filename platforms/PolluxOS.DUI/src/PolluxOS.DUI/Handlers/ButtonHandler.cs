using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.PolluxOS.DUI.Interop;

namespace Microsoft.Maui.Platforms.PolluxOS.DUI.Handlers;

/// <summary>MAUI <see cref="IButton"/> → DUI <c>Button</c> control.</summary>
public partial class ButtonHandler : DuiViewHandler<IButton>
{
    public static readonly IPropertyMapper<IButton, ButtonHandler> Mapper =
        new PropertyMapper<IButton, ButtonHandler>(ViewMapper)
        {
            [nameof(IButton.Text)] = MapText,
            [nameof(IButton.TextColor)] = MapTextColor,
            [nameof(IButton.Background)] = MapBackground,
            [nameof(IButton.Padding)] = MapPadding,
        };

    public ButtonHandler() : base(Mapper)
    {
    }

    protected override string ControlClass => DuiControlClass.Button;

    protected override Size DefaultDesiredSize => new(120, 36);

    protected override DuiWidget CreatePlatformView()
    {
        var widget = base.CreatePlatformView();

        // DUI click → MAUI's IButton event pipeline.
        widget.SetClickHandler(() =>
        {
            if (VirtualView is IButton button)
                button.SendClicked();
        });

        return widget;
    }

    public static void MapText(ButtonHandler handler, IButton button)
        => handler.PlatformView?.SetText(button.Text ?? string.Empty);

    public static void MapTextColor(ButtonHandler handler, IButton button)
        => handler.PlatformView?.SetAttribute(
            "textcolor",
            button.TextColor is null ? null : ToDuiColor(button.TextColor));

    public static void MapPadding(ButtonHandler handler, IButton button)
    {
        var padding = button.Padding;
        handler.PlatformView?.SetAttribute(
            "padding",
            string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{(int)padding.Left},{(int)padding.Top},{(int)padding.Right},{(int)padding.Bottom}"));
    }
}
