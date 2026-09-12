namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// Waits for something another thread is about to do.
/// </summary>
/// <remarks>
/// The event stream is read on a task of its own — by the hub's forwarder, and by the page's own reader —
/// so "push an event and then assert" is a race a test would sometimes win. bUnit has
/// <c>WaitForAssertion</c> for a rendered component; this is the same idea for the fixtures that have no
/// component to wait on.
/// </remarks>
internal static class Eventually
{
    private static readonly TimeSpan waitFor = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Returns once <paramref name="condition" /> holds, and fails by timing out if it never does.
    /// </summary>
    public static async Task Until(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(waitFor);
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
