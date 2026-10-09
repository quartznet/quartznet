using Microsoft.AspNetCore.SignalR.Client;

using Quartz.Impl;

namespace Quartz.Tests.Unit.DashboardAgent;

/// <summary>
/// The agent's reconnection never gives up: at once, then two, ten and thirty seconds, and thirty seconds
/// for ever after.
/// </summary>
public class AgentRetryPolicyTest
{
    [Test]
    public void TheDelaysAreZeroTwoTenThirtyAndThenThirtyForEver()
    {
        List<TimeSpan?> delays = [];
        for (long attempt = 0; attempt < 8; attempt++)
        {
            delays.Add(AgentRetryPolicy.Instance.NextRetryDelay(new RetryContext
            {
                PreviousRetryCount = attempt,
                ElapsedTime = TimeSpan.FromSeconds(attempt * 30),
                RetryReason = new TimeoutException(),
            }));
        }

        delays.Should().Equal(
            [
                TimeSpan.Zero,
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(30),
            ],
            "a worker that outlives a dashboard restart has to be back on it however long that takes, which the client's own four-attempt policy does not give");
    }

    [Test]
    public void TheReconnectionBackoffDoublesFromASecondToHalfAMinute()
    {
        List<TimeSpan> waits = [];
        TimeSpan wait = Reconnection.FirstRetryDelay;
        for (int i = 0; i < 7; i++)
        {
            waits.Add(wait);
            wait = Reconnection.Next(wait);
        }

        waits.Should().Equal(
            [
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(4),
                TimeSpan.FromSeconds(8),
                TimeSpan.FromSeconds(16),
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(30),
            ],
            "the HTTP event reader and the agent dial on one rule");
    }
}
