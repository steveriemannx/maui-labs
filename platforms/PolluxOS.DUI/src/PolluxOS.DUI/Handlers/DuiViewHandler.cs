using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.PolluxOS.DUI.Interop;
using Microsoft.Maui.Platforms.PolluxOS.DUI.Platform;

namespace Microsoft.Maui.Platforms.PolluxOS.DUI.Handlers;

/// <summary>
/// Base handler for DUI-backed MAUI views: creates the DUI control, maps the
/// shared <c>IView</c> properties, and bridges MAUI's layout rectangle to DUI.
/// </summary>
public abstract class DuiViewHandler<TVirtualView> : ViewHandler<TVirtualView, DuiWidget>
    where TVirtualView : class, IView
{
    static DuiViewHandler()
    {
        // Shared IView mapping, registered once against MAUI's view mapper — the
        // same pattern the AppKit/WPF backends in this repo use.
        try
        {
            if (ViewMapper is PropertyMapper<IView, IViewHandler> mapper)
            {
                mapper[nameof(IView.Visibility)] = MapVisibility;
                mapper[nameof(IView.IsEnabled)] = MapIsEnabled;
                mapper[nameof(IView.AutomationId)] = MapAutomationId;
                mapper[nameof(IView.Background)] = MapBackground;
            }
        }
        catch
        {
            // Mapping registration failed — non-fatal.
        }
    }

    protected DuiViewHandler(IPropertyMapper mapper, CommandMapper? commandMapper = null)
        : base(mapper, commandMapper)
    {
    }

    /// <summary>DUI control class name to instantiate — a <c>DUI_CTR_*</c> value from
    /// DUI's <c>include/dui/dui_defs.h</c>.</summary>
    protected abstract string ControlClass { get; }

    /// <summary>Size used when neither MAUI nor DUI can produce a measurement.</summary>
    protected virtual Size DefaultDesiredSize => new(0, 0);

    protected override DuiWidget CreatePlatformView()
    {
        var platformContext = MauiContext?.Services.GetService<DuiPlatformContext>()
            ?? throw new InvalidOperationException("DuiPlatformContext is not registered. Call UsePolluxOSDui().");

        var parent = platformContext.RequireParent();
        var name = VirtualView?.AutomationId;
        var handle = DuiNative.OrThrow(
            DuiNative.dui_shim_widget_create(parent.Handle, ControlClass, name),
            $"create DUI control '{ControlClass}'");

        return new DuiWidget(handle);
    }

    public static new void MapVisibility(IViewHandler handler, IView view)
        => (handler.PlatformView as DuiWidget)?.SetVisible(view.Visibility == Visibility.Visible);

    public static new void MapIsEnabled(IViewHandler handler, IView view)
        => (handler.PlatformView as DuiWidget)?.SetEnabled(view.IsEnabled);

    public static new void MapAutomationId(IViewHandler handler, IView view)
        => (handler.PlatformView as DuiWidget)?.SetAttribute("name", view.AutomationId);

    public static new void MapBackground(IViewHandler handler, IView view)
    {
        if (handler.PlatformView is not DuiWidget widget)
            return;

        widget.SetAttribute(
            "bkcolor",
            view.Background is SolidPaint { Color: { } color } ? ToDuiColor(color) : null);
    }

    /// <summary>DUI accepts <c>#AARRGGBB</c> colour attributes.</summary>
    protected internal static string ToDuiColor(Color color)
        => $"#{ToByte(color.Alpha):X2}{ToByte(color.Red):X2}{ToByte(color.Green):X2}{ToByte(color.Blue):X2}";

    static int ToByte(float value) => (int)Math.Round(Math.Clamp(value, 0f, 1f) * 255f);

    public override Size GetDesiredSize(double widthConstraint, double heightConstraint)
    {
        var view = VirtualView;
        if (view is null)
            return Size.Zero;

        if (double.IsNaN(widthConstraint))
            widthConstraint = double.PositiveInfinity;
        if (double.IsNaN(heightConstraint))
            heightConstraint = double.PositiveInfinity;

        var hasWidth = IsExplicit(view.Width);
        var hasHeight = IsExplicit(view.Height);

        if (hasWidth && hasHeight)
            return new Size(view.Width, view.Height);

        // DUI sizes controls during its own layout pass, and the bridge cannot
        // measure them yet (dui_shim_widget_measure returns "unavailable"), so MAUI
        // explicit sizes win and this default is the fallback.
        var fallback = DefaultDesiredSize;
        var width = hasWidth ? view.Width : Math.Min(fallback.Width, widthConstraint);
        var height = hasHeight ? view.Height : Math.Min(fallback.Height, heightConstraint);

        if (IsExplicit(view.MinimumWidth))
            width = Math.Max(width, view.MinimumWidth);
        if (IsExplicit(view.MinimumHeight))
            height = Math.Max(height, view.MinimumHeight);
        if (IsExplicit(view.MaximumWidth))
            width = Math.Min(width, view.MaximumWidth);
        if (IsExplicit(view.MaximumHeight))
            height = Math.Min(height, view.MaximumHeight);

        return new Size(width, height);
    }

    public override void PlatformArrange(Rect rect)
    {
        PlatformView?.SetBounds(rect);
    }

    protected static bool IsExplicit(double value)
        => !double.IsNaN(value) && value >= 0 && !double.IsPositiveInfinity(value);
}
