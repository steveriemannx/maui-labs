namespace Microsoft.Maui.Platforms.PolluxOS.DUI.Interop;

/// <summary>
/// Toolkit-level lifecycle: start/shut down DUI, run its message loop, and read
/// the bridge version. One DUI instance per process.
/// </summary>
/// <remarks>
/// DUI is single-threaded and its loop owns the UI. <see cref="Run"/> must be
/// called on the main thread (on macOS it also pre-warms the NSApplication run
/// loop) and blocks until the last window closes.
/// </remarks>
public static class DuiRuntime
{
    static bool s_started;

    /// <summary>Bridge version reported by the shim (not the DUI project version).</summary>
    public static string Version => DuiNative.Version();

    public static bool IsStarted => s_started;

    /// <summary>
    /// Starts DUI and loads its resources (themes/skins/lang) from
    /// <paramref name="resourceRoot"/>. DUI expects the same layout as the
    /// toolkit's own <c>resources/</c> directory, e.g.
    /// <c>&lt;root&gt;/themes/polluxos/&lt;skin&gt;/&lt;file&gt;.xml</c>.
    /// </summary>
    public static void Startup(string resourceRoot, string? locale = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceRoot);

        if (s_started)
            return;

        var result = DuiNative.dui_shim_startup(resourceRoot, locale);
        if (result != 0)
        {
            var error = DuiNative.TakeLastError();
            throw new InvalidOperationException(
                error is { Length: > 0 } ? error : $"Could not start DUI with resource root '{resourceRoot}'.");
        }

        s_started = true;
    }

    /// <summary>Runs the DUI message loop on the calling (UI) thread until quit.</summary>
    public static int Run()
    {
        EnsureStarted();
        return DuiNative.dui_shim_run();
    }

    /// <summary>Requests that the loop started by <see cref="Run"/> returns.</summary>
    public static void PostQuit(int exitCode = 0)
    {
        DuiNative.dui_shim_post_quit(exitCode);
    }

    /// <summary>Switches the toolkit theme. Not wired in this bridge revision.</summary>
    public static bool TrySetTheme(bool dark)
    {
        EnsureStarted();
        DuiNative.dui_shim_set_theme(dark ? 1 : 0);
        return string.IsNullOrEmpty(DuiNative.TakeLastError());
    }

    public static void Shutdown()
    {
        if (!s_started)
            return;

        DuiNative.dui_shim_shutdown();
        s_started = false;
    }

    static void EnsureStarted()
    {
        if (!s_started)
            throw new InvalidOperationException("DUI has not been started. Call DuiRuntime.Startup(resourceRoot) first.");
    }
}
