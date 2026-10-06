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

    protected override DuiWidget CreatePlatformView()
    {
        // Wrap the container so MAUI's cross-platform layout can drive it: the panel
        // forwards CrossPlatformMeasure/Arrange to the virtual layout, whose cross-platform
        // engine then arranges every child (each child handler's PlatformArrange sets the
        // DUI rectangle).
        var widget = CreateDuiWidget();
        return VirtualView is ICrossPlatformLayout crossPlatformLayout
            ? new DuiLayoutPanel(widget, crossPlatformLayout)
            : widget;
    }

    public override Size GetDesiredSize(double widthConstraint, double heightConstraint)
        => VirtualView is ICrossPlatformLayout crossPlatformLayout
            ? crossPlatformLayout.CrossPlatformMeasure(widthConstraint, heightConstraint)
            : base.GetDesiredSize(widthConstraint, heightConstraint);

    public override void PlatformArrange(Rect rect)
    {
        // Place the container itself, then let MAUI arrange its children inside it.
        base.PlatformArrange(rect);

        if (VirtualView is ICrossPlatformLayout crossPlatformLayout)
            crossPlatformLayout.CrossPlatformArrange(new Rect(0, 0, rect.Width, rect.Height));
    }

    // ILayout (the platform-neutral interface) exposes no child collection — children
    // arrive through the ILayoutHandler commands — so the handler tracks what it added.
    readonly List<DuiWidget> _children = new();

    protected override void ConnectHandler(DuiWidget platformView)
    {
        base.ConnectHandler(platformView);

        // Children that already existed when the handler was created never raise an
        // ILayoutHandler.Add command, so realize them here (the concrete Layout type
        // exposes them; ILayout itself does not).
        if (VirtualView is Microsoft.Maui.Controls.Layout layout)
        {
            foreach (var child in layout.Children)
                RealizeChild((IView)child);
        }
    }

    public void Add(IView child) => RealizeChild(child);

    public void Insert(int index, IView child)
    {
        // TODO: DUI containers have no positional insert; ordering is currently
        // append-only. Revisit when the bridge exposes one.
        RealizeChild(child);
    }

    public void Update(int index, IView child)
    {
        Remove(child);
        RealizeChild(child);
    }

    public void Remove(IView child)
    {
        if (child.Handler?.PlatformView is not DuiWidget widget)
            return;

        _children.Remove(widget);
        widget.Dispose();
    }

    public void Clear()
    {
        foreach (var child in _children.ToArray())
            child.Dispose();

        _children.Clear();
    }

    void RealizeChild(IView child)
    {
        if (MauiContext is null)
            return;

        var platformContext = MauiContext.Services.GetService<DuiPlatformContext>();
        if (platformContext is null)
            return;

        DuiWidget? platformView;
        using (platformContext.PushParent(PlatformView))
        {
            // ToHandler realizes the child's handler; its DUI control is created under
            // this container because that container is on the parent stack.
            platformView = child.ToHandler(MauiContext)?.PlatformView as DuiWidget;
        }

        if (platformView is not null && !_children.Contains(platformView))
            _children.Add(platformView);
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
