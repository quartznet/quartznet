#nullable enable

using FakeItEasy;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Quartz.Tests.Unit.HealthChecks;

/// <summary>
/// What the health check reports for a node that routes execution groups, about the groups it does not
/// serve (#4069).
/// </summary>
/// <remarks>
/// <para>
/// The trap in routing: a trigger of a group no live node serves is acquired by nobody and, because a
/// node's misfire sweep handles only its own groups, re-armed by nobody. It waits, unmodified, which from
/// any node is silence. The check turns that silence into a finding by asking for schedulable triggers
/// overdue by more than a few misfire thresholds and naming the groups among them this node does not
/// serve.
/// </para>
/// <para>
/// The scheduler is a fake whose metadata reports the routing and whose trigger listing answers what
/// each case needs, so the assertions are about the check's reading of them. No store is in the
/// container, so the threshold is one minute — the database store's default.
/// </para>
/// </remarks>
public class UnservedExecutionGroupHealthCheckTest
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task AnOverdueTriggerOfAGroupThisNodeDoesNotServeIsDegradedAndNamesTheGroup()
    {
        FakeTimeProvider clock = new(Now);
        IScheduler scheduler = RoutingScheduler(clock, served: ["reports"]);
        GivenOverdue(scheduler, Header("gpu-1", "gpu", Now - TimeSpan.FromMinutes(7)), Header("gpu-2", "gpu", Now - TimeSpan.FromMinutes(4)), Header("plain", null, Now - TimeSpan.FromMinutes(5)));

        await using ServiceProvider provider = Container(scheduler, _ => { });
        HealthReportEntry result = await Run(provider);

        result.Status.Should().Be(HealthStatus.Degraded,
            "three thresholds late in a group this node does not serve means no serving node's sweep has reached it, which is the trap "
            + "routing sets; degraded rather than unhealthy, because the reporting node is fine");
        result.Description.Should().Contain("3 trigger(s) in execution groups it does not serve")
            .And.Contain("'gpu' (2)")
            .And.Contain("'_' (1)", "the ungrouped bucket is named the way configuration spells it")
            .And.Contain("420s ago", "the oldest is what says how long nobody has served the group")
            .And.Contain("3 × its 60s misfire threshold");
        result.Data.Should().ContainKey("unservedExecutionGroups")
            .WhoseValue.Should().BeEquivalentTo(new Dictionary<string, int> { ["gpu"] = 2, ["_"] = 1 });
        result.Data.Should().ContainKey("oldestUnservedDueUtc").WhoseValue.Should().Be(Now - TimeSpan.FromMinutes(7));
        result.Data.Should().ContainKey("misfireThreshold").WhoseValue.Should().Be(TimeSpan.FromMinutes(1));
    }

    [Test]
    public async Task ATriggerNotYetThreeThresholdsLateIsHealthy()
    {
        FakeTimeProvider clock = new(Now);
        IScheduler scheduler = RoutingScheduler(clock, served: ["reports"]);

        // The store is asked for triggers due before now - 3 min and answers with one that is not;
        // the check re-reads the answer rather than trusting it.
        GivenOverdue(scheduler, Header("gpu-1", "gpu", Now - TimeSpan.FromMinutes(2)));

        await using ServiceProvider provider = Container(scheduler, _ => { });

        (await Run(provider)).Status.Should().Be(HealthStatus.Healthy,
            "two thresholds is inside the window a serving node's sweep has to reach a misfired trigger in");
    }

    [Test]
    public async Task AnOverdueTriggerOfAServedGroupIsNotReportedAsUnserved()
    {
        FakeTimeProvider clock = new(Now);
        IScheduler scheduler = RoutingScheduler(clock, served: ["reports"]);
        GivenOverdue(scheduler, Header("reports-1", "reports", Now - TimeSpan.FromMinutes(7)));

        await using ServiceProvider provider = Container(scheduler, _ => { });

        (await Run(provider)).Status.Should().Be(HealthStatus.Healthy,
            "the unserved reading is about the groups this node does not serve; a served trigger that is late is the stall reading's business, "
            + "which is off by default");
    }

    [TestCase(0d)]
    public async Task AZeroToleranceTurnsTheReadingOff(double tolerance)
    {
        FakeTimeProvider clock = new(Now);
        IScheduler scheduler = RoutingScheduler(clock, served: ["reports"]);
        GivenOverdue(scheduler, Header("gpu-1", "gpu", Now - TimeSpan.FromHours(1)));

        await using ServiceProvider provider = Container(scheduler, options => options.UnservedExecutionGroupTolerance = tolerance);

        (await Run(provider)).Status.Should().Be(HealthStatus.Healthy);

        // Off means no query rather than a query whose answer is ignored.
        A.CallTo(() => scheduler.QueryTriggers(A<TriggerQuery>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task ANegativeToleranceIsRefusedAtStartup()
    {
        FakeTimeProvider clock = new(Now);
        IScheduler scheduler = RoutingScheduler(clock, served: ["reports"]);

        await using ServiceProvider provider = Container(scheduler, options => options.UnservedExecutionGroupTolerance = -1);

        Action act = () => provider.GetRequiredService<IOptionsMonitor<QuartzHealthCheckOptions>>().Get(Options.DefaultName);

        act.Should().Throw<OptionsValidationException>().WithMessage("*UnservedExecutionGroupTolerance must not be negative*");
    }

    [Test]
    public async Task ANodeThatRoutesNothingMakesNoQuery()
    {
        FakeTimeProvider clock = new(Now);
        IScheduler scheduler = RoutingScheduler(clock, served: []);
        GivenOverdue(scheduler, Header("gpu-1", "gpu", Now - TimeSpan.FromHours(1)));

        await using ServiceProvider provider = Container(scheduler, _ => { });

        (await Run(provider)).Status.Should().Be(HealthStatus.Healthy);

        // A node that serves every group has no unserved group to look for, and must not pay for the
        // question on every probe.
        A.CallTo(() => scheduler.QueryTriggers(A<TriggerQuery>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    /// <summary>
    /// The stall reading, on a node that routes, looks only at the groups the node serves: an overdue
    /// trigger of another group is the unserved reading's finding, not a stall of this node.
    /// </summary>
    [Test]
    public async Task TheStallReadingOnARoutingNodeIgnoresTheGroupsItDoesNotServe()
    {
        FakeTimeProvider clock = new(Now);
        IScheduler scheduler = RoutingScheduler(clock, served: ["reports"]);
        GivenOverdue(scheduler, Header("gpu-1", "gpu", Now - TimeSpan.FromHours(1)));

        await using ServiceProvider provider = Container(scheduler, options => options.StaleFiringTolerance = 3);
        HealthReportEntry result = await Run(provider);

        result.Status.Should().Be(HealthStatus.Degraded, "the unserved reading reports the gpu trigger");
        result.Description.Should().Contain("does not serve")
            .And.NotContain("has not fired trigger", "an hour late would be an unhealthy stall if the trigger were this node's to fire, and it is not");
    }

    [Test]
    public async Task TheStallReadingOnARoutingNodeStillReportsAServedTrigger()
    {
        FakeTimeProvider clock = new(Now);
        IScheduler scheduler = RoutingScheduler(clock, served: ["reports"]);
        GivenOverdue(scheduler, Header("gpu-1", "gpu", Now - TimeSpan.FromHours(1)), Header("reports-1", "reports", Now - TimeSpan.FromHours(1)));

        await using ServiceProvider provider = Container(scheduler, options => options.StaleFiringTolerance = 3);
        HealthReportEntry result = await Run(provider);

        result.Status.Should().Be(HealthStatus.Unhealthy, "the served trigger is twice the tolerance late, which is the stall reading's graver bar");
        result.Description.Should().Contain("has not fired trigger 'routing.reports-1'",
            "the first overdue trigger the store listed is not this node's; the stall is reported on the first one that is");
    }

    private static IScheduler RoutingScheduler(TimeProvider clock, string[] served)
    {
        IScheduler scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.SchedulerName).Returns("core");
        A.CallTo(() => scheduler.Status).Returns(SchedulerStatus.Running);
        A.CallTo(() => scheduler.TimeProvider).Returns(clock);
        A.CallTo(() => scheduler.GetMetadata(A<CancellationToken>._)).Returns(new SchedulerMetadata
        {
            SchedulerName = "core",
            SchedulerInstanceId = "one",
            SchedulerTypeName = "Quartz.Core.QuartzScheduler",
            JobStoreTypeName = "Acme.ExoticJobStore",
            ThreadPoolTypeName = "Quartz.Impl.DefaultThreadPool",
            Status = SchedulerStatus.Running,
            JobStoreClustered = false,
            Version = "4.6.0.0",
            ServedExecutionGroups = served,
        });
        A.CallTo(() => scheduler.QueryTriggers(A<TriggerQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<TriggerHeader>([], HasMore: false));

        return scheduler;
    }

    private static void GivenOverdue(IScheduler scheduler, params TriggerHeader[] headers)
    {
        A.CallTo(() => scheduler.QueryTriggers(A<TriggerQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<TriggerHeader>(headers, HasMore: false));
    }

    private static TriggerHeader Header(string name, string? executionGroup, DateTimeOffset due) => new(
        new TriggerKey(name, "routing"),
        new JobKey("job", "routing"),
        Description: null,
        TriggerType: "SIMPLE",
        State: TriggerState.Normal,
        StartTimeUtc: due,
        EndTimeUtc: null,
        NextFireTimeUtc: due,
        PreviousFireTimeUtc: null,
        CalendarName: null,
        Priority: 5,
        ExecutionGroup: executionGroup,
        RetryPolicy: null,
        RetryAttempt: 0);

    private static ServiceProvider Container(IScheduler scheduler, Action<QuartzHealthCheckOptions> configure)
    {
        ISchedulerFactory factory = A.Fake<ISchedulerFactory>();
        A.CallTo(() => factory.GetScheduler(A<CancellationToken>._)).Returns(new ValueTask<IScheduler>(scheduler));

        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(factory);
        services.AddHealthChecks().AddQuartz(configure);

        return services.BuildServiceProvider();
    }

    private static async Task<HealthReportEntry> Run(ServiceProvider provider)
    {
        HealthReport report = await provider.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(registration => registration.Name == "quartz-scheduler");

        return report.Entries["quartz-scheduler"];
    }
}
