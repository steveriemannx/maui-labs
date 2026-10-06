using Microsoft.Maui.Platforms.PolluxOS.DUI.Interop;

namespace Microsoft.Maui.Platforms.PolluxOS.DUI.Platform;

/// <summary>
/// Carries the DUI widget that new handler-created controls should be parented to.
/// </summary>
/// <remarks>
/// DUI creates a control only when it is given a parent container, so the backend
/// tracks a parent stack: the window handler establishes the root, and each layout
/// handler pushes its own container while MAUI realizes its children through
/// <c>ILayoutHandler.Add</c>. Handlers read the current parent from their
/// <c>MauiContext</c> services.
/// </remarks>
public sealed class DuiPlatformContext
{
    readonly Stack<DuiWidget> _parents = new();

    public DuiWindow? Window { get; private set; }

    public DuiWidget? Root { get; private set; }

    public event EventHandler? Changed;

    public void SetWindow(DuiWindow window)
    {
        Window = window;
        Root = window.Root;
        _parents.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        Window = null;
        Root = null;
        _parents.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Root container of the active window.</summary>
    public DuiWidget RequireRoot()
        => Root ?? throw new InvalidOperationException(
            "No active DUI window. Create the MAUI Window before its handlers build platform views.");

    /// <summary>The container a new platform view belongs to: the innermost layout,
    /// or the window root when no layout is being realized.</summary>
    public DuiWidget RequireParent() => _parents.Count > 0 ? _parents.Peek() : RequireRoot();

    /// <summary>Makes <paramref name="container"/> the parent for children realized
    /// until the returned scope is disposed.</summary>
    public IDisposable PushParent(DuiWidget container)
    {
        _parents.Push(container);
        return new ParentScope(_parents);
    }

    sealed class ParentScope : IDisposable
    {
        readonly Stack<DuiWidget> _parents;
        bool _disposed;

        internal ParentScope(Stack<DuiWidget> parents) => _parents = parents;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_parents.Count > 0)
                _parents.Pop();
        }
    }
}
