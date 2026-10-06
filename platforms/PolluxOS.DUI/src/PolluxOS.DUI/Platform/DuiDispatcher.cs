using System.Collections.Concurrent;
using Microsoft.Maui.Dispatching;

namespace Microsoft.Maui.Platforms.PolluxOS.DUI.Platform;

/// <summary>
/// MAUI dispatcher for the DUI backend.
/// </summary>
/// <remarks>
/// DUI is single-threaded and owns its own UI thread, so "the UI thread" is the thread
/// the toolkit calls its idle handler on. Work is queued here and drained from that
/// handler (registered by <see cref="DuiDispatcherProvider"/> / <c>UsePolluxOSDui</c>);
/// on any other thread the call is queued and reported as requiring dispatch.
///
/// No platform-specific API is involved, which is what lets the backend target a
/// platform-neutral TFM and run on FreeBSD/polluxos as well as macOS.
/// </remarks>
public class DuiDispatcher : IDispatcher
{
    static readonly ConcurrentQueue<Action> s_queue = new();

    [ThreadStatic]
    static bool t_onUiThread;

    static DuiDispatcher? s_current;
    static nint s_idleCallbackPointer;

    /// <summary>The process-wide dispatcher (one DUI instance per process).</summary>
    public static DuiDispatcher Current => s_current ??= new DuiDispatcher();

    public static IDispatcher? GetForCurrentThread() => Current;

    /// <summary>True while running inside DUI's idle callback.</summary>
    internal static bool IsOnUiThread => t_onUiThread;

    internal static void DrainQueue()
    {
        t_onUiThread = true;
        try
        {
            while (s_queue.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"PolluxOS.DUI: dispatched action threw: {ex}");
                }
            }
        }
        finally
        {
            t_onUiThread = false;
        }
    }

    internal static nint IdleCallbackPointer
    {
        get => s_idleCallbackPointer;
        set => s_idleCallbackPointer = value;
    }

    public bool IsDispatchRequired => !t_onUiThread;

    public IDispatcherTimer CreateTimer() => new DuiDispatcherTimer();

    public bool Dispatch(Action action)
    {
        s_queue.Enqueue(action);
        return true;
    }

    public bool DispatchDelayed(TimeSpan delay, Action action)
    {
        // A pooled timer can be noisy; one thread per delayed dispatch is fine for a
        // bring-up backend and keeps the queue the only shared state.
        var timer = new Timer(_ => s_queue.Enqueue(action), null, delay, Timeout.InfiniteTimeSpan);
        _ = timer;
        return true;
    }
}

public class DuiDispatcherProvider : IDispatcherProvider
{
    public IDispatcher? GetForCurrentThread() => DuiDispatcher.GetForCurrentThread();
}

/// <summary>Timer whose ticks are delivered through the dispatcher queue, so handlers
/// run on the toolkit's UI thread.</summary>
public class DuiDispatcherTimer : IDispatcherTimer
{
    readonly DuiDispatcher _dispatcher = DuiDispatcher.Current;
    Timer? _timer;
    TimeSpan _interval = TimeSpan.FromMilliseconds(16);

    public TimeSpan Interval
    {
        get => _interval;
        set
        {
            _interval = value;
            if (IsRunning)
            {
                Stop();
                Start();
            }
        }
    }

    public bool IsRepeating { get; set; } = true;

    public bool IsRunning { get; private set; }

    public event EventHandler? Tick;

    public void Start()
    {
        if (IsRunning)
            return;

        IsRunning = true;
        _timer = new Timer(_ =>
        {
            if (!IsRunning)
                return;

            _dispatcher.Dispatch(() => Tick?.Invoke(this, EventArgs.Empty));

            if (!IsRepeating)
                Stop();
        }, null, _interval, IsRepeating ? _interval : Timeout.InfiniteTimeSpan);
    }

    public void Stop()
    {
        IsRunning = false;
        _timer?.Dispose();
        _timer = null;
    }
}
