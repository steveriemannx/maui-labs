using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.PolluxOS.DUI.Interop;
using Microsoft.Maui.Platforms.PolluxOS.DUI.Platform;

namespace Microsoft.Maui.Platforms.PolluxOS.DUI.Handlers;

/// <summary>
/// MAUI <see cref="ILayout"/> → DUI <c>Box</c> container.
/// </summary>
/// <remarks>
/// DUI only creates a control when it is handed a parent, so each added child is
/// realized with this layout's container pushed on the
/// <see cref="DuiPlatformContext"/> parent stack.
/// </remarks>
public partial class LayoutHandler : DuiViewHandler<ILayout>
{
    public static readonly IPropertyMapper<ILayout, LayoutHandler> Mapper =
        new PropertyMapper<ILayout, LayoutHandler>(ViewMapper)
        {
            [nameof(ILayout.ClipsToBounds)] = MapClipsToBounds,
        };

    public static readonly CommandMapper<ILayout, LayoutHandler> LayoutCommandMapper =
        new(ViewCommandMapper)
        {
            [nameof(ILayoutHandler.Add)] = MapAdd,
            [nameof(ILayoutHandler.Insert)] = MapInsert,
            [nameof(ILayoutHandler.Update)] = MapUpdate,
            [nameof(ILayoutHandler.Remove)] = MapRemove,
            [nameof(ILayoutHandler.Clear)] = MapClear,
        };

    public LayoutHandler() : base(Mapper, LayoutCommandMapper)
    {
    }

    protected override string ControlClass => DuiControlClass.Box;

    public void Add(IView child) => RealizeChild(child);

    public void Insert(int index, IView child)
    {
        // TODO: DUI containers have no positional insert; ordering is currently
        // append-only. Revisit when Box exposes an index-based API.
        RealizeChild(child);
    }

    public void Update(int index, IView child)
    {
        Remove(child);
        RealizeChild(child);
    }

    public void Remove(IView child)
    {
        if (child.Handler?.PlatformView is DuiWidget widget)
            widget.Dispose();
    }

    public void Clear()
    {
        if (VirtualView is null)
            return;

        foreach (var child in VirtualView.Children.Reverse())
            Remove(child);
    }

    void RealizeChild(IView child)
    {
        if (MauiContext is null)
            return;

        var platformContext = MauiContext.Services.GetService<DuiPlatformContext>();
        if (platformContext is null)
            return;

        using (platformContext.PushParent(PlatformView))
        {
            child.ToPlatform(MauiContext);
        }
    }

    public static void MapAdd(LayoutHandler handler, ILayout layout, object? arg)
    {
        if (arg is LayoutHandlerUpdate update)
            handler.Add(update.View);
    }

    public static void MapInsert(LayoutHandler handler, ILayout layout, object? arg)
    {
        if (arg is LayoutHandlerUpdate update)
            handler.Insert(update.Index, update.View);
    }

    public static void MapUpdate(LayoutHandler handler, ILayout layout, object? arg)
    {
        if (arg is LayoutHandlerUpdate update)
            handler.Update(update.Index, update.View);
    }

    public static void MapRemove(LayoutHandler handler, ILayout layout, object? arg)
    {
        if (arg is LayoutHandlerUpdate update)
            handler.Remove(update.View);
    }

    public static void MapClear(LayoutHandler handler, ILayout layout, object? arg)
        => handler.Clear();

    public static void MapClipsToBounds(LayoutHandler handler, ILayout layout)
    {
        // DUI clips its own containers; recorded explicitly so the intent is visible
        // when the toolkit attribute is confirmed.
        handler.PlatformView?.SetAttribute("clip", layout.ClipsToBounds ? "true" : "false");
    }
}
