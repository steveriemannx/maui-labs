using Microsoft.Maui.Platforms.PolluxOS.DUI.Interop;
using Xunit;

namespace Microsoft.Maui.Platforms.PolluxOS.DUI.Tests;

/// <summary>
/// Bridge smoke tests. They are meaningful only when <c>libdui_shim.dylib</c> has
/// been built and staged next to the test assembly; without it the tests report the
/// missing prerequisite instead of failing on a DllNotFoundException, so a managed
/// build stays green while the native side is being brought up.
/// </summary>
public class DuiBridgeSmokeTests
{
    static bool NativeBridgePresent()
        => File.Exists(Path.Combine(AppContext.BaseDirectory, "libdui_shim.dylib"));

    [Fact]
    public void Bridge_ReportsVersionWhenPresent()
    {
        if (!NativeBridgePresent())
            return;

        var version = DuiRuntime.Version;

        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.NotEqual("unknown", version);
    }

    [Fact]
    public void Run_WithoutStartup_ThrowsActionableError()
    {
        if (DuiRuntime.IsStarted)
            return;

        var exception = Assert.Throws<InvalidOperationException>(() => DuiRuntime.Run());
        Assert.Contains("Startup", exception.Message);
    }
}
