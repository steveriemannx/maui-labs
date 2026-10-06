using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platforms.PolluxOS.DUI.Interop;

namespace Microsoft.Maui.Platforms.PolluxOS.DUI.Handlers;

/// <summary>
/// A DUI container that also participates in MAUI's cross-platform layout.
/// </summary>
/// <remarks>
/// MAUI arranges a layout by asking the container to measure/arrange itself
/// (<see cref="ICrossPlatformLayout"/>), and the cross-platform layout engine then places
/// each child — which calls the child handler's <c>PlatformArrange</c>, i.e. the DUI
/// control's rectangle. Without this the children never receive a rectangle and pile up
/// in the container's corner (which is exactly what the first FreeBSD screenshots showed).
///
/// The container owns the widget handle it was created with; the intermediate
/// <see cref="DuiWidget"/> instance is only a carrier.
/// </remarks>
public sealed class DuiLayoutPanel : DuiWidget, ICrossPlatformLayout
{
    readonly ICrossPlatformLayout _crossPlatformLayout;

    internal DuiLayoutPanel(DuiWidget widget, ICrossPlatformLayout crossPlatformLayout)
        : base(widget.Handle)
    {
        _crossPlatformLayout = crossPlatformLayout;
    }

    public Size CrossPlatformMeasure(double widthConstraint, double heightConstraint)
        => _crossPlatformLayout.CrossPlatformMeasure(widthConstraint, heightConstraint);

    public Size CrossPlatformArrange(Rect bounds)
        => _crossPlatformLayout.CrossPlatformArrange(bounds);
}
