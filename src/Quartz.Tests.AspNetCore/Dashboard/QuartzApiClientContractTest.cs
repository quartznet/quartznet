using System.Globalization;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Configuration;
using Quartz.Dashboard.Services;
using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.Calendar;
using Quartz.Tests.AspNetCore.Support;

// The wire's own names, aliased rather than imported: the dashboard's DTOs and the contract's share three
// names, and this fixture is about the pair agreeing rather than about either of them.
using KeyDto = Quartz.HttpApiContract.KeyDto;
using SchedulerEvent = Quartz.HttpApiContract.SchedulerEvent;
using SchedulerEventKind = Quartz.HttpApiContract.SchedulerEventKind;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// Which process the scheduler a test drives runs in.
/// </summary>
/// <remarks>
/// The dashboard's client is one implementation over two arrangements: a scheduler this container built,
/// and a scheduler in another process registered with <c>AddQuartzHttpClient</c>. Running the contract
/// under both is what stops the second one drifting — which is exactly what happened to the HTTP-backed
/// client 4.0 deleted, twice, with nothing exercising it.
/// </remarks>
public enum Carrier
{
    /// <summary>A scheduler this container built.</summary>
    Local,

    /// <summary>A scheduler in another process, reached over the Quartz HTTP API.</summary>
    Http,

    /// <summary>A scheduler in another process that dialled out to the dashboard, reached over its agent's connection.</summary>
    Agent
}

/// <summary>
/// The contract <see cref="IQuartzApiClient" /> states, held against whichever implementation the
/// container answers with — over a local scheduler and over one in another process.
/// </summary>
/// <remarks>
/// <para>
/// The interface is public so that an application can replace it, and its four "return the thing itself"
/// members have non-nullable return types — so what they do when the thing is gone is contract, not an
/// implementation detail of the one client Quartz ships. The dashboard's error boundary renders a
/// <see cref="KeyNotFoundException" /> as the not-found page; a replacement answering <c>null!</c>
/// instead would fault the page with a <see cref="NullReferenceException" /> raised somewhere further in,
/// and nothing said so until this test did.
/// </para>
/// <para>
/// It resolves the client out of a container rather than constructing <c>InProcessQuartzApiClient</c>,
/// because the promise is about whatever <c>AddQuartzDashboard</c> left registered — the registration is
/// <c>TryAdd</c>, so an application that registers its own client first is the one the pages read, and it
/// is the one this contract binds.
/// </para>
/// <para>
/// The <see cref="Carrier.Http" /> fixture is a dashboard with no scheduler of its own: it registers
/// <c>AddQuartzHttpClient</c> against a host running the API, so every answer below travels as JSON and
/// comes back through <c>HttpScheduler</c>. A page cannot tell the two apart, and that is the claim.
/// </para>
/// <para>
/// The <see cref="Carrier.Agent" /> fixture is the same claim for the third carrier: a dashboard that
/// accepts agents, and a worker whose scheduler dialled it. Every answer travels down the agent's
/// connection as the contract's bytes and comes back through the same <c>HttpScheduler</c>, over a
/// transport that is not HTTP. The worker allows the test's job types, because an agent refuses every
/// job type until told which it accepts.
/// </para>
/// </remarks>
[TestFixture(Carrier.Local)]
[TestFixture(Carrier.Http)]
[TestFixture(Carrier.Agent)]
public sealed class QuartzApiClientContractTest
{
    private readonly Carrier carrier;

    private WebApplicationFactory<Program>? host;
    private Agents.AgentDashboard? dashboard;
    private IServiceProvider provider = null!;

    /// <summary>The container this fixture built itself, when it did; the agent fixture borrows the dashboard's.</summary>
    private ServiceProvider? owned;
    private IServiceScope scope = null!;
    private IScheduler scheduler = null!;
    private IQuartzApiClient client = null!;
    private IExecutionHistoryStore history = null!;

    /// <summary>
    /// What the client is asked about: the bare name for a scheduler reached through no target, and
    /// <c>target/name</c> for the agent's.
    /// </summary>
    private string key = null!;

    /// <summary>
    /// The broker of the process the scheduler runs in, which is where its events are published. For the
    /// HTTP carrier that is the host's, and the dashboard reads it through the target's reader.
    /// </summary>
    private SchedulerEventBroker broker = null!;

    public QuartzApiClientContractTest(Carrier carrier)
    {
        this.carrier = carrier;
    }

    [SetUp]
    public async Task SetUp()
    {
        switch (carrier)
        {
            case Carrier.Local:
                await SetUpLocal();
                break;
            case Carrier.Http:
                await SetUpHttp();
                break;
            default:
                await SetUpAgent();
                break;
        }

        // Scoped, because that is the lifetime the pages resolve it with.
        scope = provider.CreateScope();
        client = scope.ServiceProvider.GetRequiredService<IQuartzApiClient>();
    }

    private async Task SetUpAgent()
    {
        // The worker runs on the system clock, as the other two carriers' schedulers do: the cases below
        // schedule against now and backfill up to it.
        dashboard = await Agents.AgentDashboard.Start();
        Agents.AgentWorker worker = await dashboard.StartAgent(
            "w1",
            options => options.IsJobTypeAllowed = name => name.StartsWith("Quartz.Tests.AspNetCore.", StringComparison.Ordinal),
            clock: TimeProvider.System);

        // Standing by, as the other two carriers' schedulers are never started: what the cases below put
        // in the store has to still be there when they look.
        scheduler = worker.Scheduler;
        await scheduler.Standby();

        history = worker.Services.GetRequiredService<IExecutionHistoryStore>();
        broker = worker.Broker;
        provider = dashboard.App.Services;
        key = worker.Key;
    }

    private async Task SetUpLocal()
    {
        string schedulerName = $"api-client-contract-{Guid.NewGuid():N}";

        ServiceCollection services = new();
        services.AddQuartzDashboard();
        services.AddQuartz(quartz => quartz.ConfigureScheduler(options => options.InstanceName = schedulerName));

        owned = services.BuildServiceProvider();
        provider = owned;

        // Resolving the scheduler is what binds it into the repository the client looks names up in.
        scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        history = provider.GetRequiredService<IExecutionHistoryStore>();
        broker = provider.GetRequiredService<SchedulerEventBroker>();
        key = scheduler.SchedulerName;
    }

    private async Task SetUpHttp()
    {
        TestContentRoot.Apply();
        host = new WebApplicationFactory<Program>();

        // The test host maps the API but runs no hosted service, so nothing has built its scheduler yet.
        scheduler = await host.Services.GetRequiredService<ISchedulerFactory>().GetScheduler();
        history = host.Services.GetRequiredService<IExecutionHistoryStore>();
        broker = host.Services.GetRequiredService<SchedulerEventBroker>();

        // A dashboard with no scheduler of its own: everything it renders is somebody else's process.
        ServiceCollection services = new();
        services.AddQuartzDashboard();
        services.AddQuartzHttpClient(scheduler.SchedulerName, _ => host.CreateClient());

        owned = services.BuildServiceProvider();
        provider = owned;

        // Resolving it is what binds it into this container's repository, which the hosted service would
        // do in an application that had one.
        provider.GetRequiredKeyedService<IScheduler>(scheduler.SchedulerName).Should().BeOfType<HttpScheduler>();
        key = scheduler.SchedulerName;
    }

    [TearDown]
    public async Task TearDown()
    {
        scope?.Dispose();

        // Cleared while the container is still there: the in-memory store announces the jobs it deletes
        // through a signaler that resolves the scheduler from it the first time it is used.
        if (scheduler is not null)
        {
            await scheduler.Clear();
        }

        if (owned is not null)
        {
            await owned.DisposeAsync();
            owned = null;
        }

        if (scheduler is not null && carrier == Carrier.Local)
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }

        if (host is not null)
        {
            await host.DisposeAsync();
            host = null;
        }

        if (dashboard is not null)
        {
            await dashboard.DisposeAsync();
            dashboard = null;
        }
    }

    [Test]
    public async Task AnUnknownSchedulerNameIsReportedAsAMissingKey()
    {
        Func<Task> act = () => client.GetScheduler("no-such-scheduler").AsTask();

        (await act.Should().ThrowAsync<KeyNotFoundException>(
            "GetScheduler returns a non-nullable detail, so a name nothing goes by has no other answer"))
            .Which.Message.Should().Contain("no-such-scheduler", "the page shows the name that was not found");
    }

    [Test]
    public async Task AMissingJobIsReportedAsAMissingKey()
    {
        Func<Task> act = () => client.GetJobDetail(key, new JobKeyDto("ghosts", "no-such-job")).AsTask();

        (await act.Should().ThrowAsync<KeyNotFoundException>(
            "the scheduler exists and holds no such job, which is the case the non-nullable JobDetailDto cannot express"))
            .Which.Message.Should().Contain("no-such-job");
    }

    [Test]
    public async Task AMissingTriggerIsReportedAsAMissingKey()
    {
        Func<Task> act = () => client.GetTrigger(key, new TriggerKeyDto("ghosts", "no-such-trigger")).AsTask();

        (await act.Should().ThrowAsync<KeyNotFoundException>())
            .Which.Message.Should().Contain("no-such-trigger");
    }

    private static IEnumerable<TestCaseData> EveryBuiltInCalendar()
    {
        // Kathmandu is UTC+05:45: a zone the wire has to carry by its id, since no default would produce it.
        TimeZoneInfo kathmandu = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kathmandu");
        BaseCalendar under = new(kathmandu) { Description = "under" };

        HolidayCalendar holiday = new(under) { TimeZone = kathmandu, Description = "holiday" };
        holiday.AddExcludedDay(new DateOnly(2030, 12, 25));
        holiday.AddExcludedDay(new DateOnly(2031, 1, 1));
        yield return new TestCaseData("contract-holiday", holiday).SetArgDisplayNames("Holiday");

        AnnualCalendar annual = new(under) { TimeZone = kathmandu, Description = "annual" };
        annual.AddExcludedDay(new MonthDay(2, 29));
        annual.AddExcludedDay(new MonthDay(12, 25));
        yield return new TestCaseData("contract-annual", annual).SetArgDisplayNames("Annual");

        WeeklyCalendar weekly = new(under) { TimeZone = kathmandu, Description = "weekly" };
        weekly.RemoveExcludedDay(DayOfWeek.Saturday);
        weekly.AddExcludedDay(DayOfWeek.Wednesday);
        yield return new TestCaseData("contract-weekly", weekly).SetArgDisplayNames("Weekly");

        MonthlyCalendar monthly = new(under) { TimeZone = kathmandu, Description = "monthly" };
        monthly.AddExcludedDay(1);
        monthly.AddExcludedDay(31);
        yield return new TestCaseData("contract-monthly", monthly).SetArgDisplayNames("Monthly");

        DailyCalendar daily = new(new TimeOnly(9, 30), new TimeOnly(17, 0, 0, 250), under) { TimeZone = kathmandu, Description = "daily", InvertTimeRange = true };
        yield return new TestCaseData("contract-daily", daily).SetArgDisplayNames("Daily");

        CronCalendar cron = new(under, "* * 0-6 ? * MON-FRI", kathmandu) { Description = "cron" };
        yield return new TestCaseData("contract-cron", cron).SetArgDisplayNames("Cron");
    }

    /// <summary>
    /// Each built-in calendar type the dashboard's editor builds is stored through the client and read back as it
    /// was sent, whichever carrier the scheduler is reached by (#4413): its own rules, its time zone, its
    /// description and the calendar under it.
    /// </summary>
    [TestCaseSource(nameof(EveryBuiltInCalendar))]
    public async Task EveryBuiltInCalendarTypeIsStoredAndReadBackAsItWasSent(string name, ICalendar calendar)
    {
        await client.AddCalendar(key, new AddCalendarRequest(name, calendar, Replace: false, UpdateTriggers: false));

        ICalendar read = await client.GetCalendar(key, name);

        read.Should().BeOfType(calendar.GetType());
        read.Should().Be(calendar, "the calendar the page shows is the one the editor sent, rules, zone, description and base alike");
        read.CalendarBase.Should().Be(calendar.CalendarBase);
    }

    /// <summary>
    /// What the Replace question on the calendar pages offers: a replace leaves each trigger's next fire time as it
    /// was unless the triggers are updated, and the count of the triggers that use the calendar is one read with
    /// no trigger in it. 9:00 UTC every day from 5 October 2030, years ahead of every carrier's clock.
    /// </summary>
    [Test]
    public async Task AReplaceUpdatesTheTriggersThatUseTheCalendarOnlyWhenAskedTo()
    {
        JobKey jobKey = new("export", "calendar-contract");
        TriggerKey triggerKey = new("daily", "calendar-contract");
        TriggerKeyDto triggerKeyDto = new(triggerKey.Group, triggerKey.Name);
        DateTimeOffset fifth = new(2030, 10, 5, 9, 0, 0, TimeSpan.Zero);
        await client.AddCalendar(key, new AddCalendarRequest("contract-days", new HolidayCalendar { TimeZone = TimeZoneInfo.Utc }, Replace: false, UpdateTriggers: false));
        await scheduler.AddJob(JobBuilder.Create<DummyJob>().WithIdentity(jobKey).StoreDurably().Build());
        await scheduler.ScheduleJob(TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .StartAt(new DateTimeOffset(2030, 10, 5, 0, 0, 0, TimeSpan.Zero))
            .WithCalendarName("contract-days")
            .WithCronSchedule("0 0 9 * * ?", cron => cron.InTimeZone(TimeZoneInfo.Utc))
            .Build());
        await scheduler.ScheduleJob(TriggerBuilder.Create()
            .WithIdentity("unrelated", "calendar-contract")
            .ForJob(jobKey)
            .StartAt(fifth)
            .Build());

        PagedResult<TriggerHeaderDto> users = await client.QueryTriggers(key, new DashboardTriggerQuery { CalendarName = "contract-days", Take = 0 });
        users.TotalCount.Should().Be(1, "one trigger names the calendar");
        users.Items.Should().BeEmpty("a count reads no trigger");

        HolidayCalendar withoutTheFifth = new() { TimeZone = TimeZoneInfo.Utc };
        withoutTheFifth.AddExcludedDay(new DateOnly(2030, 10, 5));

        await client.AddCalendar(key, new AddCalendarRequest("contract-days", withoutTheFifth, Replace: true, UpdateTriggers: false));
        (await client.GetTrigger(key, triggerKeyDto)).NextFireTimeUtc.Should().Be(fifth,
            "a replace alone leaves the trigger the next fire time it had, though the new calendar excludes it");

        await client.AddCalendar(key, new AddCalendarRequest("contract-days", withoutTheFifth, Replace: true, UpdateTriggers: true));
        (await client.GetTrigger(key, triggerKeyDto)).NextFireTimeUtc.Should().Be(fifth.AddDays(1),
            "updating the triggers recomputes the next fire time against the calendar that replaced the old one");
    }

    [Test]
    public async Task AMissingCalendarIsReportedAsAMissingKey()
    {
        Func<Task> act = () => client.GetCalendar(key, "no-such-calendar").AsTask();

        (await act.Should().ThrowAsync<KeyNotFoundException>())
            .Which.Message.Should().Contain("no-such-calendar");
    }

    /// <summary>
    /// The other half of the contract: a capability the source does not have is a value rather than an
    /// exception, so the overview can draw "cannot say" differently from "nothing is limited".
    /// </summary>
    [Test]
    public async Task ASchedulerThatLimitsNothingReportsNoLimitsRatherThanRefusing()
    {
        ExecutionLimitsDto limits = await client.GetExecutionLimits(key);

        limits.Should().NotBeNull("the member never answers null");
        limits.CanReport.Should().BeTrue("a scheduler Quartz ships can always say what its limits are");
        limits.Limits.Should().BeEmpty("nothing was limited, which is a different fact from being unable to report");
    }

    /// <summary>
    /// What the client schedules is what the scheduler holds, whichever process that scheduler is in.
    /// </summary>
    [Test]
    public async Task WhatIsScheduledThroughTheClientIsWhatTheSchedulerHolds()
    {
        JobKey jobKey = new("nightly", "contract");
        TriggerKey triggerKey = new("at-midnight", "contract");

        JobDetailDto job = new(
            Name: jobKey.Name,
            Group: jobKey.Group,
            JobType: typeof(DummyJob).FullName!,
            Description: "scheduled from the dashboard",
            Durable: true,
            RequestsRecovery: false,
            ConcurrentExecutionDisallowed: false,
            PersistJobDataAfterExecution: false,
            JobDataMap: new JobDataMap { ["colour"] = "green" });

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey.Name, jobKey.Group)
            .WithCronSchedule("0 0 0 * * ?")
            .Build();

        await client.ScheduleJob(key, new ScheduleJobRequest(trigger, job));

        ITrigger? stored = await scheduler.GetTrigger(triggerKey);
        stored.Should().NotBeNull("the scheduler that holds it is the one the client was pointed at");
        stored!.JobKey.Should().Be(jobKey);

        JobDetailDto readBack = await client.GetJobDetail(key, new JobKeyDto(jobKey.Group, jobKey.Name));
        readBack.Name.Should().Be(jobKey.Name);
    }

    /// <summary>
    /// A trigger scheduled paused through the client is stored paused with its reason, whichever process
    /// the scheduler is in: for the HTTP carrier that is the schedule route's <c>paused</c> members, sent
    /// once the host has said it is 4.4 or later (#4018).
    /// </summary>
    [Test]
    public async Task ATriggerScheduledPausedThroughTheClientIsStoredPausedWithItsReason()
    {
        JobKey jobKey = new("nightly", "scheduled-paused");
        await scheduler.AddJob(JobBuilder.Create<DummyJob>().WithIdentity(jobKey).StoreDurably().Build(), new AddJobOptions { Replace = true });

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("now", "scheduled-paused")
            .ForJob(jobKey)
            .StartNow()
            .Build();

        await client.ScheduleJob(key, new ScheduleJobRequest(trigger, Job: null)
        {
            Paused = new PauseDetails { Reason = "awaiting approval", RequestedBy = "alice" }
        });

        (await scheduler.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Paused, "the trigger was stored paused");
        (await client.GetTriggerPause(key, new TriggerKeyDto("scheduled-paused", "now"))).Should().Match<PauseInfo>(
            pause => pause.Reason == "awaiting approval" && pause.RequestedBy == "alice");

        await scheduler.DeleteJob(jobKey);
    }

    /// <summary>
    /// The control panel's three operations mean the same thing whichever process the scheduler is in: an
    /// edit in place, a narrowed listing and a key set.
    /// </summary>
    /// <remarks>
    /// For the HTTP carrier these are the <c>update-details</c> route, the listing's query parameters and
    /// the <c>…/keys/pause</c>, <c>…/keys/resume</c> and <c>…/unschedule</c> key-set routes, so a page
    /// fronting a remote scheduler has nothing to disable.
    /// </remarks>
    /// <summary>
    /// The schedule editor's trigger of every kind, rescheduled through the client, is stored as the editor built it,
    /// whichever process the scheduler runs in (#4412). Over HTTP and through the agent it travels as JSON, so every
    /// kind's schedule has to survive the wire.
    /// </summary>
    [TestCase("Cron")]
    [TestCase("Simple")]
    [TestCase("CalendarInterval")]
    [TestCase("DailyTimeInterval")]
    [TestCase("Recurrence")]
    public async Task EveryKindTheScheduleEditorBuildsIsRescheduledThroughTheClient(string kindName)
    {
        ScheduleKind kind = Enum.Parse<ScheduleKind>(kindName);
        JobKey jobKey = new("rescheduled", "every-kind");
        TriggerKey triggerKey = new("t-" + kindName, "every-kind");
        TriggerKeyDto triggerKeyDto = new(triggerKey.Group, triggerKey.Name);
        await scheduler.AddJob(JobBuilder.Create<DummyJob>().WithIdentity(jobKey).StoreDurably().Build(), new AddJobOptions { Replace = true });

        IOperableTrigger template = ScheduleForm.NewTrigger(kind);
        template.Key = triggerKey;
        template.JobKey = jobKey;
        template.StartTimeUtc = DateTimeOffset.UtcNow.AddDays(-30);
        template.PreferredNode = PreferredNode.For("node-a");
        await scheduler.ScheduleJob(ScheduleForm.ForNew(kind, TimeZoneInfo.Utc.Id).Apply(template, null, TimeProvider.System).Trigger!);

        ITrigger read = await client.GetTrigger(key, triggerKeyDto);
        ScheduleForm form = ScheduleForm.From(read)!;
        switch (kind)
        {
            case ScheduleKind.Cron:
                form.CronExpression = "0 30 6 ? * MON-FRI";
                break;
            case ScheduleKind.Simple:
                form.SimpleInterval = "45";
                form.RepeatForever = false;
                form.RepeatCount = "12";
                break;
            case ScheduleKind.CalendarInterval:
                form.CalendarInterval = "3";
                form.CalendarUnit = IntervalUnit.Week;
                break;
            case ScheduleKind.DailyTimeInterval:
                form.DailyStart = "07:30:00";
                form.DailyEnd = "19:00:00";
                form.DailyDays.Remove(DayOfWeek.Friday);
                form.DailyInterval = "20";
                form.DailyUnit = IntervalUnit.Minute;
                break;
            default:
                form.RecurrenceRule = "FREQ=MONTHLY;BYMONTHDAY=-1;BYHOUR=23;BYMINUTE=0;BYSECOND=0";
                break;
        }

        ScheduleBuild build = form.Apply(read, null, TimeProvider.System);
        build.Errors.Should().BeEmpty();

        await client.RescheduleJob(key, triggerKeyDto, new RescheduleRequest(build.Trigger!));

        ITrigger? stored = await scheduler.GetTrigger(triggerKey);
        stored.Should().BeOfType(read.GetType(), "the kind is what it was");
        ScheduleForm.From(stored!)!.SameScheduleAs(form).Should().BeTrue("the scheduler holds the schedule the editor built");
        stored!.PreferredNode.Node.Should().Be("node-a", "a reschedule must not unpin the trigger");
        stored.CalendarName.Should().BeNull();
        stored.JobKey.Should().Be(jobKey);
        stored.NextFireTimeUtc.Should().NotBeNull().And.BeOnOrAfter(build.Trigger!.NextFireTimeUtc!.Value.AddSeconds(-1),
            "the first fire the editor computed is the one stored, not one from the trigger's start a month ago");

        await scheduler.DeleteJob(jobKey);
    }

    /// <summary>
    /// <em>Add trigger</em>'s trigger of every kind, posted through the client with no job, is stored for the job it
    /// names, whichever process the scheduler runs in (#4412).
    /// </summary>
    [TestCase("Cron")]
    [TestCase("Simple")]
    [TestCase("CalendarInterval")]
    [TestCase("DailyTimeInterval")]
    [TestCase("Recurrence")]
    public async Task ATriggerOfEveryKindIsAddedToAStoredJobThroughTheClient(string kindName)
    {
        ScheduleKind kind = Enum.Parse<ScheduleKind>(kindName);
        JobKey jobKey = new("added-to", "every-kind");
        TriggerKey triggerKey = new("added-" + kindName, "every-kind");
        await scheduler.AddJob(JobBuilder.Create<DummyJob>().WithIdentity(jobKey).StoreDurably().Build(), new AddJobOptions { Replace = true });

        IOperableTrigger template = ScheduleForm.NewTrigger(kind);
        template.Key = triggerKey;
        template.JobKey = jobKey;
        template.Priority = 9;
        template.StartTimeUtc = DateTimeOffset.UtcNow;
        template.JobDataMap = new JobDataMap { ["colour"] = "green" };
        ScheduleBuild build = ScheduleForm.ForNew(kind, TimeZoneInfo.Utc.Id).Apply(template, null, TimeProvider.System, startAtFirstFire: true);
        build.Errors.Should().BeEmpty();

        await client.ScheduleJob(key, new ScheduleJobRequest(build.Trigger!, Job: null));

        ITrigger? stored = await scheduler.GetTrigger(triggerKey);
        stored.Should().NotBeNull();
        ScheduleForm.KindOf(stored!).Should().Be(kind);
        stored!.JobKey.Should().Be(jobKey, "the trigger is added to the stored job, and no job travels with it");
        stored.Priority.Should().Be(9);
        stored.JobDataMap["colour"].Should().Be("green");
        ScheduleForm.From(stored)!.SameScheduleAs(ScheduleForm.ForNew(kind, TimeZoneInfo.Utc.Id)).Should().BeTrue();
        stored.NextFireTimeUtc.Should().NotBeNull();

        await scheduler.DeleteJob(jobKey);
    }

    [Test]
    public async Task ATriggerIsEditedFilteredAndActedOnAsASetThroughTheClient()
    {
        JobKey jobKey = new("nightly", "panel");
        DateTimeOffset soon = DateTimeOffset.UtcNow.AddMinutes(10);
        await scheduler.AddCalendar("holidays", new Impl.Calendar.HolidayCalendar(), new AddCalendarOptions { Replace = true });
        await scheduler.AddJob(JobBuilder.Create<DummyJob>().WithIdentity(jobKey).StoreDurably().Build(), new AddJobOptions { Replace = true });
        await scheduler.ScheduleJob(TriggerBuilder.Create().WithIdentity("soon", "panel").ForJob(jobKey).StartAt(soon).Build());
        await scheduler.ScheduleJob(TriggerBuilder.Create().WithIdentity("later", "panel").ForJob(jobKey).StartAt(soon.AddDays(2)).Build());

        TriggerKeyDto soonKey = new("panel", "soon");
        TriggerKeyDto laterKey = new("panel", "later");

        (await client.UpdateTriggerDetails(key, soonKey, new TriggerDetailsUpdate()
            .WithDescription("edited from the dashboard")
            .WithCalendarName("holidays")
            .WithPriority(8))).Should().BeTrue();
        ITrigger edited = (await scheduler.GetTrigger(new TriggerKey("soon", "panel")))!;
        edited.Description.Should().Be("edited from the dashboard");
        edited.CalendarName.Should().Be("holidays");
        edited.Priority.Should().Be(8);

        async Task<List<string>> Names(DashboardTriggerQuery query) =>
            (await client.QueryTriggers(key, query with { GroupContains = "panel" })).Items.Select(x => x.Name).ToList();

        (await Names(new DashboardTriggerQuery { CalendarName = "holidays" })).Should().Equal(["soon"]);
        (await Names(new DashboardTriggerQuery { NameContains = "lat" })).Should().Equal(["later"]);
        (await Names(new DashboardTriggerQuery { Job = new JobKeyDto("panel", "nightly") })).Should().BeEquivalentTo(["soon", "later"]);
        (await Names(new DashboardTriggerQuery { NextFireTimeBefore = soon.AddHours(1) })).Should().Equal(["soon"],
            "the bound travels as an instant with its offset, and the server reads the same moment");

        List<TriggerKeyDto> selection = [soonKey, laterKey, new("panel", "gone")];
        (await client.PauseTriggers(key, selection)).Should().BeEquivalentTo([soonKey, laterKey]);
        (await client.ResumeTriggers(key, selection)).Should().BeEquivalentTo([soonKey, laterKey]);
        (await client.UnscheduleJobs(key, selection)).Should().BeEquivalentTo([soonKey, laterKey],
            "a key that names nothing is left out of the answer, over both carriers");
        (await scheduler.GetTriggersOfJob(jobKey)).Should().BeEmpty();
    }

    /// <summary>
    /// A selection paused with a reason keeps it, whichever process the scheduler is in: for the HTTP
    /// carrier that is one <c>…/keys/pause</c> request carrying the reason beside the keys (#3964).
    /// </summary>
    [Test]
    public async Task ASetPausedWithAReasonKeepsItThroughTheClient()
    {
        JobKey jobKey = new("nightly", "set-pause");
        DateTimeOffset soon = DateTimeOffset.UtcNow.AddMinutes(10);
        await scheduler.AddJob(JobBuilder.Create<DummyJob>().WithIdentity(jobKey).StoreDurably().Build(), new AddJobOptions { Replace = true });
        await scheduler.ScheduleJob(TriggerBuilder.Create().WithIdentity("soon", "set-pause").ForJob(jobKey).StartAt(soon).Build());
        await scheduler.ScheduleJob(TriggerBuilder.Create().WithIdentity("later", "set-pause").ForJob(jobKey).StartAt(soon.AddDays(2)).Build());

        TriggerKeyDto soonKey = new("set-pause", "soon");
        TriggerKeyDto laterKey = new("set-pause", "later");
        PauseDetails details = new() { Reason = "vendor outage", RequestedBy = "alice" };

        (await client.PauseTriggersWith(key, [laterKey, new("set-pause", "gone"), soonKey], details))
            .Should().Equal([laterKey, soonKey], "a key that names nothing is left out, over both carriers");
        (await client.GetTriggerPause(key, soonKey)).Should().Match<PauseInfo>(
            pause => pause.Reason == "vendor outage" && pause.RequestedBy == "alice");
        (await scheduler.GetTriggerPause(new TriggerKey("later", "set-pause")))!.Reason.Should().Be("vendor outage");

        (await client.ResumeTriggers(key, [soonKey, laterKey])).Should().HaveCount(2);
        (await scheduler.GetTriggerPause(new TriggerKey("soon", "set-pause"))).Should().BeNull("resuming forgets the record");

        (await client.PauseJobsWith(key, [new JobKeyDto("set-pause", "nightly")], new PauseDetails { Reason = "quarter close" }))
            .Should().Equal([new JobKeyDto("set-pause", "nightly")]);
        (await scheduler.GetTriggerPause(new TriggerKey("soon", "set-pause")))!.Reason.Should().Be("quarter close");

        await scheduler.DeleteJob(jobKey);
    }

    /// <summary>
    /// A group paused through the client is the group's pause, whichever process the scheduler is in: a job
    /// added afterwards is held, and resuming the group lifts it (#4319). For the other two carriers each is
    /// one group request, <c>…/jobs/pause</c> or <c>…/triggers/pause</c>, rather than one per member.
    /// </summary>
    [Test]
    public async Task AGroupPausedThroughTheClientHoldsWhatIsAddedToItLater()
    {
        const string group = "group-pause";
        TriggerKey later = new("later", group);
        DateTimeOffset soon = DateTimeOffset.UtcNow.AddHours(1);

        await client.PauseJobGroupWith(key, group, new PauseDetails { Reason = "quarter close", RequestedBy = "alice" });

        JobKey jobKey = new("nightly", group);
        await scheduler.ScheduleJob(
            JobBuilder.Create<DummyJob>().WithIdentity(jobKey).StoreDurably().Build(),
            TriggerBuilder.Create().WithIdentity(later).ForJob(jobKey).StartAt(soon).Build());
        (await scheduler.GetTriggerState(later)).Should().Be(TriggerState.Paused,
            "the group was paused before the job was added, and only a pause of the group holds it");
        (await client.GetJobGroupPause(key, group))!.Reason.Should().Be("quarter close");

        await client.ResumeJobGroup(key, group);
        (await scheduler.GetTriggerState(later)).Should().Be(TriggerState.Normal);
        (await client.QueryJobGroups(key, new DashboardGroupQuery { Paused = true })).Items.Should().NotContain(g => g.Name == group);

        await client.PauseTriggerGroupWith(key, group, details: null);
        await scheduler.ScheduleJob(TriggerBuilder.Create().WithIdentity("latest", group).ForJob(jobKey).StartAt(soon).Build());
        (await scheduler.GetTriggerState(new TriggerKey("latest", group))).Should().Be(TriggerState.Paused);

        await client.ResumeTriggerGroup(key, group);
        (await scheduler.GetTriggerState(new TriggerKey("latest", group))).Should().Be(TriggerState.Normal);
        (await client.QueryTriggerGroups(key, new DashboardGroupQuery { Paused = true })).Items.Should().NotContain(g => g.Name == group);

        await scheduler.DeleteJob(jobKey);
    }

    /// <summary>
    /// The header search's two reads (#4411), through every carrier: the jobs and the triggers whose names
    /// contain the text, eight of each, with the total its <em>See all</em> link counts.
    /// </summary>
    [Test]
    public async Task TheHeaderSearchFindsJobsAndTriggersByNameThroughTheClient()
    {
        const string group = "header-search";
        for (int index = 0; index < 10; index++)
        {
            JobKey jobKey = new("invoice-" + index.ToString("00", CultureInfo.InvariantCulture), group);
            await scheduler.ScheduleJob(
                JobBuilder.Create<DummyJob>().WithIdentity(jobKey).Build(),
                TriggerBuilder.Create().WithIdentity(jobKey.Name + "-nightly", group).ForJob(jobKey).StartAt(DateTimeOffset.UtcNow.AddHours(1)).Build());
        }

        await scheduler.ScheduleJob(
            JobBuilder.Create<DummyJob>().WithIdentity("payroll", group).Build(),
            TriggerBuilder.Create().WithIdentity("payroll-monthly", group).StartAt(DateTimeOffset.UtcNow.AddHours(1)).Build());

        PagedResult<JobKeyDto> jobs = await client.QueryJobs(key, new DashboardJobQuery { NameContains = "invoice", Take = 8 });
        PagedResult<TriggerHeaderDto> triggers = await client.QueryTriggers(key, new DashboardTriggerQuery { NameContains = "invoice", Take = 8 });

        jobs.Items.Should().HaveCount(8).And.OnlyContain(job => job.Name.StartsWith("invoice-", StringComparison.Ordinal));
        jobs.TotalCount.Should().Be(10);
        jobs.HasMore.Should().BeTrue();
        triggers.Items.Should().HaveCount(8).And.OnlyContain(trigger => trigger.Name.EndsWith("-nightly", StringComparison.Ordinal));
        triggers.TotalCount.Should().Be(10);

        await scheduler.DeleteJobs([.. Enumerable.Range(0, 10).Select(index => new JobKey("invoice-" + index.ToString("00", CultureInfo.InvariantCulture), group)), new JobKey("payroll", group)]);
    }

    /// <summary>
    /// Trigger Detail's "Why isn't it firing?" reads the same through every carrier (#4411): the scheduler's
    /// status, the paused groups and the job group's pause, the job's firings by the job filter, the limits and
    /// the trigger's misfires, each over the route a page already reads.
    /// </summary>
    [Test]
    public async Task ATriggerIsDiagnosedThroughTheClient()
    {
        const string group = "diagnosis";
        JobKey jobKey = new("nightly", group);
        TriggerKey triggerKey = new("at-three", group);
        await scheduler.ScheduleJob(
            JobBuilder.Create<DummyJob>().WithIdentity(jobKey).StoreDurably().Build(),
            TriggerBuilder.Create().WithIdentity(triggerKey).ForJob(jobKey).StartAt(DateTimeOffset.UtcNow.AddHours(1)).Build());
        await client.PauseJobGroupWith(key, group, new PauseDetails { Reason = "quarter close", RequestedBy = "alice" });
        await client.PauseTriggerGroupWith(key, group, new PauseDetails { Reason = "vendor outage", RequestedBy = "bob" });
        await history.AddMisfire(Misfire(DateTimeOffset.UtcNow.AddMinutes(-5), "at-three") with { TriggerGroup = group, JobKey = jobKey });

        TriggerKeyDto dto = new(group, "at-three");
        ITrigger trigger = await client.GetTrigger(key, dto);
        TriggerState state = await client.GetTriggerState(key, dto);
        TriggerDiagnosisFacts facts = await TriggerDiagnosis.Read(
            client,
            new DiagnosisSubject(key, trigger, state, Pause: null, ContinuationParentExists: true, DateTimeOffset.UtcNow));
        Dictionary<string, DiagnosisCheck> checks = TriggerDiagnosis.Evaluate(facts, value => value?.ToString("u", CultureInfo.InvariantCulture) ?? "-")
            .ToDictionary(check => check.Name);

        checks["Scheduler"].Verdict.Should().Be(DiagnosisVerdict.Problem, "the fixture's scheduler is never started, on every carrier");
        checks["Trigger"].Text.Should().StartWith("Paused");
        checks["Job group"].Text.Should().StartWith("'diagnosis' is paused by alice at").And.Contain("(quarter close)");
        checks["Trigger group"].Text.Should().StartWith("'diagnosis' is paused by bob at").And.Contain("(vendor outage)");
        checks["Its job's firings"].Text.Should().Be("None running.");
        checks["Execution group"].Text.Should().Be("No execution group: unlimited.");
        checks["Misfires"].Text.Should().StartWith("1 recorded; the latest at");
        checks.Values.Should().NotContain(check => check.Verdict == DiagnosisVerdict.Unknown, "every read answers through every carrier");

        await client.ResumeTriggerGroup(key, group);
        await scheduler.DeleteJob(jobKey);
    }

    /// <summary>
    /// The Trigger Detail page's backfill, through the client: the extension over the scheduler the name
    /// resolves to, which for the HTTP carrier is <see cref="HttpScheduler" /> sending the host's
    /// <c>backfill</c> route.
    /// </summary>
    [Test]
    public async Task ATriggerIsBackfilledThroughTheClient()
    {
        JobKey jobKey = new("hourly-export", "backfill-contract");
        TriggerKey triggerKey = new("hourly", "backfill-contract");
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        DateTimeOffset hour = new(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero);
        await scheduler.ScheduleJob(
            JobBuilder.Create<DummyJob>().WithIdentity(jobKey).Build(),
            TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .ForJob(jobKey)
                .StartAt(hour.AddDays(-1))
                .WithCronSchedule("0 0 * * * ?", cron => cron.InTimeZone(TimeZoneInfo.Utc))
                .Build());

        BackfillResult result = await client.Backfill(
            key, new TriggerKeyDto(triggerKey.Group, triggerKey.Name), hour.AddHours(-4), hour);

        result.Scheduled.Should().Be(4, "an hourly trigger has four fire times in four hours");
        (await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals("backfill:backfill-contract")))
            .Should().BeEquivalentTo(result.ScheduledTriggers, "the scheduler holds what the client says it scheduled, over both carriers");
    }

    /// <summary>
    /// A trigger's upcoming fire times are read through the client, computed where the scheduler runs: for the
    /// HTTP and agent carriers by the <c>fire-times</c> route <see cref="HttpScheduler" /> sends.
    /// </summary>
    /// <remarks>
    /// 09:00 in Kathmandu, UTC+05:45, every day from 5 October 2030, under a calendar that excludes the 7th
    /// there: years ahead of every carrier's clock, so no carrier's "now" moves the first fire time.
    /// </remarks>
    [Test]
    public async Task UpcomingFireTimesAreReadThroughTheClient()
    {
        TimeZoneInfo kathmandu = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kathmandu");
        HolidayCalendar holidays = new() { TimeZone = kathmandu };
        holidays.AddExcludedDay(new DateOnly(2030, 10, 7));
        await scheduler.AddCalendar("fire-times-holidays", holidays);

        JobKey jobKey = new("daily-export", "fire-times-contract");
        TriggerKey triggerKey = new("daily", "fire-times-contract");
        await scheduler.ScheduleJob(
            JobBuilder.Create<DummyJob>().WithIdentity(jobKey).Build(),
            TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .ForJob(jobKey)
                .StartAt(new DateTimeOffset(2030, 10, 5, 0, 0, 0, TimeSpan.Zero))
                .WithCalendarName("fire-times-holidays")
                .WithCronSchedule("0 0 9 * * ?", cron => cron.InTimeZone(kathmandu))
                .Build());

        List<DateTimeOffset> fireTimes = await client.GetUpcomingFireTimes(key, new TriggerKeyDto(triggerKey.Group, triggerKey.Name), 3);

        DateTimeOffset nine = new(2030, 10, 5, 3, 15, 0, TimeSpan.Zero);
        fireTimes.Should().Equal([nine, nine.AddDays(1), nine.AddDays(3)],
            "the trigger's own zone and calendar decide its fire times, whichever carrier the page reads them through");
    }

    /// <summary>
    /// The agenda is computed where the triggers are stored and reads the same through every carrier: the fires of
    /// every trigger in the window, earliest first and ties by key, with the window's end excluded (#4438).
    /// </summary>
    [Test]
    public async Task TheAgendaIsReadThroughTheClient()
    {
        DateTimeOffset start = await ScheduleAgendaTriggers();

        UpcomingFiresDto agenda = await client.QueryUpcomingFires(key, new DashboardUpcomingFiresQuery { From = start, To = start.AddHours(2) });

        agenda.FromUtc.Should().Be(start, "the start asked for is after every carrier's now");
        agenda.ToUtc.Should().Be(start.AddHours(2));
        agenda.Fires.Select(fire => (fire.FireTimeUtc - start, fire.Trigger.Name)).Should().Equal(
            [
                (TimeSpan.FromMinutes(30), "a-first"),
                (TimeSpan.FromMinutes(50), "a-first"),
                (TimeSpan.FromMinutes(50), "b-second"),
                (TimeSpan.FromMinutes(70), "a-first"),
                (TimeSpan.FromMinutes(70), "b-second"),
                (TimeSpan.FromMinutes(90), "a-first"),
                (TimeSpan.FromMinutes(90), "b-second"),
                (TimeSpan.FromMinutes(110), "a-first"),
                (TimeSpan.FromMinutes(110), "b-second"),
            ],
            "earliest first, a tie by trigger group then name, and nothing at the window's end");
        agenda.Fires.Should().AllSatisfy(fire =>
        {
            fire.Trigger.Group.Should().Be("agenda-contract");
            fire.Job.Should().Be(new JobKeyDto("agenda-contract", "agenda-job"));
            fire.State.Should().Be(TriggerState.Normal);
            fire.ScheduleDescription.Should().Be("Every 20 minutes");
            fire.OnlyTheNext.Should().BeFalse();
        });
        agenda.Cut.Should().BeFalse();
        agenda.CutAtUtc.Should().BeNull();
        agenda.TriggersNotComputed.Should().Be(0);
    }

    [Test]
    public async Task AnAgendaThatReachesItsTakeSaysWhereItWasCut()
    {
        DateTimeOffset start = await ScheduleAgendaTriggers();

        UpcomingFiresDto agenda = await client.QueryUpcomingFires(key, new DashboardUpcomingFiresQuery { From = start, To = start.AddHours(2), Take = 3 });

        agenda.Fires.Should().HaveCount(3);
        agenda.Cut.Should().BeTrue();
        agenda.CutAtUtc.Should().Be(start.AddMinutes(50), "fires at or after the last one listed may be missing");
    }

    /// <summary>
    /// A refusal has one wording on every path: the scheduler's own words, raised as an argument the page shows.
    /// </summary>
    [Test]
    public async Task AnAgendaWindowLongerThanSevenDaysIsRefusedInTheSameWordsThroughEveryCarrier()
    {
        DateTimeOffset start = TimeProvider.System.GetUtcNow().AddHours(1);

        Func<Task> act = () => client.QueryUpcomingFires(key, new DashboardUpcomingFiresQuery { From = start, To = start.AddDays(8) }).AsTask();

        (await act.Should().ThrowAsync<ArgumentException>())
            .Which.Message.Should().Contain("is longer than 7 days: narrow it.");
    }

    /// <summary>
    /// Two triggers every 20 minutes, from half an hour and from 50 minutes after a start a few minutes ahead of the
    /// system clock, which every carrier's scheduler runs on.
    /// </summary>
    private async Task<DateTimeOffset> ScheduleAgendaTriggers()
    {
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        DateTimeOffset start = new DateTimeOffset(now.UtcTicks - now.UtcTicks % TimeSpan.TicksPerMinute, TimeSpan.Zero).AddMinutes(10);

        JobKey jobKey = new("agenda-job", "agenda-contract");
        await scheduler.AddJob(JobBuilder.Create<DummyJob>().WithIdentity(jobKey).StoreDurably().Build());
        foreach ((string name, int minutes) in ((string, int)[]) [("b-second", 50), ("a-first", 30)])
        {
            await scheduler.ScheduleJob(TriggerBuilder.Create()
                .WithIdentity(name, "agenda-contract")
                .ForJob(jobKey)
                .StartAt(start.AddMinutes(minutes))
                .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMinutes(20)).RepeatForever())
                .Build());
        }

        return start;
    }

    [Test]
    public async Task TheFireTimesOfAMissingTriggerAreReportedAsAMissingKey()
    {
        Func<Task> act = () => client.GetUpcomingFireTimes(key, new TriggerKeyDto("ghosts", "no-such-trigger"), 3).AsTask();

        (await act.Should().ThrowAsync<KeyNotFoundException>(
            "the answer is a non-nullable list, so a trigger that is not there has no other answer than the one GetTrigger gives"))
            .Which.Message.Should().Contain("no-such-trigger");
    }

    /// <summary>
    /// The history of what a scheduler has run is readable through the client, filters and paging
    /// included — from the process the scheduler runs in.
    /// </summary>
    /// <remarks>
    /// Recorded where the scheduler is, which is the point: for the HTTP carrier that is the host's own
    /// store, reached over the history routes. A dashboard fronting a scheduler over HTTP used to show an
    /// empty History page with nothing to say why.
    /// </remarks>
    [Test]
    public async Task TheHistoryIsReadFromTheProcessTheSchedulerRunsIn()
    {
        DateTimeOffset firedAt = DateTimeOffset.UtcNow;
        await history.AddExecution(Execution(firedAt, "nightly", node: "node-a", triggerName: "at-midnight"));
        await history.AddExecution(Execution(firedAt.AddSeconds(1), "hourly", node: "node-b", triggerName: "on-the-hour"));

        PagedResult<DashboardHistoryEntry> all = await client.QueryExecutions(new DashboardHistoryQuery
        {
            SchedulerName = key,
            IncludeTotalCount = true
        });
        all.Items.Select(entry => entry.JobName).Should().Equal(["hourly", "nightly"], "a history page reads newest first");
        all.TotalCount.Should().Be(2);

        PagedResult<DashboardHistoryEntry> onNodeA = await client.QueryExecutions(new DashboardHistoryQuery
        {
            SchedulerName = key,
            SchedulerInstanceId = "node-a"
        });
        onNodeA.Items.Should().ContainSingle().Which.JobName.Should().Be("nightly",
            "a cluster's history is unreadable until it can be narrowed to one machine");

        PagedResult<DashboardHistoryEntry> byJob = await client.QueryExecutions(new DashboardHistoryQuery
        {
            SchedulerName = key,
            JobFilter = "night"
        });
        byJob.Items.Should().ContainSingle().Which.JobName.Should().Be("nightly");

        PagedResult<DashboardHistoryEntry> byTrigger = await client.QueryExecutions(new DashboardHistoryQuery
        {
            SchedulerName = key,
            TriggerFilter = "on-the-hour"
        });
        byTrigger.Items.Should().ContainSingle().Which.JobName.Should().Be("hourly");

        PagedResult<DashboardHistoryEntry> firstPage = await client.QueryExecutions(new DashboardHistoryQuery
        {
            SchedulerName = key,
            Take = 1,
            IncludeTotalCount = true
        });
        firstPage.Items.Should().ContainSingle().Which.JobName.Should().Be("hourly");
        firstPage.HasMore.Should().BeTrue("paging means the same thing over both carriers");
    }

    [Test]
    public async Task TheMisfiresOfTheSchedulerAreCountedThroughTheClient()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await history.AddMisfire(Misfire(now.AddMinutes(-30), "long-ago"));
        await history.AddMisfire(Misfire(now, "just-now"));

        PagedResult<DashboardMisfireEntry> listed = await client.QueryMisfires(new DashboardMisfireQuery
        {
            SchedulerName = key
        });
        listed.Items.Select(entry => entry.TriggerName).Should().Equal(["just-now", "long-ago"]);
        listed.Items[0].JobKey!.Name.Should().Be("DummyJob", "a misfire says which job did not run");

        int recent = await client.CountMisfires(key, now.AddMinutes(-10));
        recent.Should().Be(1, "the overview's tile asks for a count over a window, not for a page");
    }

    /// <summary>
    /// What a run achieved, the 4.4 filters, a vetoed firing and each job's run status read the same
    /// through the client over both carriers.
    /// </summary>
    /// <remarks>
    /// For the HTTP carrier the filters go out only after the host has said it is 4.4 or later, the veto
    /// only because the client asks for every reason by name, and the statuses through the
    /// <c>…/history/job-status</c> routes.
    /// </remarks>
    [Test]
    public async Task RunResultsFiltersVetoesAndStatusesAreReadThroughTheClient()
    {
        DateTimeOffset start = DateTimeOffset.UtcNow.AddMinutes(-10);
        ExecutionHistoryEntry skipped = Execution(start.AddMinutes(1), "healthy") with
        {
            EntryId = "entry-skipped",
            Result = JobRunResult.Skipped,
            Summary = "no stale reservations",
            MetricsJson = """{"scanned":1200,"released":0}""",
            Manual = true,
            FireInstanceId = "node-a-17"
        };

        await history.AddExecution(Execution(start, "healthy") with { Result = JobRunResult.Succeeded });
        await history.AddExecution(skipped);
        await history.AddExecution(Failure(start, "sick"));
        await history.AddExecution(Failure(start.AddMinutes(1), "sick"));
        await history.AddMisfire(Misfire(start, "vetoed") with { Reason = MisfireReason.Vetoed });

        DashboardHistoryEntry row = (await client.QueryExecutions(new DashboardHistoryQuery
        {
            SchedulerName = key,
            Job = new JobKeyDto("contract", "healthy"),
            Results = [JobRunResult.Skipped]
        })).Items.Should().ContainSingle().Subject;

        row.EffectiveResult.Should().Be(JobRunResult.Skipped);
        row.Summary.Should().Be("no stale reservations");
        row.MetricsJson.Should().Be("""{"scanned":1200,"released":0}""", "the metrics come back as the text the recorder wrote");
        row.Manual.Should().BeTrue();
        row.FireInstanceId.Should().Be("node-a-17");

        (await client.QueryExecutions(new DashboardHistoryQuery
        {
            SchedulerName = key,
            FiredFrom = start.AddSeconds(30)
        })).Items.Select(entry => entry.JobName).Should().BeEquivalentTo(["healthy", "sick"], "the window starts after the first two runs");

        (await client.QueryMisfires(new DashboardMisfireQuery { SchedulerName = key }))
            .Items.Should().ContainSingle().Which.Reason.Should().Be(MisfireReason.Vetoed,
                "the dashboard can read a veto, so it asks for it, over both carriers");

        (await client.GetJobRunStatus(key, new JobKeyDto("contract", "sick")))!
            .ConsecutiveFailures.Should().Be(2);
        (await client.GetJobRunStatus(key, new JobKeyDto("contract", "never-ran"))).Should().BeNull();

        List<JobRunStatus> statuses = await client.GetJobRunStatuses(
            key,
            [new JobKeyDto("contract", "sick"), new JobKeyDto("contract", "healthy"), new JobKeyDto("contract", "never-ran")]);
        statuses.Select(status => status.Job.Name).Should().Equal(["healthy", "sick"],
            "by job group and then name, and a job with no recorded run is absent");
        statuses[0].LastResult.Should().Be(JobRunResult.Skipped);
        statuses[0].SchedulerName.Should().Be(scheduler.SchedulerName);

        // The Overview's failing jobs (#4411): one read of the statuses failing in a row, by the scheduler's key.
        PagedResult<JobRunStatus> failing = await client.QueryJobRunStatuses(
            new JobRunStatusQuery { SchedulerName = key, Failing = true, IncludeTotalCount = true });
        failing.Items.Should().ContainSingle().Which.Job.Name.Should().Be("sick");
        failing.Items[0].ConsecutiveFailures.Should().Be(2);
        failing.TotalCount.Should().Be(1);
    }

    /// <summary>
    /// The per-trigger run statuses read the same through the client over every carrier: one read naming a page's
    /// triggers, from the store the history is read from, and for a remote one through
    /// <c>…/history/trigger-status/fetch</c>.
    /// </summary>
    [Test]
    public async Task TriggerRunStatusesAreReadThroughTheClientForAPagesTriggers()
    {
        DateTimeOffset start = DateTimeOffset.UtcNow.AddMinutes(-10);
        await history.AddExecution(Execution(start, "report", triggerName: "daily") with { Result = JobRunResult.Succeeded });
        await history.AddExecution(Failure(start, "report") with { TriggerName = "hourly" });
        await history.AddExecution(Failure(start.AddMinutes(1), "report") with { TriggerName = "hourly" });

        TriggerKey[] page = [new("hourly", "contract"), new("daily", "contract"), new("never-fired", "contract")];

        PagedResult<TriggerRunStatus> statuses = await client.QueryTriggerRunStatuses(new TriggerRunStatusQuery
        {
            SchedulerName = key,
            Triggers = page,
            Take = PagedQuery.All
        });

        statuses.Items.Select(status => status.Trigger.Name).Should().Equal(["daily", "hourly"],
            "by trigger group and then name, and a trigger with no recorded run is absent");
        statuses.Items[0].LastResult.Should().Be(JobRunResult.Succeeded);
        statuses.Items[0].SchedulerName.Should().Be(scheduler.SchedulerName);
        statuses.Items[1].ConsecutiveFailures.Should().Be(2, "one job, and only its hourly schedule is failing");
        statuses.Items[1].LastFailureMessage.Should().Be("the upstream system is down");

        (await client.QueryTriggerRunStatuses(new TriggerRunStatusQuery { SchedulerName = key, Triggers = page, Failing = true }))
            .Items.Should().ContainSingle().Which.Trigger.Name.Should().Be("hourly");
    }

    /// <summary>
    /// The run statistics read the same through the client over both carriers: from the store the history is
    /// read from, and for the HTTP carrier through <c>…/history/statistics</c> once the host has said it is 4.4.
    /// </summary>
    [Test]
    public async Task RunStatisticsAreReadThroughTheClient()
    {
        DateTimeOffset start = DateTimeOffset.UtcNow.AddMinutes(-10);
        await history.AddExecution(Execution(start, "healthy") with { Result = JobRunResult.Succeeded, Duration = TimeSpan.FromMilliseconds(100) });
        await history.AddExecution(Failure(start.AddMinutes(1), "sick") with { Duration = TimeSpan.FromMilliseconds(300) });

        ExecutionStatistics statistics = await client.QueryExecutionStatistics(new ExecutionStatisticsQuery
        {
            SchedulerName = key,
            JobGroup = "contract",
            BucketSize = TimeSpan.FromDays(1)
        });

        statistics.Buckets.Sum(bucket => bucket.RunCount).Should().Be(2);
        statistics.Buckets.Sum(bucket => bucket.FailedCount).Should().Be(1);
        statistics.Buckets.Max(bucket => bucket.MaxDuration).Should().Be(TimeSpan.FromMilliseconds(300));
        statistics.BucketSize.Should().Be(TimeSpan.FromDays(1));
    }

    /// <summary>
    /// What a scheduler does is watchable from this container, from the process the scheduler runs in.
    /// </summary>
    /// <remarks>
    /// The Live Logs page resolves its stream exactly this way, so running it over both carriers is what
    /// stops the remote one drifting: for the HTTP carrier the event is raised on the host, written as a
    /// frame by the event route and read back by the target's reader, and the page cannot tell that from an
    /// event published into this process's broker.
    /// </remarks>
    [Test]
    public async Task TheEventsAreWatchedFromTheProcessTheSchedulerRunsIn()
    {
        ISchedulerEventSource? source = SchedulerEventLookup.For(provider, key);
        source.Should().NotBeNull("a dashboard reads the events of whatever scheduler it renders");

        using CancellationTokenSource subscription = new();
        await using IAsyncEnumerator<SchedulerEvent> events = source!
            .Subscribe(scheduler.SchedulerName, subscription.Token)
            .GetAsyncEnumerator(CancellationToken.None);

        Task<bool> reading = events.MoveNextAsync().AsTask();

        // A stream carries what happens after a subscription is made, so nothing is raised until the
        // scheduler's own process has one.
        using CancellationTokenSource waiting = new(TimeSpan.FromSeconds(30));
        while (!broker.HasSubscribers(scheduler.SchedulerName))
        {
            await Task.Delay(10, waiting.Token);
        }

        await scheduler.AddJob(
            JobBuilder.Create<DummyJob>().WithIdentity("watched", "contract").StoreDurably().Build(),
            new AddJobOptions { Replace = true });
        (await scheduler.PauseJob(new JobKey("watched", "contract"))).Should().BeTrue();

        (await reading.WaitAsync(TimeSpan.FromSeconds(30))).Should().BeTrue("the scheduler raised an event");

        SchedulerEvent watched = events.Current;
        watched.Kind.Should().Be(SchedulerEventKind.JobPaused);
        watched.JobKey.Should().Be(new KeyDto("watched", "contract"));
        watched.SchedulerName.Should().Be(scheduler.SchedulerName);
        watched.SchedulerInstanceId.Should().Be(scheduler.SchedulerInstanceId,
            "the node is the one that raised it, which for a remote target is somebody else's process");

        await subscription.CancelAsync();
    }

    /// <summary>
    /// A scheduler in another process is listed as one, and the listing says which node answered.
    /// </summary>
    [Test]
    public async Task TheListingSaysWhereTheSchedulerIs()
    {
        List<SchedulerHeaderDto> schedulers = await client.GetSchedulers();

        SchedulerHeaderDto header = schedulers.Should().ContainSingle(x => x.SchedulerName == scheduler.SchedulerName).Subject;

        switch (carrier)
        {
            case Carrier.Http:
                header.Origin.Should().Be(SchedulerOrigin.Remote,
                    "nothing in this process runs it, and every page about it is about somebody else's process");
                break;
            case Carrier.Agent:
                header.Origin.Should().Be(SchedulerOrigin.Agent, "the process dialled in rather than being dialled");
                header.Key.Should().Be(key);
                header.LastSeenUtc.Should().NotBeNull("an agent is heard, so the listing says when");
                break;
            default:
                header.Origin.Should().Be(SchedulerOrigin.Container);
                break;
        }

        header.SchedulerInstanceId.Should().Be(scheduler.SchedulerInstanceId,
            "the listing carries the node, asked for once and asynchronously rather than read off a blocking property");
        header.Status.Should().NotBeNull();
    }

    /// <summary>
    /// More rows than one page holds are read a page at a time, and asking for all of them against a
    /// server that caps pages is refused rather than silently truncated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The truncation guard is <c>HttpScheduler.WholeAnswer</c>: <c>take=all</c> travels as a request for
    /// everything, and a server with <c>QuartzHttpApiOptions.MaxPageSize</c> set answers it with that
    /// many rows and a <c>hasMore</c>. Handing those back as the whole store is the one answer worse than
    /// no answer, so the client refuses — which is why every dashboard listing loops on <c>HasMore</c>
    /// with a page size of its own.
    /// </para>
    /// <para>
    /// The cap has to be at or above the page size the caller asks with: a <em>number</em> above it is a
    /// <c>400</c>, so a server capped below <see cref="PagedQuery.DefaultTake" /> refuses the dashboard's
    /// own page rather than truncating it.
    /// </para>
    /// </remarks>
    [Test]
    public async Task MoreRowsThanOnePageHoldsAreReadAPageAtATime()
    {
        if (carrier == Carrier.Local)
        {
            Assert.Ignore("the cap is the carrier's, and a local client has no wire to be capped on");
        }

        const int Jobs = 1001;
        for (int index = 0; index < Jobs; index++)
        {
            await scheduler.AddJob(
                JobBuilder.Create<DummyJob>()
                    .WithIdentity("job" + index.ToString("D4", System.Globalization.CultureInfo.InvariantCulture), "bulk")
                    .StoreDurably()
                    .Build(),
                new AddJobOptions { Replace = true });
        }

        List<JobKeyDto> collected = [];
        DashboardJobQuery query = new() { GroupContains = "bulk", Take = PagedQuery.DefaultTake };
        while (true)
        {
            PagedResult<JobKeyDto> page = await client.QueryJobs(key, query);
            collected.AddRange(page.Items);

            if (!page.HasMore || page.Items.Count == 0)
            {
                break;
            }

            query = query with { Skip = query.Skip + page.Items.Count };
        }

        collected.Should().HaveCount(Jobs, "a page at a time is how a listing bigger than one page is read");

        Func<Task> askForEverything = () => client.QueryJobs(
            key,
            new DashboardJobQuery { GroupContains = "bulk", Take = PagedQuery.All }).AsTask();

        await askForEverything.Should().ThrowAsync<HttpClientException>(
            "the carrier caps what 'everything' means, and a truncated page handed back as the whole store is worse than an error");
    }

    private ExecutionHistoryEntry Execution(
        DateTimeOffset firedAt,
        string jobName,
        string node = "node-a",
        string triggerName = "DummyTrigger") => new(
        SchedulerName: scheduler.SchedulerName,
        SchedulerInstanceId: node,
        JobGroup: "contract",
        JobName: jobName,
        TriggerGroup: "contract",
        TriggerName: triggerName,
        FiredAtUtc: firedAt,
        Duration: TimeSpan.FromMilliseconds(5),
        Succeeded: true,
        ExceptionMessage: null);

    private ExecutionHistoryEntry Failure(DateTimeOffset firedAt, string jobName) => Execution(firedAt, jobName) with
    {
        Succeeded = false,
        ExceptionMessage = "the upstream system is down",
        Result = JobRunResult.Failed
    };

    private MisfireHistoryEntry Misfire(DateTimeOffset misfiredAt, string triggerName) => new(
        SchedulerName: scheduler.SchedulerName,
        SchedulerInstanceId: "node-a",
        TriggerGroup: "contract",
        TriggerName: triggerName,
        JobKey: new JobKey("DummyJob", "contract"),
        MisfiredAtUtc: misfiredAt,
        ScheduledFireTimeUtc: misfiredAt.AddMinutes(-5));
}
