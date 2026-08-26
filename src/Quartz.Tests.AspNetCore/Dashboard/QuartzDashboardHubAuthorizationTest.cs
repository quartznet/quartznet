using System.Security.Claims;

using FakeItEasy;

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

using Quartz.Dashboard.Hubs;
using Quartz.Dashboard.Services;
using Quartz.Tests.AspNetCore.Dashboard.Support;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// The live-events hub's group join, which is the third way a dashboard reaches a scheduler: the plugin
/// broadcasts to a group named after the scheduler, so joining that group is subscribing to it.
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
        (QuartzDashboardHub hub, IGroupManager groups, TestSchedulerAuthorizationService authorization) =
            CreateHub(policyName: SchedulerPolicyName, allowed: "acme");

        await hub.JoinScheduler("acme");

        A.CallTo(() => groups.AddToGroupAsync("connection-1", "acme", A<CancellationToken>._)).MustHaveHappened();
        authorization.Asked.Should().Equal([(SchedulerPolicyName, new SchedulerResource("acme"))],
            "the hub asks the same policy about the same resource the pages and the API do");
    }

    [Test]
    public async Task AConnectionIsRefusedTheGroupOfASchedulerItDoesNotPassFor()
    {
        (QuartzDashboardHub hub, IGroupManager groups, _) =
            CreateHub(policyName: SchedulerPolicyName, allowed: "acme");

        Func<Task> join = () => hub.JoinScheduler("globex");

        await join.Should().ThrowAsync<HubException>()
            .WithMessage("*globex*",
                "a refusal that silently did not join would look exactly like a scheduler with nothing to report");

        A.CallTo(() => groups.AddToGroupAsync(A<string>._, A<string>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task WithNoPolicyEveryGroupIsJoinedAndNothingIsAsked()
    {
        (QuartzDashboardHub hub, IGroupManager groups, TestSchedulerAuthorizationService authorization) =
            CreateHub(policyName: null, allowed: []);

        await hub.JoinScheduler("globex");

        A.CallTo(() => groups.AddToGroupAsync("connection-1", "globex", A<CancellationToken>._)).MustHaveHappened();
        authorization.Asked.Should().BeEmpty("an unset policy leaves the hub exactly as it was");
    }

    private static (QuartzDashboardHub Hub, IGroupManager Groups, TestSchedulerAuthorizationService Authorization) CreateHub(
        string? policyName,
        params string[] allowed)
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

        QuartzDashboardHub hub = new(authorization)
        {
            Context = callerContext,
            Groups = groups
        };

        return (hub, groups, authorizationService);
    }
}
