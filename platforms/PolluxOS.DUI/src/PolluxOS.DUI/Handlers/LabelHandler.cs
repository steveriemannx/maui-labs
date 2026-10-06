using System.Globalization;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.PolluxOS.DUI.Interop;

namespace Microsoft.Maui.Platforms.PolluxOS.DUI.Handlers;

/// <summary>MAUI <see cref="ILabel"/> → DUI <c>Label</c> control.</summary>
public partial class LabelHandler : DuiViewHandler<ILabel>
{
    public static readonly IPropertyMapper<ILabel, LabelHandler> Mapper =
        new PropertyMapper<ILabel, LabelHandler>(ViewMapper)
        {
            [nameof(ILabel.Text)] = MapText,
            [nameof(ILabel.TextColor)] = MapTextColor,
            [nameof(ILabel.Font)] = MapFont,
            [nameof(ILabel.HorizontalTextAlignment)] = MapHorizontalTextAlignment,
        };

    public LabelHandler() : base(Mapper)
    {
    }

    protected override string ControlClass => DuiControlClass.Label;

    protected override Size DefaultDesiredSize => new(120, 24);

    public static void MapText(LabelHandler handler, ILabel label)
        => handler.PlatformView?.SetText(label.Text ?? string.Empty);

    public static void MapTextColor(LabelHandler handler, ILabel label)
        => handler.PlatformView?.SetAttribute(
            "textcolor",
            label.TextColor is null ? null : ToDuiColor(label.TextColor));

    public static void MapFont(LabelHandler handler, ILabel label)
    {
        var widget = handler.PlatformView;
        if (widget is null)
            return;

        var font = label.Font;
        if (font.Size > 0)
            widget.SetAttribute("fontsize", ((int)Math.Round(font.Size)).ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrEmpty(font.Family))
            widget.SetAttribute("fontfamily", font.Family);
    }

    public static void MapHorizontalTextAlignment(LabelHandler handler, ILabel label)
        => handler.PlatformView?.SetAttribute(
            "align",
            label.HorizontalTextAlignment switch
            {
                TextAlignment.Center => "center",
                TextAlignment.End => "right",
                _ => "left",
            });
}
