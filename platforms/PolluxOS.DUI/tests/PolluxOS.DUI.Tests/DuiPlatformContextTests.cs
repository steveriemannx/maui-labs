using Microsoft.Maui.Platforms.PolluxOS.DUI.Platform;
using Xunit;

namespace Microsoft.Maui.Platforms.PolluxOS.DUI.Tests;

/// <summary>
/// Managed-only behaviour of <see cref="DuiPlatformContext"/>. These tests need no
/// native bridge: they cover the parent-stack contract handlers rely on, and the
/// failure mode when a handler runs before a window exists.
/// </summary>
public class DuiPlatformContextTests
{
    [Fact]
    public void RequireRoot_ThrowsBeforeAnyWindowExists()
    {
        var context = new DuiPlatformContext();

        var exception = Assert.Throws<InvalidOperationException>(() => context.RequireRoot());
        Assert.Contains("No active DUI window", exception.Message);
    }

    [Fact]
    public void RequireParent_ThrowsBeforeAnyWindowExists()
    {
        var context = new DuiPlatformContext();

        Assert.Throws<InvalidOperationException>(() => context.RequireParent());
    }

    [Fact]
    public void Clear_IsIdempotent()
    {
        var context = new DuiPlatformContext();

        context.Clear();
        context.Clear();

        Assert.Null(context.Window);
        Assert.Null(context.Root);
    }

    [Fact]
    public void Changed_FiresOnClear()
    {
        var context = new DuiPlatformContext();
        var raised = 0;
        context.Changed += (_, _) => raised++;

        context.Clear();

        Assert.Equal(1, raised);
    }
}
