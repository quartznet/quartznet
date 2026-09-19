#nullable enable

using FakeItEasy;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Time.Testing;

using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.HealthChecks;

/// <summary>
/// What the health check reports for a scheduler that has stopped firing although something is due.
/// </summary>
/// <remarks>
/// <para>
/// The silent stall is the one failure the rest of the check cannot see: the scheduler under test here
/// is <see cref="SchedulerStatus.Running" /> and its store answers every question, which is all the
/// check ever asserted, while nothing at all is leaving its queue. It is modelled by a store whose
/// acquisition hands the scheduler nothing — a wedged acquisition query, a lock nobody releases and a
/// thread pool with nothing free all look exactly like this from outside.
/// </para>
/// <para>
/// "Overdue" is on the scheduler's own clock, so every test here moves a <see cref="FakeTimeProvider" />
/// rather than waiting, and the store's misfire threshold is set explicitly so no assertion depends on
/// which default it happens to carry.
/// </para>
/// </remarks>
public class StaleFiringHealthCheckTest
{
    /// <summary>The store's own definition of "late", which the tolerance multiplies.</summary>
    private static readonly TimeSpan MisfireThreshold = TimeSpan.FromSeconds(10);

    [Test]
    public async Task ASchedulerThatHasStoppedFiringIsDegraded()
    {
        await using StalledScheduler stalled = await StalledScheduler.Create(options => options.StaleFiringTolerance = 3);

        // Three ten-second thresholds is thirty seconds of grace; forty is past it and not yet twice it.
        stalled.Clock.Advance(TimeSpan.FromSeconds(40));

        HealthReportEntry result = await stalled.Check();

        result.Status.Should().Be(
            HealthStatus.Degraded,
            "a trigger later than lateness is defined to be says work is not leaving the queue, which is the one "
            + "thing a running scheduler whose store answers could not report");
        result.Description.Should().Contain("overdue-trigger").And.Contain("misfire threshold");
    }

    /// <summary>
    /// The report names the trigger and the instant, because "something is overdue" is not something an
    /// operator can act on and "<c>stalled.overdue-trigger</c>, due at 12:00" is.
    /// </summary>
    [Test]
    public async Task TheReportCarriesTheOverdueTriggerAndWhenItWasDue()
    {
        await using StalledScheduler stalled = await StalledScheduler.Create(options => options.StaleFiringTolerance = 3);
        DateTimeOffset due = stalled.Clock.GetUtcNow();
        stalled.Clock.Advance(TimeSpan.FromSeconds(40));

        HealthReportEntry result = await stalled.Check();

        result.Data.Should().ContainKey("overdueTrigger")
            .WhoseValue.Should().Be("stalled.overdue-trigger");
        result.Data.Should().ContainKey("overdueSince")
            .WhoseValue.Should().Be(due, "the instant the trigger was due is how long the stall has lasted");
        result.Data.Should().ContainKey("misfireThreshold")
            .WhoseValue.Should().Be(MisfireThreshold, "the verdict is a multiple of it, so the report shows it");
    }

    [Test]
    public async Task ASchedulerTwiceAsFarBehindIsUnhealthy()
    {
        await using StalledScheduler stalled = await StalledScheduler.Create(options => options.StaleFiringTolerance = 3);

        // Past six thresholds rather than three.
        stalled.Clock.Advance(TimeSpan.FromSeconds(70));

        HealthReportEntry result = await stalled.Check();

        result.Status.Should().Be(
            HealthStatus.Unhealthy,
            "a backlog that keeps growing has stopped being a delay, so the second bar takes the node out of "
            + "rotation rather than merely flagging it");
        result.Description.Should().Contain("6 ×", "the message says which bar was crossed");
    }

    [Test]
    public async Task ASchedulerThatIsMerelyALittleLateIsHealthy()
    {
        await using StalledScheduler stalled = await StalledScheduler.Create(options => options.StaleFiringTolerance = 3);

        stalled.Clock.Advance(TimeSpan.FromSeconds(25));

        HealthReportEntry result = await stalled.Check();

        result.Status.Should().Be(
            HealthStatus.Healthy,
            "one threshold late is ordinary scheduling lateness, and on the database store it is also the window "
            + "the misfire handler has to notice one - which is why the tolerance multiplies rather than equals it");
    }

    /// <summary>
    /// The reading is off unless a deployment asks for it, and off means no query rather than a query
    /// whose answer is ignored.
    /// </summary>
    [Test]
    public async Task TheReadingIsOffByDefaultAndCostsNoQuery()
    {
        await using StalledScheduler stalled = await StalledScheduler.Create(configure: null);
        stalled.Clock.Advance(TimeSpan.FromHours(1));

        HealthReportEntry result = await stalled.Check();

        result.Status.Should().Be(HealthStatus.Healthy, "an hour behind is invisible until a tolerance is set");
        stalled.Store.TriggerQueries.Should().Be(
            0,
            "what counts as overdue is the application's to say, so a scheduler that never asked must not pay "
            + "for the question on every probe");
    }

    [TestCase(0d)]
    [TestCase(-1d)]
    public async Task ANonPositiveToleranceTurnsTheReadingOff(double tolerance)
    {
        await using StalledScheduler stalled = await StalledScheduler.Create(options => options.StaleFiringTolerance = tolerance);
        stalled.Clock.Advance(TimeSpan.FromHours(1));

        HealthReportEntry result = await stalled.Check();

        result.Status.Should().Be(HealthStatus.Healthy);
        stalled.Store.TriggerQueries.Should().Be(
            0,
            "zero and null mean the same thing, so a deployment can turn the reading off by binding a number");
    }

    /// <summary>
    /// A scheduler whose triggers were all paused is doing what it was told, so it reports as it always
    /// has — and it does so because a paused trigger is not in <see cref="TriggerState.Normal" />, not
    /// because of a second rule that could drift from the first.
    /// </summary>
    [Test]
    public async Task PausingEverythingIsNotAStall()
    {
        await using StalledScheduler stalled = await StalledScheduler.Create(options => options.StaleFiringTolerance = 3);
        await stalled.Scheduler.PauseAll();

        stalled.Clock.Advance(TimeSpan.FromHours(1));

        HealthReportEntry result = await stalled.Check();

        result.Status.Should().Be(
            HealthStatus.Healthy,
            "a paused trigger is not due to fire, so an operator who paused the scheduler has not broken it");
    }

    /// <summary>
    /// Standby is answered before anything is asked of the store, so a node that was deliberately taken
    /// out of rotation reports exactly what it reported before this reading existed.
    /// </summary>
    [Test]
    public async Task ASchedulerInStandbyReportsStandbyAndIsNotAskedAboutOverdueTriggers()
    {
        await using StalledScheduler stalled = await StalledScheduler.Create(options => options.StaleFiringTolerance = 3);
        await stalled.Scheduler.Standby();
        stalled.Clock.Advance(TimeSpan.FromHours(1));
        stalled.Store.ResetCounters();

        HealthReportEntry result = await stalled.Check();

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("standby");
        stalled.Store.TriggerQueries.Should().Be(
            0,
            "standby is deliberate, and a deliberately idle scheduler must not be reported as a stalled one");
    }

    /// <summary>
    /// The unit is the store's own misfire threshold, read off the store the scheduler is running rather
    /// than off a constant, so raising it raises the bar with it.
    /// </summary>
    [Test]
    public async Task TheBarMovesWithTheStoresOwnMisfireThreshold()
    {
        await using StalledScheduler generous = await StalledScheduler.Create(
            options => options.StaleFiringTolerance = 3,
            misfireThreshold: TimeSpan.FromMinutes(5));

        generous.Clock.Advance(TimeSpan.FromSeconds(40));

        HealthReportEntry result = await generous.Check();

        result.Status.Should().Be(
            HealthStatus.Healthy,
            "forty seconds is four thresholds on a ten-second store and a fraction of one on a five-minute store, "
            + "so the verdict has to come from the store rather than from a number the check picked");
    }

    /// <summary>
    /// A scheduler that is keeping up is asked once; one that is not is asked a second time, because the
    /// store answers with whichever overdue trigger it listed first and that one alone cannot say whether
    /// anything is past the worse bar.
    /// </summary>
    [Test]
    public async Task AHealthySchedulerIsAskedExactlyOnce()
    {
        await using StalledScheduler stalled = await StalledScheduler.Create(options => options.StaleFiringTolerance = 3);
        stalled.Store.ResetCounters();

        (await stalled.Check()).Status.Should().Be(HealthStatus.Healthy);

        stalled.Store.TriggerQueries.Should().Be(
            1,
            "the second query only runs once the first has found something, so the ordinary probe costs one "
            + "indexed read rather than two");
    }

    /// <summary>
    /// The store answers with whichever overdue trigger it lists first, which is not necessarily the
    /// worst one — so the verdict cannot be read off that one alone.
    /// </summary>
    /// <remarks>
    /// Here the first trigger by group and name is the mildly overdue one and the badly overdue one
    /// sorts after it. Taking the first answer at face value would report degraded and name the wrong
    /// trigger; the second query is what makes the verdict the worst case rather than the first case.
    /// </remarks>
    [Test]
    public async Task TheVerdictIsTheWorstOverdueTriggerRatherThanTheFirstOneListed()
    {
        await using StalledScheduler stalled = await StalledScheduler.Create(options => options.StaleFiringTolerance = 3);

        // "overdue-trigger" was due when the fixture built it. Forty seconds on, a second trigger is
        // scheduled for that moment - it sorts first by name and is the less overdue of the two.
        stalled.Clock.Advance(TimeSpan.FromSeconds(40));
        await stalled.Schedule("a-mild-one");

        // Forty more: the mild one is four thresholds behind, the original eight.
        stalled.Clock.Advance(TimeSpan.FromSeconds(40));
        stalled.Store.ResetCounters();

        HealthReportEntry result = await stalled.Check();

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Data.Should().ContainKey("overdueTrigger").WhoseValue.Should().Be(
            "stalled.overdue-trigger",
            "the trigger an operator is shown has to be the one the verdict was reached on");
        stalled.Store.TriggerQueries.Should().Be(
            2,
            "the escalation is what this test is about: the first answer was the mild trigger, so a second "
            + "query had to establish that a worse one exists");
    }

    /// <summary>
    /// A clustered node can be behind on its check-in <em>and</em> stalled. The graver verdict has to be
    /// the one reported, or the milder finding would keep a node in rotation that the other one was
    /// about to take out of it.
    /// </summary>
    [Test]
    public async Task AStalledNodeIsUnhealthyEvenWhenItsCheckInIsAlsoLate()
    {
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero));
        DateTimeOffset lastCheckIn = clock.GetUtcNow();
        DateTimeOffset due = clock.GetUtcNow();

        // Twenty-eight check-in intervals late, and seven misfire thresholds overdue — the store this
        // container does not hold is judged by one minute. Degraded on one reading, unhealthy on the
        // other.
        clock.Advance(TimeSpan.FromMinutes(7));

        IScheduler scheduler = RunningScheduler(clock, clustered: true);
        A.CallTo(() => scheduler.QueryClusterNodes(A<CancellationToken>._)).Returns(
            new List<ClusterNode> { new("node-a", lastCheckIn, TimeSpan.FromSeconds(15), ClusterNodeState.Alive, IsCurrentNode: true) });
        A.CallTo(() => scheduler.QueryTriggers(A<TriggerQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<TriggerHeader>([OverdueHeader(due)], HasMore: false));

        await using ServiceProvider checkInOnly = Container(scheduler, _ => { });
        (await Run(checkInOnly)).Status.Should().Be(
            HealthStatus.Degraded,
            "the check-in reading on its own answers degraded - which is the verdict the stale reading "
            + "must not be allowed to hide behind");

        await using ServiceProvider provider = Container(scheduler, options => options.StaleFiringTolerance = 3);

        HealthReportEntry result = await Run(provider);

        result.Status.Should().Be(
            HealthStatus.Unhealthy,
            "the node has stopped firing, which is graver than a late check-in; reporting the first finding "
            + "would answer degraded and leave it in the rotation");
        result.Description.Should().Contain("has not fired trigger");
    }

    /// <summary>
    /// A scheduler whose store the container does not hold — one added at runtime, or one the check
    /// found in the repository — is judged by one minute, the database store's default.
    /// </summary>
    [Test]
    public async Task AStoreTheContainerDoesNotHoldIsJudgedByOneMinute()
    {
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero));
        List<TriggerQuery> asked = [];

        IScheduler scheduler = RunningScheduler(clock, clustered: false);
        A.CallTo(() => scheduler.QueryTriggers(A<TriggerQuery>._, A<CancellationToken>._))
            .Invokes((TriggerQuery query, CancellationToken _) => asked.Add(query))
            .Returns(new PagedResult<TriggerHeader>([], HasMore: false));

        await using ServiceProvider provider = Container(scheduler, options => options.StaleFiringTolerance = 3);

        (await Run(provider)).Status.Should().Be(HealthStatus.Healthy);
        asked.Should().ContainSingle().Which.NextFireTimeBefore.Should().Be(
            clock.GetUtcNow() - TimeSpan.FromMinutes(3),
            "there is no store in this container to read a threshold off, so the check falls back to one "
            + "minute - the database store's default and the more forgiving of the two");
    }

    /// <summary>
    /// A store that cannot answer is a store problem, not a stall — and the check has to say which.
    /// </summary>
    [Test]
    public async Task AStoreThatCannotAnswerTheQueryIsUnhealthyAndSaysSo()
    {
        await using StalledScheduler stalled = await StalledScheduler.Create(options => options.StaleFiringTolerance = 3);
        stalled.Store.FailTriggerQueries = true;
        stalled.Clock.Advance(TimeSpan.FromSeconds(40));

        HealthReportEntry result = await stalled.Check();

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("cannot read the triggers");
    }

    /// <summary>
    /// A scheduler that is running and answers its store, for the cases where the store itself is not
    /// what is under test.
    /// </summary>
    private static IScheduler RunningScheduler(TimeProvider clock, bool clustered)
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
            JobStoreClustered = clustered,
            Version = "4.2.0.0"
        });

        return scheduler;
    }

    /// <summary>A listing entry for a trigger that was due at <paramref name="due" /> and did not fire.</summary>
    private static TriggerHeader OverdueHeader(DateTimeOffset due) => new(
        new TriggerKey("overdue-trigger", "stalled"),
        new JobKey("job", "stalled"),
        Description: null,
        TriggerType: "SIMPLE",
        State: TriggerState.Normal,
        StartTimeUtc: due,
        EndTimeUtc: null,
        NextFireTimeUtc: due,
        PreviousFireTimeUtc: null,
        CalendarName: null,
        Priority: 5,
        ExecutionGroup: null,
        RetryPolicy: null,
        RetryAttempt: 0);

    /// <summary>
    /// A container holding that scheduler and a health check over it, and nothing else — no job store,
    /// which is what makes these the "store the container does not hold" cases.
    /// </summary>
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

    /// <summary>
    /// A running scheduler, a real in-memory store, and one trigger that will never be fired because the
    /// store hands the scheduler nothing to acquire.
    /// </summary>
    private sealed class StalledScheduler : IAsyncDisposable
    {
        private readonly ServiceProvider provider;

        private StalledScheduler(ServiceProvider provider, IScheduler scheduler, FakeTimeProvider clock, StalledJobStore store)
        {
            this.provider = provider;
            Scheduler = scheduler;
            Clock = clock;
            Store = store;
        }

        public IScheduler Scheduler { get; }

        public FakeTimeProvider Clock { get; }

        public StalledJobStore Store { get; }

        public static async Task<StalledScheduler> Create(
            Action<QuartzHealthCheckOptions>? configure,
            TimeSpan? misfireThreshold = null)
        {
            FakeTimeProvider clock = new(new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero));
            StalledJobStore? store = null;

            ServiceCollection services = new();
            services.AddLogging();
            services.AddSingleton<TimeProvider>(clock);
            services.AddQuartz(q => q.UseJobStore(scheduler =>
            {
                RAMJobStore inner = ActivatorUtilities.CreateInstance<RAMJobStore>(scheduler);
                inner.MisfireThreshold = misfireThreshold ?? MisfireThreshold;
                return store = new StalledJobStore(inner);
            }));

            services.AddHealthChecks().AddQuartz(configure);

            ServiceProvider provider = services.BuildServiceProvider();
            IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
            await scheduler.Start();

            StalledScheduler stalled = new(provider, scheduler, clock, store!);
            await stalled.Schedule("overdue-trigger");
            store!.ResetCounters();
            return stalled;
        }

        /// <summary>
        /// Schedules a trigger due at this moment on the fixture's clock. It is never fired, so it is
        /// still due at that moment however far the test moves the clock afterwards.
        /// </summary>
        public async Task Schedule(string triggerName)
        {
            await Scheduler.ScheduleJob(
                JobBuilder.Create<NothingJob>().WithIdentity(triggerName, "stalled").Build(),
                TriggerBuilder.Create(Clock)
                    .WithIdentity(triggerName, "stalled")
                    .StartAt(Clock.GetUtcNow())
                    .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMinutes(1)).RepeatForever())
                    .Build());
        }

        public async Task<HealthReportEntry> Check()
        {
            HealthReport report = await provider.GetRequiredService<HealthCheckService>()
                .CheckHealthAsync(registration => registration.Name == "quartz-scheduler");

            return report.Entries["quartz-scheduler"];
        }

        public async ValueTask DisposeAsync()
        {
            await Scheduler.Shutdown(waitForJobsToComplete: false);
            await provider.DisposeAsync();
        }
    }

    /// <summary>
    /// The stall: a real store that answers every question truthfully and never hands the scheduler a
    /// trigger to fire, so the one that is due stays due however far the clock moves.
    /// </summary>
    private sealed class StalledJobStore(IJobStore inner) : DelegatingJobStore(inner)
    {
        public int TriggerQueries { get; private set; }

        public bool FailTriggerQueries { get; set; }

        public void ResetCounters() => TriggerQueries = 0;

        public override ValueTask<List<IOperableTrigger>> AcquireNextTriggers(
            TriggerAcquisitionRequest request,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<List<IOperableTrigger>>([]);
        }

        public override ValueTask<PagedResult<TriggerHeader>> QueryTriggers(
            TriggerQuery query,
            CancellationToken cancellationToken = default)
        {
            TriggerQueries++;
            if (FailTriggerQueries)
            {
                throw new JobPersistenceException("the store is not answering");
            }

            return base.QueryTriggers(query, cancellationToken);
        }
    }

    private sealed class NothingJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
