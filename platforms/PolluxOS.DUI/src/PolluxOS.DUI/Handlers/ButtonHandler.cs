using System.Globalization;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.PolluxOS.DUI.Interop;

namespace Microsoft.Maui.Platforms.PolluxOS.DUI.Handlers;

/// <summary>MAUI button → DUI <c>Button</c> control.</summary>
/// <remarks>
/// The text properties live on <see cref="IText"/>/<see cref="ITextStyle"/> rather than on
/// <see cref="IButton"/>, and the click notification is raised through
/// <see cref="IButtonController"/> — the shapes the platform-neutral MAUI build exposes.
/// </remarks>
public partial class ButtonHandler : DuiViewHandler<IButton>
{
    public static readonly IPropertyMapper<IButton, ButtonHandler> Mapper =
        new PropertyMapper<IButton, ButtonHandler>(ViewMapper)
        {
            [nameof(IText.Text)] = MapText,
            [nameof(ITextStyle.TextColor)] = MapTextColor,
            [nameof(ITextStyle.Font)] = MapFont,
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

        // DUI click → MAUI's button notification pipeline.
        widget.SetClickHandler(() =>
        {
            if (VirtualView is IButtonController controller)
                controller.SendClicked();
        });

        return widget;
    }

    public static void MapText(ButtonHandler handler, IButton button)
        => handler.PlatformView?.SetText(button is IText text ? text.Text ?? string.Empty : string.Empty);

    public static void MapTextColor(ButtonHandler handler, IButton button)
        => handler.PlatformView?.SetAttribute(
            "textcolor",
            button is ITextStyle style && style.TextColor is { } color ? ToDuiColor(color) : null);

    public static void MapFont(ButtonHandler handler, IButton button)
    {
        if (handler.PlatformView is not { } widget || button is not ITextStyle style)
            return;

        if (style.Font.Size > 0)
            widget.SetAttribute("fontsize", ((int)Math.Round(style.Font.Size)).ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrEmpty(style.Font.Family))
            widget.SetAttribute("fontfamily", style.Font.Family);
    }

    public static void MapPadding(ButtonHandler handler, IButton button)
    {
        var padding = button.Padding;
        handler.PlatformView?.SetAttribute(
            "padding",
            string.Create(
                CultureInfo.InvariantCulture,
                $"{(int)padding.Left},{(int)padding.Top},{(int)padding.Right},{(int)padding.Bottom}"));
    }
}
