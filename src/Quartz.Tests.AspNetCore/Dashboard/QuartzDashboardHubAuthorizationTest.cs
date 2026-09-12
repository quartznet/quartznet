using System.Security.Claims;

using FakeItEasy;

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Quartz.Dashboard.Hubs;
using Quartz.Dashboard.Services;
using Quartz.Extensibility;
using Quartz.Tests.AspNetCore.Dashboard.Support;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// The live-events hub's group join, which is the third way a dashboard reaches a scheduler: the forwarder
/// sends to a group named after the scheduler, so joining that group is subscribing to it.
/// </summary>
/// <remarks>
/// A connection is not a circuit — its caller is a <see cref="HubCallerContext" />, and the policy is
/// evaluated against that caller's principal rather than against the rendered dashboard's.
/// </remarks>
public class QuartzDashboardHubAuthorizationTest
{
    private const string SchedulerPolicyName = "SchedulerOwner";

    [Test]
    public async Task AConnectionMayJoinTheGroupOfASchedulerItPassesFor()
    {
        Joining joining = CreateHub(policyName: SchedulerPolicyName, allowed: "acme");

        await joining.Hub.JoinScheduler("acme");

        A.CallTo(() => joining.Groups.AddToGroupAsync("connection-1", "acme", A<CancellationToken>._)).MustHaveHappened();
        joining.Authorization.Asked.Should().Equal([(SchedulerPolicyName, new SchedulerResource("acme"))],
            "the hub asks the same policy about the same resource the pages and the API do");

        await Eventually.Until(() => joining.Events.Subscribed.Count > 0);
        joining.Events.Subscribed.Should().Equal(["acme"],
            "the join is what starts the forwarding: a hub nobody has connected to costs its schedulers nothing");
    }

    [Test]
    public async Task AConnectionIsRefusedTheGroupOfASchedulerItDoesNotPassFor()
    {
        Joining joining = CreateHub(policyName: SchedulerPolicyName, allowed: "acme");

        Func<Task> join = () => joining.Hub.JoinScheduler("globex");

        await join.Should().ThrowAsync<HubException>()
            .WithMessage("*globex*",
                "a refusal that silently did not join would look exactly like a scheduler with nothing to report");

        A.CallTo(() => joining.Groups.AddToGroupAsync(A<string>._, A<string>._, A<CancellationToken>._)).MustNotHaveHappened();

        await Task.Delay(100);
        joining.Events.Subscribed.Should().BeEmpty(
            "nothing is subscribed to on behalf of a caller who may not see it");
    }

    [Test]
    public async Task WithNoPolicyEveryGroupIsJoinedAndNothingIsAsked()
    {
        Joining joining = CreateHub(policyName: null, allowed: []);

        await joining.Hub.JoinScheduler("globex");

        A.CallTo(() => joining.Groups.AddToGroupAsync("connection-1", "globex", A<CancellationToken>._)).MustHaveHappened();
        joining.Authorization.Asked.Should().BeEmpty("an unset policy leaves the hub exactly as it was");
    }

    /// <summary>
    /// What a join is asserted against: the hub, the groups it manages, the policy it asked, and the stream
    /// the join starts forwarding from.
    /// </summary>
    private sealed record Joining(
        QuartzDashboardHub Hub,
        IGroupManager Groups,
        TestSchedulerAuthorizationService Authorization,
        FakeSchedulerEventSource Events);

    private static Joining CreateHub(string? policyName, params string[] allowed)
    {
        TestSchedulerAuthorizationService authorizationService = new();
        foreach (string schedulerName in allowed)
        {
            authorizationService.Allowed.Add(schedulerName);
        }

        SchedulerAuthorization authorization = new(
            Options.Create(new QuartzDashboardOptions { SchedulerAuthorizationPolicy = policyName }),
            authorizationService,
            new TestAuthenticationStateProvider());

        HubCallerContext callerContext = A.Fake<HubCallerContext>();
        A.CallTo(() => callerContext.ConnectionId).Returns("connection-1");
        A.CallTo(() => callerContext.User).Returns(new ClaimsPrincipal(new ClaimsIdentity("test")));

        IGroupManager groups = A.Fake<IGroupManager>();

        // A forwarder over a stream nothing publishes into: the join is what starts it, which is a fact this
        // fixture asserts and which must not need a scheduler to be true.
        FakeSchedulerEventSource events = new();
        ServiceCollection services = new();
        services.AddSingleton<ISchedulerEventSource>(events);
        ServiceProvider provider = services.BuildServiceProvider();

        QuartzDashboardHub hub = new(
            authorization,
            new DashboardHubForwarder(provider, NullLogger<DashboardHubForwarder>.Instance),
            NullLogger<QuartzDashboardHub>.Instance)
        {
            Context = callerContext,
            Groups = groups
        };

        return new Joining(hub, groups, authorizationService, events);
    }
}
