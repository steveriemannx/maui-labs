using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.PolluxOS.DUI.Interop;
using Microsoft.Maui.Platforms.PolluxOS.DUI.Platform;

namespace Microsoft.Maui.Platforms.PolluxOS.DUI.Handlers;

/// <summary>
/// MAUI <see cref="IContentView"/> → DUI <c>Box</c> that hosts the presented content.
/// </summary>
public partial class ContentViewHandler : DuiViewHandler<IContentView>
{
    public static readonly IPropertyMapper<IContentView, ContentViewHandler> Mapper =
        new PropertyMapper<IContentView, ContentViewHandler>(ViewMapper)
        {
            [nameof(IContentView.PresentedContent)] = MapPresentedContent,
        };

    public ContentViewHandler() : base(Mapper)
    {
    }

    protected override string ControlClass => DuiControlClass.Box;

    public static void MapPresentedContent(ContentViewHandler handler, IContentView view)
        => handler.SetContent(view.PresentedContent as IView ?? view.Content);

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
            content.ToPlatform(MauiContext);
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
