using Quartz.Impl;

namespace Quartz.Tests.AspNetCore.Support;

/// <summary>
/// Stands in for <c>HttpScheduler</c>: a proxy that says which target it stands behind and forwards
/// everything else to a fake, so a case about identity or cluster detection needs no host.
/// </summary>
internal sealed class FakeProxyScheduler : DelegatingScheduler, IProxyScheduler
{
    public FakeProxyScheduler(IScheduler inner, string? target, SchedulerOrigin origin = SchedulerOrigin.Remote) : base(inner)
    {
        Target = target;
        Origin = origin;
    }

    public string? Target { get; }

    public SchedulerOrigin Origin { get; }
}
