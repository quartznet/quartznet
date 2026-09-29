#region License
/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */
#endregion

#nullable enable

using FakeItEasy;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using Quartz.Configuration;
using Quartz.Extensibility;

namespace Quartz.Tests.Unit.HealthChecks;

/// <summary>
/// <see cref="QuartzHealthCheckOptions.RequiredJobs" />: the check degrades when a named job has not succeeded
/// within its window, read from the execution history's per-job status (#3961).
/// </summary>
/// <remarks>
/// Every window is on the scheduler's clock, so the tests move a <see cref="FakeTimeProvider" /> rather than
/// wait, and write runs straight into the in-memory history rather than run jobs.
/// </remarks>
public class RequiredJobsHealthCheckTest
{
    private static readonly DateTimeOffset Start = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly JobKey Nightly = new("nightly-report", "reports");
    private static readonly JobKey Hourly = new("hourly-sync", "reports");

    [Test]
    public async Task AJobWithinItsWindowSinceTheCheckStartedIsHealthy()
    {
        await using RunningScheduler running = await RunningScheduler.Create(o => o.RequireSuccessWithin(Nightly, TimeSpan.FromHours(1)));

        (await running.Check()).Status.Should().Be(HealthStatus.Healthy);
        running.Clock.Advance(TimeSpan.FromMinutes(59));

        (await running.Check()).Status.Should().Be(
            HealthStatus.Healthy,
            "a job with no recorded run is judged from when the check started watching it, so a process that has "
            + "just started gives each job one window to run in");
    }

    [Test]
    public async Task AJobThatHasNotRunWithinItsWindowIsDegraded()
    {
        await using RunningScheduler running = await RunningScheduler.Create(o => o.RequireSuccessWithin(Nightly, TimeSpan.FromHours(1)));
        await running.Check();
        running.Clock.Advance(TimeSpan.FromMinutes(61));

        HealthReportEntry result = await running.Check();

        result.Status.Should().Be(HealthStatus.Degraded, "degraded is the default status of a requirement");
        result.Description.Should().Contain("'reports.nightly-report'").And.Contain("01:00:00")
            .And.Contain("has not succeeded in the 01:01:00 since the check started watching it");
        Entry(result, Nightly)["lastSucceededAtUtc"].Should().Be("never");
        Entry(result, Nightly)["consecutiveFailures"].Should().Be(0);
        Entry(result, Nightly)["succeededWithin"].Should().Be(TimeSpan.FromHours(1));
    }

    [Test]
    public async Task ASuccessMovesTheJobBackToHealthy()
    {
        await using RunningScheduler running = await RunningScheduler.Create(o => o.RequireSuccessWithin(Nightly, TimeSpan.FromHours(1)));
        await running.Check();
        running.Clock.Advance(TimeSpan.FromHours(3));
        (await running.Check()).Status.Should().Be(HealthStatus.Degraded);

        await running.Record(Nightly, JobRunResult.Succeeded, running.Clock.GetUtcNow().AddMinutes(-5));

        (await running.Check()).Status.Should().Be(HealthStatus.Healthy);

        running.Clock.Advance(TimeSpan.FromMinutes(56));
        HealthReportEntry late = await running.Check();
        late.Status.Should().Be(HealthStatus.Degraded, "the window runs from the last success's fire time");
        late.Description.Should().Contain("it last succeeded 01:01:00 ago");
    }

    [Test]
    public async Task ASkippedRunCountsAsASuccess()
    {
        await using RunningScheduler running = await RunningScheduler.Create(o => o.RequireSuccessWithin(Nightly, TimeSpan.FromHours(1)));
        await running.Check();
        running.Clock.Advance(TimeSpan.FromHours(3));

        await running.Record(Nightly, JobRunResult.Skipped, running.Clock.GetUtcNow().AddMinutes(-10));

        (await running.Check()).Status.Should().Be(
            HealthStatus.Healthy,
            "a run that found nothing to do did its job, so a quiet day is not a stale job");
    }

    [Test]
    public async Task AFailingJobReportsItsLastSuccessAndConsecutiveFailures()
    {
        await using RunningScheduler running = await RunningScheduler.Create(o => o.RequireSuccessWithin(Nightly, TimeSpan.FromHours(1)));
        DateTimeOffset succeeded = Start.AddHours(-3);
        await running.Record(Nightly, JobRunResult.Succeeded, succeeded);
        await running.Record(Nightly, JobRunResult.Failed, Start.AddHours(-2));
        await running.Record(Nightly, JobRunResult.Failed, Start.AddHours(-1));

        HealthReportEntry result = await running.Check();

        result.Status.Should().Be(
            HealthStatus.Degraded,
            "a recorded success three hours ago is older than the window, however recently the check started");
        result.Description.Should().Contain("it last succeeded 03:00:00 ago and its last 2 runs failed");
        Entry(result, Nightly)["lastSucceededAtUtc"].Should().Be(succeeded);
        Entry(result, Nightly)["consecutiveFailures"].Should().Be(2);
    }

    [Test]
    public async Task AFailingJobThatHasNeverSucceededIsJudgedFromTheCheckStart()
    {
        await using RunningScheduler running = await RunningScheduler.Create(o => o.RequireSuccessWithin(Nightly, TimeSpan.FromHours(1)));
        await running.Record(Nightly, JobRunResult.Failed, Start.AddMinutes(-1));

        (await running.Check()).Status.Should().Be(HealthStatus.Healthy);
        running.Clock.Advance(TimeSpan.FromHours(2));

        HealthReportEntry result = await running.Check();
        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("since the check started watching it and its last run failed");
        Entry(result, Nightly)["lastSucceededAtUtc"].Should().Be("never");
        Entry(result, Nightly)["consecutiveFailures"].Should().Be(1);
    }

    [Test]
    public async Task ARequirementCanReportUnhealthy()
    {
        await using RunningScheduler running = await RunningScheduler.Create(
            o => o.RequireSuccessWithin(Nightly, TimeSpan.FromHours(1), HealthStatus.Unhealthy));
        await running.Check();
        running.Clock.Advance(TimeSpan.FromHours(2));

        (await running.Check()).Status.Should().Be(
            HealthStatus.Unhealthy,
            "a job that must never be late says so per requirement, and takes the node out of rotation");
    }

    [Test]
    public async Task TheMessageNamesTheGravestLateJobAndCountsTheOthers()
    {
        JobKey onTime = new("on-time", "reports");
        await using RunningScheduler running = await RunningScheduler.Create(o =>
        {
            o.RequireSuccessWithin(Hourly, TimeSpan.FromHours(1));
            o.RequireSuccessWithin(Nightly, TimeSpan.FromHours(1), HealthStatus.Unhealthy);
            o.RequireSuccessWithin(onTime, TimeSpan.FromHours(1));
        });
        await running.Check();
        running.Clock.Advance(TimeSpan.FromHours(2));
        await running.Record(onTime, JobRunResult.Succeeded, running.Clock.GetUtcNow());

        HealthReportEntry result = await running.Check();

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("'reports.nightly-report'", "the job that made the verdict unhealthy is the one named")
            .And.Contain("1 other required job has not succeeded within its window either");
        result.Data.Keys.Should().BeEquivalentTo(["reports.nightly-report", "reports.hourly-sync"],
            "the data lists every late job, and only the late ones");
    }

    [Test]
    public async Task MoreThanOneOtherLateJobIsCounted()
    {
        await using RunningScheduler running = await RunningScheduler.Create(o =>
        {
            o.RequireSuccessWithin(Nightly, TimeSpan.FromHours(1));
            o.RequireSuccessWithin(Hourly, TimeSpan.FromHours(1));
            o.RequireSuccessWithin(new JobKey("third", "reports"), TimeSpan.FromHours(1));
        });
        await running.Check();
        running.Clock.Advance(TimeSpan.FromHours(2));

        HealthReportEntry result = await running.Check();

        result.Description.Should().Contain("'reports.nightly-report'", "among equals, the first required is named")
            .And.Contain("2 other required jobs have not succeeded within their windows either");
    }

    [Test]
    public async Task EveryRequirementIsReadInOneStatusCall()
    {
        IExecutionHistoryStore store = A.Fake<IExecutionHistoryStore>();
        List<JobRunStatusQuery> asked = [];
        A.CallTo(() => store.QueryJobRunStatuses(A<JobRunStatusQuery>._, A<CancellationToken>._))
            .Invokes((JobRunStatusQuery query, CancellationToken _) => asked.Add(query))
            .Returns(new PagedResult<JobRunStatus>([Status(Nightly, Start)], HasMore: false));

        await using RunningScheduler running = await RunningScheduler.Create(
            o =>
            {
                o.RequireSuccessWithin(Nightly, TimeSpan.FromHours(1));
                o.RequireSuccessWithin(Hourly, TimeSpan.FromHours(1));
            },
            services => services.AddSingleton(store));

        (await running.Check()).Status.Should().Be(HealthStatus.Healthy);
        (await running.Check()).Status.Should().Be(HealthStatus.Healthy);

        List<JobRunStatusQuery> reads = asked.Where(query => query.Jobs is { Count: > 0 }).ToList();
        reads.Should().HaveCount(2, "each check reads once, however many jobs it requires");
        reads.Should().AllSatisfy(query =>
        {
            query.SchedulerName.Should().Be(running.Scheduler.SchedulerName);
            query.Jobs.Should().BeEquivalentTo([Nightly, Hourly]);
            query.Take.Should().Be(2);
        });
    }

    [Test]
    public async Task TheLaterRequirementForAJobWins()
    {
        QuartzHealthCheckOptions options = new();
        options.RequireSuccessWithin(Nightly, TimeSpan.FromMinutes(1));
        options.RequireSuccessWithin(Nightly, TimeSpan.FromHours(5), HealthStatus.Unhealthy);

        options.RequiredJobs.Should().ContainSingle("adding the same job twice replaces the first")
            .Which.Should().BeEquivalentTo(new RequiredJobOptions
            {
                Name = "nightly-report",
                Group = "reports",
                SucceededWithin = TimeSpan.FromHours(5),
                Status = HealthStatus.Unhealthy
            });

        await using RunningScheduler running = await RunningScheduler.Create(o =>
        {
            o.RequireSuccessWithin(Nightly, TimeSpan.FromHours(5));

            // A configuration section and a callback can both add to the list; the later entry is the one read.
            o.RequiredJobs.Add(new RequiredJobOptions { Name = Nightly.Name, Group = Nightly.Group, SucceededWithin = TimeSpan.FromMinutes(1) });
        });
        await running.Check();
        running.Clock.Advance(TimeSpan.FromMinutes(2));

        (await running.Check()).Status.Should().Be(HealthStatus.Degraded);
    }

    [Test]
    public async Task StandbyAndALateJobReportTheWorseOfTheTwoAndBoth()
    {
        await using RunningScheduler running = await RunningScheduler.Create(
            o => o.RequireSuccessWithin(Nightly, TimeSpan.FromHours(1), HealthStatus.Unhealthy));
        await running.Check();
        await running.Scheduler.Standby();
        running.Clock.Advance(TimeSpan.FromHours(2));

        HealthReportEntry result = await running.Check();

        result.Status.Should().Be(HealthStatus.Unhealthy, "standby is degraded and the late job unhealthy, and the worse one wins");
        result.Description.Should().StartWith($"Quartz scheduler '{running.Scheduler.SchedulerName}' requires job")
            .And.Contain("is in standby", "the milder finding is reported after the graver one, not dropped");
        result.Data.Should().ContainKey("reports.nightly-report");
    }

    [Test]
    public async Task OnATieTheSchedulersOwnVerdictIsReportedFirst()
    {
        await using RunningScheduler running = await RunningScheduler.Create(o => o.RequireSuccessWithin(Nightly, TimeSpan.FromHours(1)));
        await running.Check();
        await running.Scheduler.Standby();
        running.Clock.Advance(TimeSpan.FromHours(2));

        HealthReportEntry result = await running.Check();

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().StartWith($"Quartz scheduler '{running.Scheduler.SchedulerName}' is in standby. ")
            .And.Contain("'reports.nightly-report'");
    }

    [Test]
    public async Task AnUnhealthyVerdictIsNotReadFurther()
    {
        IExecutionHistoryStore store = A.Fake<IExecutionHistoryStore>();
        A.CallTo(() => store.QueryJobRunStatuses(A<JobRunStatusQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<JobRunStatus>([], HasMore: false));

        await using RunningScheduler running = await RunningScheduler.Create(
            o =>
            {
                o.StandbyStatus = HealthStatus.Unhealthy;
                o.RequireSuccessWithin(Nightly, TimeSpan.FromHours(1));
            },
            services => services.AddSingleton(store));
        await running.Scheduler.Standby();

        (await running.Check()).Status.Should().Be(HealthStatus.Unhealthy);

        A.CallTo(() => store.QueryJobRunStatuses(
                A<JobRunStatusQuery>.That.Matches(query => query.Jobs != null && query.Jobs.Count > 0),
                A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Test]
    public async Task ALateClusterCheckInAndALateJobReportTheWorseOfTheTwo()
    {
        FakeTimeProvider clock = new(Start);
        IScheduler scheduler = FakeRunningScheduler(clock, clustered: true);
        A.CallTo(() => scheduler.QueryClusterNodes(A<CancellationToken>._)).Returns(
            new List<ClusterNode> { new("node-a", Start, TimeSpan.FromSeconds(15), ClusterNodeState.Alive, IsCurrentNode: true) });

        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(FactoryFor(scheduler));
        services.AddSingleton<TimeProvider>(clock);
        services.AddQuartzExecutionHistory();
        services.AddHealthChecks().AddQuartz(o =>
        {
            o.RequireSuccessWithin(Nightly, TimeSpan.FromMinutes(30));
            o.RequireSuccessWithin(Hourly, TimeSpan.FromMinutes(30), HealthStatus.Unhealthy);
        });

        await using ServiceProvider provider = services.BuildServiceProvider();
        (await Run(provider)).Status.Should().Be(HealthStatus.Healthy);

        clock.Advance(TimeSpan.FromMinutes(40));
        IExecutionHistoryStore history = provider.GetRequiredService<IExecutionHistoryStore>();
        await history.AddExecution(Execution("core", Hourly, JobRunResult.Succeeded, clock.GetUtcNow()));

        HealthReportEntry degraded = await Run(provider);
        degraded.Status.Should().Be(HealthStatus.Degraded, "the check-in and the nightly job are both late, and both only degrade");
        degraded.Description.Should().Contain("check-in interval").And.Contain("'reports.nightly-report'");
        degraded.Data.Should().ContainKey("reports.nightly-report");

        clock.Advance(TimeSpan.FromMinutes(40));

        HealthReportEntry unhealthy = await Run(provider);
        unhealthy.Status.Should().Be(HealthStatus.Unhealthy, "the hourly job is now late too, and it asked for unhealthy");
        unhealthy.Description.Should().StartWith("Quartz scheduler 'core' requires job 'reports.hourly-sync'")
            .And.Contain("check-in interval");
    }

    // ---------------------------------------------------------------------------------------------
    // Misconfiguration
    // ---------------------------------------------------------------------------------------------

    [Test]
    public void RequiringAJobWithNoHistoryFailsAtStartup()
    {
        ServiceCollection services = new();
        services.AddQuartz();
        services.AddHealthChecks().AddQuartz(o => o.RequireSuccessWithin(Nightly, TimeSpan.FromHours(1)));

        using ServiceProvider provider = services.BuildServiceProvider();

        Action act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*RequiredJobs*AddQuartzExecutionHistory()*UsePersistentStore(store => store.UseExecutionHistory())*",
                "a requirement nothing can answer must stop the host, not report every job as late");
    }

    [Test]
    public void RequiringAJobOfAHistoryThatKeepsNoStatusFailsAtStartup()
    {
        ServiceCollection services = new();
        services.AddQuartz("reporting");
        services.AddSingleton<IExecutionHistoryStore>(new RowsOnlyHistoryStore());
        services.AddHealthChecks().AddQuartz("reporting", o => o.RequireSuccessWithin(Nightly, TimeSpan.FromHours(1)));

        using ServiceProvider provider = services.BuildServiceProvider();

        Action act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("The health check of Quartz scheduler 'reporting'*RowsOnlyHistoryStore*keeps no per-job run status*",
                "the store's own reason is carried into the message");
    }

    [Test]
    public void NoRequirementsNeedNoHistory()
    {
        ServiceCollection services = new();
        services.AddQuartz();
        services.AddHealthChecks().AddQuartz(o => o.Tags.Add("ready"));

        using ServiceProvider provider = services.BuildServiceProvider();

        Action act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().NotThrow("the requirement is opt-in, and a check without one reads nothing of the history");
    }

    [Test]
    public async Task AStoreThatCannotBeReachedAtStartupIsReportedByTheCheck()
    {
        IExecutionHistoryStore store = A.Fake<IExecutionHistoryStore>();
        A.CallTo(() => store.QueryJobRunStatuses(A<JobRunStatusQuery>._, A<CancellationToken>._))
            .Throws(new JobPersistenceException("the database is not answering"));

        await using RunningScheduler running = await RunningScheduler.Create(
            o => o.RequireSuccessWithin(Nightly, TimeSpan.FromHours(1)),
            services => services.AddSingleton(store));

        Action validate = () => running.Provider.GetRequiredService<IStartupValidator>().Validate();
        validate.Should().NotThrow("a store that is down is not a store that cannot answer, and the check says which");

        HealthReportEntry result = await running.Check();
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("cannot read its jobs' run status");
    }

    [Test]
    public async Task AStoreThatOnlyAnswersForNoJobsIsReportedByTheCheck()
    {
        // A remote host older than 4.4 behind the HTTP client: nothing to fetch for no jobs, a refusal for some.
        IExecutionHistoryStore store = A.Fake<IExecutionHistoryStore>();
        A.CallTo(() => store.QueryJobRunStatuses(A<JobRunStatusQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<JobRunStatus>([], HasMore: false));
        A.CallTo(() => store.QueryJobRunStatuses(
                A<JobRunStatusQuery>.That.Matches(query => query.Jobs != null && query.Jobs.Count > 0),
                A<CancellationToken>._))
            .Throws(new NotSupportedException("The host is older than 4.4."));

        await using RunningScheduler running = await RunningScheduler.Create(
            o => o.RequireSuccessWithin(Nightly, TimeSpan.FromHours(1)),
            services => services.AddSingleton(store));

        HealthReportEntry result = await running.Check();

        result.Status.Should().Be(HealthStatus.Unhealthy, "a misconfiguration is said out loud, never read as every job being late");
        result.Description.Should().Contain("The host is older than 4.4.").And.Contain("AddQuartzExecutionHistory()");
    }

    /// <summary>
    /// Startup validation refuses a requirement with no history to read; a check that meets one anyway says
    /// what to register rather than reporting the job as late.
    /// </summary>
    [Test]
    public async Task ACheckThatFindsNoHistoryIsUnhealthyAndSaysWhatToRegister()
    {
        ServiceCollection services = new();
        services.AddSingleton(FactoryFor(FakeRunningScheduler(new FakeTimeProvider(Start), clustered: false)));
        await using ServiceProvider provider = services.BuildServiceProvider();

        QuartzHealthCheckOptions options = new();
        options.RequireSuccessWithin(Nightly, TimeSpan.FromHours(1));
        IOptionsMonitor<QuartzHealthCheckOptions> checkOptions = A.Fake<IOptionsMonitor<QuartzHealthCheckOptions>>();
        A.CallTo(() => checkOptions.Get(A<string?>._)).Returns(options);
        IOptionsMonitor<QuartzHostedServiceOptions> hostedService = A.Fake<IOptionsMonitor<QuartzHostedServiceOptions>>();
        A.CallTo(() => hostedService.Get(A<string?>._)).Returns(new QuartzHostedServiceOptions());

        IHealthCheck check = new QuartzHealthCheck(
            provider,
            new SchedulerHealthCheckTarget(SchedulerName: null),
            hostedService,
            checkOptions,
            new RequiredJobsBaseline());

        HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("the default Quartz scheduler").And.Contain("AddQuartzExecutionHistory()");
    }

    /// <summary>
    /// A scheduler <c>AddQuartzHttpClient</c> registered keeps its history under its own name, in a container
    /// that need keep no shared one.
    /// </summary>
    [Test]
    public async Task ASchedulersOwnKeyedHistoryIsReadWhenTheContainerKeepsNoSharedOne()
    {
        FakeTimeProvider clock = new(Start);
        IExecutionHistoryStore store = A.Fake<IExecutionHistoryStore>();
        A.CallTo(() => store.QueryJobRunStatuses(A<JobRunStatusQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<JobRunStatus>([Status(Nightly, Start.AddHours(-2))], HasMore: false));

        ServiceCollection services = new();
        services.AddLogging();
        services.AddKeyedSingleton("remote", FactoryFor(FakeRunningScheduler(clock, clustered: false, name: "remote")));
        services.AddKeyedSingleton("remote", store);
        services.AddHealthChecks().AddQuartz("remote", o => o.RequireSuccessWithin(Nightly, TimeSpan.FromHours(1)));

        await using ServiceProvider provider = services.BuildServiceProvider();

        Action validate = () => provider.GetRequiredService<IStartupValidator>().Validate();
        validate.Should().NotThrow("the scheduler's own history answers, although the container keeps no shared one");

        HealthReport report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();
        report.Entries["quartz-scheduler-remote"].Status.Should().Be(HealthStatus.Degraded);
        A.CallTo(() => store.QueryJobRunStatuses(
                A<JobRunStatusQuery>.That.Matches(query => query.SchedulerName == "remote" && query.Jobs != null && query.Jobs.Count == 1),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    // ---------------------------------------------------------------------------------------------
    // Values and binding
    // ---------------------------------------------------------------------------------------------

    [Test]
    public void RequirementsBindFromConfiguration()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["HealthCheck:RequiredJobs:0:Name"] = "nightly-report",
                ["HealthCheck:RequiredJobs:0:Group"] = "reports",
                ["HealthCheck:RequiredJobs:0:SucceededWithin"] = "1.02:00:00",
                ["HealthCheck:RequiredJobs:1:Name"] = "ledger-close",
                ["HealthCheck:RequiredJobs:1:SucceededWithin"] = "01:30:00",
                ["HealthCheck:RequiredJobs:1:Status"] = "Unhealthy"
            })
            .Build();

        ServiceCollection services = new();
        services.Configure<QuartzHealthCheckOptions>(configuration.GetSection("HealthCheck"));

        using ServiceProvider provider = services.BuildServiceProvider();
        QuartzHealthCheckOptions options = provider.GetRequiredService<IOptions<QuartzHealthCheckOptions>>().Value;

        options.RequiredJobs.Should().BeEquivalentTo(
            [
                new RequiredJobOptions { Name = "nightly-report", Group = "reports", SucceededWithin = new TimeSpan(1, 2, 0, 0) },
                new RequiredJobOptions
                {
                    Name = "ledger-close",
                    SucceededWithin = TimeSpan.FromMinutes(90),
                    Status = HealthStatus.Unhealthy
                }
            ],
            "the element names a job by name and group so a section binds onto it, and an unset group and status "
            + "keep their defaults");
        options.RequiredJobs[1].Group.Should().Be(JobKey.DefaultGroup);
    }

    [TestCase("", "reports", 60, HealthStatus.Degraded, "names no job")]
    [TestCase("nightly", " ", 60, HealthStatus.Degraded, "empty Group")]
    [TestCase("nightly", "reports", 0, HealthStatus.Degraded, "must be positive")]
    [TestCase("nightly", "reports", -5, HealthStatus.Degraded, "must be positive")]
    [TestCase("nightly", "reports", 60, HealthStatus.Healthy, "Use Degraded or Unhealthy")]
    public void ARequirementThatCannotBeMetOrNeverFailsIsRefused(string name, string group, int minutes, HealthStatus status, string expected)
    {
        QuartzHealthCheckOptions options = new();
        options.RequiredJobs.Add(new RequiredJobOptions
        {
            Name = name,
            Group = group,
            SucceededWithin = TimeSpan.FromMinutes(minutes),
            Status = status
        });

        ValidateOptionsResult result = new QuartzHealthCheckOptionsValidator().Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(expected);
    }

    [Test]
    public void ANullRequirementIsRefused()
    {
        QuartzHealthCheckOptions options = new();
        options.RequiredJobs.Add(null!);

        ValidateOptionsResult result = new QuartzHealthCheckOptionsValidator().Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("null entry");
    }

    [Test]
    public void ARequirementThatCanBeMetPassesValidation()
    {
        QuartzHealthCheckOptions options = new();
        options.RequireSuccessWithin(Nightly, TimeSpan.FromHours(26), HealthStatus.Unhealthy);

        new QuartzHealthCheckOptionsValidator().Validate(name: null, options).Succeeded.Should().BeTrue();
    }

    [Test]
    public void RequireSuccessWithinRefusesANullJob()
    {
        QuartzHealthCheckOptions options = new();

        Action act = () => options.RequireSuccessWithin(null!, TimeSpan.FromHours(1));

        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void TheBaselineIsTheFirstInstantItWasAskedAt()
    {
        RequiredJobsBaseline baseline = new();

        baseline.Since(Start).Should().Be(Start);
        baseline.Since(Start.AddHours(1)).Should().Be(Start, "the baseline is set once and kept for the registration");
    }

    // ---------------------------------------------------------------------------------------------

    private static Dictionary<string, object> Entry(HealthReportEntry result, JobKey job)
    {
        return result.Data.Should().ContainKey(job.ToString())
            .WhoseValue.Should().BeOfType<Dictionary<string, object>>().Which;
    }

    private static JobRunStatus Status(JobKey job, DateTimeOffset lastSucceeded) => new("QuartzScheduler", job, lastSucceeded, JobRunResult.Succeeded)
    {
        LastSucceededAtUtc = lastSucceeded
    };

    private static ExecutionHistoryEntry Execution(string schedulerName, JobKey job, JobRunResult result, DateTimeOffset firedAt) => new(
        SchedulerName: schedulerName,
        SchedulerInstanceId: "node-a",
        JobGroup: job.Group,
        JobName: job.Name,
        TriggerGroup: job.Group,
        TriggerName: job.Name,
        FiredAtUtc: firedAt,
        Duration: TimeSpan.FromSeconds(1),
        Succeeded: result is JobRunResult.Succeeded or JobRunResult.Skipped,
        ExceptionMessage: result == JobRunResult.Failed ? "it broke" : null)
    {
        Result = result
    };

    private static IScheduler FakeRunningScheduler(TimeProvider clock, bool clustered, string name = "core")
    {
        IScheduler scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.SchedulerName).Returns(name);
        A.CallTo(() => scheduler.Status).Returns(SchedulerStatus.Running);
        A.CallTo(() => scheduler.TimeProvider).Returns(clock);
        A.CallTo(() => scheduler.GetMetadata(A<CancellationToken>._)).Returns(new SchedulerMetadata
        {
            SchedulerName = name,
            SchedulerInstanceId = "node-a",
            SchedulerTypeName = "Quartz.Core.QuartzScheduler",
            JobStoreTypeName = "Quartz.Impl.AdoJobStore.LocalTransactionJobStore",
            ThreadPoolTypeName = "Quartz.Impl.DefaultThreadPool",
            Status = SchedulerStatus.Running,
            JobStoreClustered = clustered,
            Version = "4.4.0.0"
        });

        return scheduler;
    }

    private static ISchedulerFactory FactoryFor(IScheduler scheduler)
    {
        ISchedulerFactory factory = A.Fake<ISchedulerFactory>();
        A.CallTo(() => factory.GetScheduler(A<CancellationToken>._)).Returns(new ValueTask<IScheduler>(scheduler));
        return factory;
    }

    private static async Task<HealthReportEntry> Run(ServiceProvider provider)
    {
        HealthReport report = await provider.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(registration => registration.Name == "quartz-scheduler");

        return report.Entries["quartz-scheduler"];
    }

    /// <summary>
    /// A started default scheduler on a fake clock, its in-memory history, and a health check with requirements.
    /// </summary>
    private sealed class RunningScheduler : IAsyncDisposable
    {
        private RunningScheduler(ServiceProvider provider, IScheduler scheduler, FakeTimeProvider clock)
        {
            Provider = provider;
            Scheduler = scheduler;
            Clock = clock;
        }

        public ServiceProvider Provider { get; }

        public IScheduler Scheduler { get; }

        public FakeTimeProvider Clock { get; }

        public static async Task<RunningScheduler> Create(
            Action<QuartzHealthCheckOptions> configure,
            Action<IServiceCollection>? services = null)
        {
            FakeTimeProvider clock = new(Start);

            ServiceCollection collection = new();
            collection.AddLogging();
            collection.AddSingleton<TimeProvider>(clock);
            services?.Invoke(collection);
            collection.AddQuartz(q => q.UseInMemoryStore());
            collection.AddQuartzExecutionHistory();
            collection.AddHealthChecks().AddQuartz(configure);

            ServiceProvider provider = collection.BuildServiceProvider();
            IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
            await scheduler.Start();

            return new RunningScheduler(provider, scheduler, clock);
        }

        public async Task Record(JobKey job, JobRunResult result, DateTimeOffset firedAt)
        {
            await Provider.GetRequiredService<IExecutionHistoryStore>()
                .AddExecution(Execution(Scheduler.SchedulerName, job, result, firedAt));
        }

        public Task<HealthReportEntry> Check() => Run(Provider);

        public async ValueTask DisposeAsync()
        {
            await Scheduler.Shutdown(waitForJobsToComplete: false);
            await Provider.DisposeAsync();
        }
    }

    /// <summary>
    /// A history store written before 4.4: it keeps rows and leaves the status reads to their defaults.
    /// </summary>
    private sealed class RowsOnlyHistoryStore : IExecutionHistoryStore
    {
        public ValueTask AddExecution(ExecutionHistoryEntry entry, CancellationToken cancellationToken = default) => default;

        public ValueTask<PagedResult<ExecutionHistoryEntry>> QueryExecutions(ExecutionHistoryQuery query, CancellationToken cancellationToken = default)
            => new(new PagedResult<ExecutionHistoryEntry>([], HasMore: false));

        public ValueTask AddMisfire(MisfireHistoryEntry entry, CancellationToken cancellationToken = default) => default;

        public ValueTask<PagedResult<MisfireHistoryEntry>> QueryMisfires(MisfireHistoryQuery query, CancellationToken cancellationToken = default)
            => new(new PagedResult<MisfireHistoryEntry>([], HasMore: false));

        public ValueTask<int> CountMisfires(string schedulerName, DateTimeOffset since, CancellationToken cancellationToken = default) => new(0);
    }
}
