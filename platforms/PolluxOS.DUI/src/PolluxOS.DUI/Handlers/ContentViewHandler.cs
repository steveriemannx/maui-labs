using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.PolluxOS.DUI.Interop;
using Microsoft.Maui.Platforms.PolluxOS.DUI.Platform;

namespace Microsoft.Maui.Platforms.PolluxOS.DUI.Handlers;

/// <summary>
/// MAUI <see cref="IContentView"/> → DUI <c>Box</c> that hosts the presented content.
/// </summary>
public partial class ContentViewHandler : DuiViewHandler<IContentView>
{
    static readonly IPropertyMapper<IContentView, ContentViewHandler> ContentMapper =
        new PropertyMapper<IContentView, ContentViewHandler>(ViewMapper)
        {
            [nameof(IContentView.PresentedContent)] = MapPresentedContent,
        };

    public ContentViewHandler() : base(ContentMapper)
    {
    }

    protected override string ControlClass => DuiControlClass.Box;

    protected override void ConnectHandler(DuiWidget platformView)
    {
        base.ConnectHandler(platformView);

        // The page's content is usually set before the handler exists, so realize it on
        // connect as well as through the property mapper.
        if (VirtualView is not null)
            SetContent(VirtualView.PresentedContent ?? VirtualView.Content as IView);
    }

    // IContentView is an ICrossPlatformLayout: forwarding measure/arrange here is what
    // makes the page's content (and, transitively, the layout's children) get rectangles.
    public override Size GetDesiredSize(double widthConstraint, double heightConstraint)
        => VirtualView is ICrossPlatformLayout crossPlatformLayout
            ? crossPlatformLayout.CrossPlatformMeasure(widthConstraint, heightConstraint)
            : base.GetDesiredSize(widthConstraint, heightConstraint);

    public override void PlatformArrange(Rect rect)
    {
        base.PlatformArrange(rect);

        if (VirtualView is ICrossPlatformLayout crossPlatformLayout)
            crossPlatformLayout.CrossPlatformArrange(new Rect(0, 0, rect.Width, rect.Height));
    }

    public static void MapPresentedContent(ContentViewHandler handler, IContentView view)
        => handler.SetContent(view.PresentedContent ?? view.Content as IView);

    /// <summary>Realizes the page's content inside this container.</summary>
    public void SetContent(IView? content)
    {
        if (MauiContext is null || content is null)
            return;

        var platformContext = MauiContext.Services.GetService<DuiPlatformContext>();
        if (platformContext is null)
            return;

        using (platformContext.PushParent(PlatformView))
        {
            // No ToPlatform on the platform-neutral build: the handler's PlatformView is
            // the bridge widget. ToHandler realizes the child's handler (and thus its DUI
            // control) with this container on the parent stack.
            content.ToHandler(MauiContext);
        }
    }
}

/// <summary>MAUI <see cref="Microsoft.Maui.Controls.ContentPage"/> → DUI container.</summary>
public partial class ContentPageHandler : ContentViewHandler
{
    public ContentPageHandler()
    {
    }
}
