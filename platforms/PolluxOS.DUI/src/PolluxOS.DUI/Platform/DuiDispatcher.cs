using CoreFoundation;
using Foundation;
using Microsoft.Maui.Dispatching;

namespace Microsoft.Maui.Platforms.PolluxOS.DUI.Platform;

/// <summary>
/// MAUI dispatcher for the DUI backend.
/// </summary>
/// <remarks>
/// DUI owns the main thread's run loop (its macOS backend runs on NSApplication), so
/// "the UI thread" and the platform main queue are the same thing. Dispatching to
/// <see cref="DispatchQueue.MainQueue"/> therefore lands on the thread DUI expects.
/// </remarks>
public class DuiDispatcher : IDispatcher
{
    public static IDispatcher? GetForCurrentThread()
    {
        if (NSThread.IsMain)
            return new DuiDispatcher();
        return null;
    }

    public bool IsDispatchRequired => !NSThread.IsMain;

    public IDispatcherTimer CreateTimer() => new DuiDispatcherTimer();

    public bool Dispatch(Action action)
    {
        DispatchQueue.MainQueue.DispatchAsync(action);
        return true;
    }

    public bool DispatchDelayed(TimeSpan delay, Action action)
    {
        DispatchQueue.MainQueue.DispatchAfter(new DispatchTime(DispatchTime.Now, delay), action);
        return true;
    }
}

public class DuiDispatcherProvider : IDispatcherProvider
{
    public IDispatcher? GetForCurrentThread() => DuiDispatcher.GetForCurrentThread();
}

/// <summary>Timer backed by the main run loop, so ticks arrive on the DUI UI thread.</summary>
public class DuiDispatcherTimer : IDispatcherTimer
{
    readonly object _gate = new();
    NSTimer? _timer;
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
        var interval = Math.Max(0.001, _interval.TotalSeconds);
        _timer = NSTimer.CreateRepeatingScheduledTimer(interval, _ =>
        {
            Tick?.Invoke(this, EventArgs.Empty);
            if (!IsRepeating)
                Stop();
        });
    }

    public void Stop()
    {
        lock (_gate)
        {
            IsRunning = false;
            _timer?.Invalidate();
            _timer?.Dispose();
            _timer = null;
        }
    }
}
