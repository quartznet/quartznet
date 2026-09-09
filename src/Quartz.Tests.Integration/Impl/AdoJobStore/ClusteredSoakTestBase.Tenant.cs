using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

using Microsoft.Extensions.DependencyInjection;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The soak's third scheduler: one nobody registered, added to node A's container after that container
/// was built, and taken apart and put back together throughout the run.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it is for.</b> The two clustered nodes are a deployment that starts once and runs. A
/// multi-tenant process is not: tenants arrive, are reconfigured, and go, and each of those is a
/// scheduler being built or shut down beside schedulers that are firing. <see cref="ISchedulerRuntime" />
/// is the door for that, and its unit tests answer the questions a unit test can — the order of build,
/// drain and create, and what each refusal protects. What they cannot answer is what a hundred rebuilds
/// against a real database over half an hour leave behind, which is this.
/// </para>
/// <para>
/// <b>It is a tenant, not a third node.</b> It has its own <c>SCHED_NAME</c> in the same tables, which
/// is the arrangement <c>SharedDatabaseTenancyTestBase</c> pins and the one multi-tenancy is built on,
/// and its store is not clustered — so every generation's first act is the recovery sweep over the whole
/// scheduler name that <see cref="ISchedulerRuntime.Restart" /> orders its steps around. The cluster's
/// own assertions are scoped to <c>ClusterSoakTest</c> and this scheduler's rows are under
/// <c>soak-tenant</c>, so "the cluster is unaffected" is asserted rather than assumed.
/// </para>
/// <para>
/// <b>Its store is registered by type and by factory, never as an instance.</b> A recipe that closes
/// over an <see cref="Extensibility.IJobStore" />, an <see cref="Extensibility.IThreadPool" />, a job
/// factory or an instance-id generator cannot be replayed — the second generation would be handed the
/// object the first is using — and <c>Restart</c> refuses it by name. That refusal is not what this
/// gate is here to exercise, so <see cref="ConfigureTenant" /> stays on the replayable side of it.
/// </para>
/// </remarks>
public abstract partial class ClusteredSoakTestBase
{
    /// <summary>
    /// The tenant's name, which is also its instance name and therefore its <c>SCHED_NAME</c>.
    /// </summary>
    private const string TenantName = "soak-tenant";

    private const string TenantGroup = "clusterSoakTenant";

    /// <summary>
    /// The scheduler-context key each generation is stamped with as the recipe runs.
    /// </summary>
    /// <remarks>
    /// A non-clustered scheduler's instance id is <c>NON_CLUSTERED</c> in every generation —
    /// <c>DefaultSchedulerFactory</c> does not call the id generator for a store that shares its
    /// database with nobody, whatever <c>GenerateInstanceId</c> says — so the id alone cannot say which
    /// generation answered. The ordinal can, and it says something the id could not anyway: the number
    /// only advances when the recipe is <em>run again</em>, which is what a restart has to have done.
    /// </remarks>
    private const string TenantGenerationKey = "soak.generation";

    /// <summary>
    /// The tenant's pool. Bigger than the workload needs on purpose: what the drain waits for should be
    /// the jobs themselves rather than a queue behind a pool of one.
    /// </summary>
    private const int TenantMaxConcurrency = 4;

    private static readonly TimeSpan TenantSteadyInterval = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan TenantSerialInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long a restart may wait for the outgoing generation's jobs. Thirty seconds against a longest
    /// job of 300 ms, so a drain that gives up has found something rather than merely been unlucky.
    /// </summary>
    private static readonly TimeSpan TenantDrainTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How soon after a rebuild the tenant has to be firing again for the rebuild to count as clean.
    /// </summary>
    private static readonly TimeSpan TenantFireAgainBudget = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long the run waits for that firing before giving up on it. Longer than the budget
    /// deliberately: a rebuild that took twelve seconds should be <em>measured</em> at twelve and fail
    /// the assertion saying so, not recorded as "never fired" because the wait stopped at ten.
    /// </summary>
    private static readonly TimeSpan TenantFireAgainPatience = TimeSpan.FromSeconds(30);

    private ISchedulerRuntime runtime;

    private IScheduler tenant;

    /// <summary>How many times the tenant's recipe has been run, which is how many generations there are.</summary>
    private int tenantGenerations;

    /// <summary>
    /// The instant the steady trigger starts from, which is the origin of the grid its scheduled fire
    /// times sit on — and so the thing the end assertions do their arithmetic against.
    /// </summary>
    private DateTimeOffset tenantWorkloadStart;

    private readonly List<TenantRebuild> tenantRebuilds = [];

    /// <summary>
    /// The tenant's rows are under its own <c>SCHED_NAME</c>, which the base class's teardown does not
    /// name, and <see cref="ISchedulerRuntime" /> deliberately deletes nothing when a tenant goes.
    /// </summary>
    [TearDown]
    public Task CleanUpTenantState()
    {
        return ExecuteStatements(
            [
                "DELETE FROM QRTZ_FIRED_TRIGGERS WHERE SCHED_NAME = @schedulerName",
                "DELETE FROM QRTZ_SIMPLE_TRIGGERS WHERE SCHED_NAME = @schedulerName",
                "DELETE FROM QRTZ_SIMPROP_TRIGGERS WHERE SCHED_NAME = @schedulerName",
                "DELETE FROM QRTZ_BLOB_TRIGGERS WHERE SCHED_NAME = @schedulerName",
                "DELETE FROM QRTZ_TRIGGERS WHERE SCHED_NAME = @schedulerName",
                "DELETE FROM QRTZ_JOB_DETAILS WHERE SCHED_NAME = @schedulerName",
                "DELETE FROM QRTZ_PAUSED_TRIGGER_GRPS WHERE SCHED_NAME = @schedulerName",
                "DELETE FROM QRTZ_SCHEDULER_STATE WHERE SCHED_NAME = @schedulerName",
            ],
            ("schedulerName", TenantName));
    }

    /// <summary>
    /// Adds the tenant to node A's container and gives it a schedule of its own.
    /// </summary>
    private async Task StartTenant(SchedulerHost host, List<string> timeline, DateTimeOffset started)
    {
        runtime = host.Services.GetRequiredService<ISchedulerRuntime>();

        // Far enough out that the whole workload is stored before any of it is due, so the first
        // scheduled fire time is one the tenant was running for.
        tenantWorkloadStart = started.AddSeconds(5);

        tenant = await runtime.Add(TenantName, ConfigureTenant);
        AttachRecorder(tenant);

        await ScheduleTenantWorkload(tenant);

        Note(timeline, DateTimeOffset.UtcNow,
            $"tenant '{TenantName}' added at run time on '{NodeA}'s container as {TenantIdentity(tenant)}");
    }

    /// <summary>
    /// The recipe, run again in full for every generation.
    /// </summary>
    /// <remarks>
    /// The store is chosen through <c>UsePersistentStore</c> with a data source described in options and
    /// a driver delegate built by a factory, so replaying this produces a second set of instances rather
    /// than the first set handed over — which is what <c>Restart</c> requires and what it refuses a
    /// recipe for otherwise. The misfire threshold is the cluster's, so that the tenant and the nodes
    /// agree about how late is late.
    /// </remarks>
    private void ConfigureTenant(IQuartzBuilder quartz)
    {
        // Here rather than inside the callback: this counts the times the recipe has been replayed,
        // which is once per generation, and an options callback runs when its container reads the
        // options rather than when the recipe is written down.
        string generation = Interlocked.Increment(ref tenantGenerations).ToString(CultureInfo.InvariantCulture);

        quartz.ConfigureScheduler(options =>
        {
            options.Context[TenantGenerationKey] = generation;
            options.IdleWaitTime = TimeSpan.FromSeconds(2);
            options.MaxBatchSize = TenantMaxConcurrency;
        });

        quartz.UseDefaultThreadPool(TenantMaxConcurrency);

        quartz.UsePersistentStore(store =>
        {
            store.UseDataSource(source =>
            {
                source.Provider = Database.Provider;
                source.ConnectionString = Database.ConnectionString;
            });

            // A factory rather than an instance: it has to answer with a new delegate every time the
            // recipe runs, which is the whole difference between a restartable recipe and a refused one.
            store.UseDriverDelegate(_ => Database.CreateDriverDelegate());

            store.ConfigureStore(options => options.MisfireThreshold = MisfireThreshold);
        });
    }

    /// <summary>
    /// The tenant's own two jobs: one that must never overlap itself, and one that fires often enough
    /// that a gap in it is visible to the second.
    /// </summary>
    /// <remarks>
    /// Scheduled once, at the start, and never again. Every later generation inherits these rows, which
    /// is what makes the schedule a single grid running the length of the soak rather than one that
    /// resets each time the tenant is rebuilt — and a grid is the only thing "fired exactly once or was
    /// skipped" can be said about.
    /// </remarks>
    private async Task ScheduleTenantWorkload(IScheduler scheduler)
    {
        // A run that fell over leaves its rows under this SCHED_NAME, and nothing removes a tenant's
        // data when the tenant goes. Scheduling onto them would inherit the earlier run's grid.
        await scheduler.DeleteJob(new JobKey(TenantJobs.Steady, TenantGroup));
        await scheduler.DeleteJob(new JobKey(TenantJobs.Serial, TenantGroup));

        IJobDetail steady = JobBuilder.Create<SoakTenantSteadyJob>()
            .WithIdentity(TenantJobs.Steady, TenantGroup)
            .StoreDurably()
            .Build();
        await scheduler.AddJob(steady, new AddJobOptions { Replace = true });

        await scheduler.ScheduleJob(TriggerBuilder.Create()
            .WithIdentity(TenantTriggers.Steady, TenantGroup)
            .ForJob(steady)
            .StartAt(tenantWorkloadStart)
            .WithSimpleSchedule(x => x.RepeatForever().WithInterval(TenantSteadyInterval))
            .Build());

        IJobDetail serial = JobBuilder.Create<SoakTenantSerialJob>()
            .WithIdentity(TenantJobs.Serial, TenantGroup)
            .StoreDurably()
            .Build();
        await scheduler.AddJob(serial, new AddJobOptions { Replace = true });

        await scheduler.ScheduleJob(TriggerBuilder.Create()
            .WithIdentity(TenantTriggers.Serial, TenantGroup)
            .ForJob(serial)
            .StartAt(tenantWorkloadStart)
            .WithSimpleSchedule(x => x.RepeatForever().WithInterval(TenantSerialInterval))
            .Build());
    }

    /// <summary>
    /// Rebuilds the tenant, either by restarting it or by removing it and adding it again from the same
    /// recipe, and records what that cost.
    /// </summary>
    /// <remarks>
    /// The two are different questions. <c>Restart</c> is the runtime doing the whole thing — build the
    /// next generation, drain the outgoing one, create — with the ordering it promises; remove and add
    /// is an application doing it by hand, with the tenant genuinely absent in between. A deployment
    /// does both, so the soak does both.
    /// </remarks>
    private async Task RebuildTenant(List<string> timeline, TenantRebuildKind kind)
    {
        DateTimeOffset openedAt = DateTimeOffset.UtcNow;
        string previousIdentity = TenantIdentity(tenant);
        int executing = TenantLedger.InFlight;

        IScheduler next = null;
        string outcome;

        try
        {
            if (kind == TenantRebuildKind.Restart)
            {
                next = await runtime.Restart(
                    TenantName,
                    new SchedulerRestartOptions { DrainTimeout = TenantDrainTimeout });
                outcome = "drained";
            }
            else
            {
                bool removed = await runtime.Remove(TenantName, waitForJobsToComplete: true);
                outcome = removed ? "removed and added" : "nothing to remove, added";
                next = await runtime.Add(TenantName, ConfigureTenant);
            }
        }
        catch (SchedulerRestartException e)
        {
            // Reported rather than swallowed. In a workload whose longest job is 300 ms a drain that
            // gave up has found something, and AssertTenantRebuilds fails on it — but the run goes on,
            // because the next rebuild starts the tenant again and the rest of the half hour is still
            // worth watching. A soak that stops at the first surprise throws away the other twenty-five
            // minutes of evidence.
            outcome = $"ABANDONED after {e.DrainTimeout} with {e.JobsStillExecuting} job(s) still executing";
            TenantLedger.RecordAbandonedDrain(e);
        }

        tenant = next;
        DateTimeOffset returnedAt = DateTimeOffset.UtcNow;

        TenantFiring firedAgain = null;
        if (next is not null)
        {
            AttachRecorder(next);
            firedAgain = await WaitForTenantFiringAfter(returnedAt, TenantFireAgainPatience);
        }

        tenantRebuilds.Add(new TenantRebuild
        {
            Number = tenantRebuilds.Count + 1,
            Kind = kind,
            OpenedAt = openedAt,
            ReturnedAt = returnedAt,
            PreviousIdentity = previousIdentity,
            NextIdentity = TenantIdentity(next),
            JobsExecuting = executing,
            Outcome = outcome,
            FiredAgainAt = firedAgain?.Start,
        });

        Note(timeline, openedAt,
            $"tenant {kind.ToString().ToLowerInvariant()}: {previousIdentity} -> {TenantIdentity(next)} "
            + $"({outcome}, {executing} executing)");
    }

    /// <summary>
    /// How a generation of the tenant is named in the report: its instance id and which run of the
    /// recipe built it.
    /// </summary>
    private static string TenantIdentity(IScheduler scheduler)
    {
        if (scheduler is null)
        {
            return "none";
        }

        string generation = scheduler.Context.TryGetValue(TenantGenerationKey, out object value)
            ? Convert.ToString(value, CultureInfo.InvariantCulture)
            : "?";

        return $"{scheduler.SchedulerInstanceId}#{generation}";
    }

    /// <summary>
    /// Takes the tenant away at the end of the run, waiting for its jobs so that the "nothing left
    /// behind" assertion is about a tenant that finished rather than one that was cut off.
    /// </summary>
    private async Task StopTenant(List<string> timeline)
    {
        if (runtime is null)
        {
            return;
        }

        try
        {
            bool removed = await runtime.Remove(TenantName, waitForJobsToComplete: true);
            Note(timeline, DateTimeOffset.UtcNow,
                removed ? $"tenant '{TenantName}' removed" : $"tenant '{TenantName}' was already gone");
        }
        catch (Exception e)
        {
            // This runs inside a finally that still has a cluster to shut down and a report to print, so
            // a failure here is recorded and asserted on afterwards rather than thrown from here.
            TenantLedger.RecordTeardownFailure(e);
            Note(timeline, DateTimeOffset.UtcNow, $"tenant '{TenantName}' could not be removed: {e.Message}");
        }
        finally
        {
            tenant = null;
        }
    }

    /// <summary>
    /// Waits for the tenant's next steady firing, answering with it or with <see langword="null" /> if
    /// the patience runs out. It does not fail the run: how long a rebuild took to fire again is a
    /// measurement the report carries and the end assertions judge, and failing here would end the soak
    /// at the first slow rebuild.
    /// </summary>
    private static async Task<TenantFiring> WaitForTenantFiringAfter(DateTimeOffset after, TimeSpan patience)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + patience;
        while (DateTimeOffset.UtcNow < deadline)
        {
            TenantFiring firing = TenantLedger.FirstSteadyFiringAfter(after);
            if (firing is not null)
            {
                return firing;
            }

            await Task.Delay(100);
        }

        return TenantLedger.FirstSteadyFiringAfter(after);
    }

    /// <summary>
    /// What a tenant that has been rebuilt a dozen times and then removed leaves in the tables.
    /// </summary>
    /// <remarks>
    /// Polled for the reason the cluster's own version is: <c>Remove</c> returns once the scheduler has
    /// stopped and its jobs have finished, but the last completions write on their own connections.
    /// </remarks>
    private async Task AssertTenantLeftNothingBehind()
    {
        await WaitForCondition(
            async () => await CountTenantRows("QRTZ_FIRED_TRIGGERS") == 0,
            timeoutMs: 60_000,
            async () =>
                $"QRTZ_FIRED_TRIGGERS to drain for '{TenantName}'. Every generation of this tenant swept the "
                + "whole scheduler name on startup and the last one was removed waiting for its jobs, so a row "
                + $"here is a firing nothing ever completed. State:\n{await DumpTenantState()}");

        int reserved = await CountRows(
            "SELECT COUNT(*) FROM QRTZ_TRIGGERS WHERE SCHED_NAME = @schedulerName AND TRIGGER_STATE IN ('ACQUIRED', 'BLOCKED')",
            ("schedulerName", TenantName));

        reserved.Should().Be(0,
            "an ACQUIRED row is one a generation reserved and never fired and a BLOCKED row is the serial job's "
            + "sibling never unblocked — a rebuild that left either would have left the next generation a trigger "
            + "that only a recovery can move");
    }

    /// <summary>
    /// The property the tenant exists to demonstrate: a schedule that survives being rebuilt.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The steady trigger repeats indefinitely under <c>SmartPolicy</c>, which
    /// <c>SimpleTriggerImpl.UpdateAfterMisfire</c> resolves to
    /// <c>RescheduleNextWithRemainingCount</c> for an indefinite repeat: the next fire time becomes the
    /// next occurrence <em>on the original grid</em> after now, so a misfire skips occurrences and never
    /// moves them. Every scheduled fire time this run sees is therefore
    /// <c>start + n × 1 s</c>, and the question is only which of those <c>n</c> fired.
    /// </para>
    /// <para>
    /// That makes the count exact rather than approximate. Between the first and last firing there are
    /// <c>(last − first) / 1 s + 1</c> occurrences; each one fired exactly once or was skipped, and each
    /// skipped one has to fall inside a window when the tenant was being rebuilt. No band, no tolerance:
    /// a second firing of one occurrence and a gap nothing accounts for are both simply wrong.
    /// </para>
    /// </remarks>
    private void AssertTenantKeptItsSchedule()
    {
        SteadySchedule schedule = AnalyseSteadySchedule();

        schedule.Firings.Should().NotBeEmpty(
            "the tenant's ordinary trigger fires every second for the whole run; none at all means the tenant "
            + "never ran, which makes every other assertion here vacuous");

        schedule.OffGrid.Should().BeEmpty(
            "a simple trigger's occurrences are start + n × interval, and misfire handling under SmartPolicy "
            + "moves the next fire time to the next occurrence on that grid rather than off it. A scheduled "
            + "fire time that is not on the grid is a trigger that was rewritten rather than caught up");

        schedule.Repeated.Should().BeEmpty(
            "one occurrence, one firing. A second firing of the same scheduled fire time is a rebuild replaying "
            + "work the previous generation had already completed — which is what the drain before the next "
            + "generation's recovery sweep exists to prevent");

        (schedule.Firings.Count + schedule.Skipped.Count).Should().Be(schedule.Expected,
            "the occurrences between the first firing and the last are exactly {0}, and each of them either "
            + "fired once or was skipped — {1} fired and {2} were skipped, so the sum saying anything else is "
            + "an occurrence that fired twice",
            schedule.Expected, schedule.Firings.Count, schedule.Skipped.Count);

        schedule.Unexplained.Should().BeEmpty(
            "an occurrence is only allowed to be skipped while the tenant is being rebuilt: between the rebuild "
            + "being asked for and the new generation firing again, the scheduler is not there to fire it and "
            + "its misfire instruction lets it go. One skipped outside every such window is a firing lost while "
            + "the tenant was up");

        TenantLedger.SerialPeakConcurrency.Should().Be(1,
            "[DisallowConcurrentExecution] is a claim about the job, not about the generation running it. A "
            + "rebuild hands the same trigger rows to a new set of instances, and two firings at once across "
            + "that boundary would be the store failing to carry the block over it");

        TenantLedger.SerialOverlaps.Should().BeEmpty(
            "the same statement listed rather than counted, so a failure names the firings that overlapped");

        TenantLedger.FiringsOf(TenantJobs.Serial).Should().NotBeEmpty(
            "the serial job is what the overlap check watches; it has to have run for the check to mean anything");
    }

    /// <summary>
    /// That every rebuild finished, and that the tenant was firing again promptly afterwards.
    /// </summary>
    private void AssertTenantRebuilds()
    {
        TenantLedger.TeardownFailures.Should().BeEmpty(
            "removing the tenant at the end of the run is the ordinary path, and a failure there is one an "
            + "application shutting a tenant down would meet too");

        TenantLedger.AbandonedDrains.Should().BeEmpty(
            "a restart's drain waits {0} for jobs whose longest is 300 ms. Giving up on that is either a job "
            + "that never completed or a completion the scheduler never noticed, and both leave the tenant shut "
            + "down with nothing built in its place",
            TenantDrainTimeout);

        tenantRebuilds.Should().NotBeEmpty(
            "the run rebuilds the tenant on a timer; none at all means the phases never fired and the whole "
            + "tenant half of this gate asserted nothing");

        foreach (TenantRebuild rebuild in tenantRebuilds)
        {
            rebuild.NextIdentity.Should().NotBe(rebuild.PreviousIdentity,
                "rebuild #{0} ({1}) has to have produced a second set of instances. The ordinal only advances "
                + "when the recipe is run again, so an unchanged identity is a scheduler that was resumed "
                + "rather than rebuilt — which every part of it refuses to be, having been shut down",
                rebuild.Number, rebuild.Kind);

            rebuild.FiredAgainAt.Should().NotBeNull(
                "rebuild #{0} ({1}, {2}) has to leave a scheduler that fires. Nothing within {3} of it means the "
                + "new generation was built and bound but never acquired anything — a tenant that is listed, "
                + "reports itself running, and does no work",
                rebuild.Number, rebuild.Kind, rebuild.Outcome, TenantFireAgainPatience);

            rebuild.FiredAgainAfter.Value.Should().BeLessThanOrEqualTo(TenantFireAgainBudget,
                "rebuild #{0} ({1}) left the tenant idle for {2:F1} s. A trigger due every second should be "
                + "picked up by the next generation as soon as its store is initialized; a longer gap is the "
                + "recovery sweep, the schema check or the first acquisition costing more than a restart should",
                rebuild.Number, rebuild.Kind, rebuild.FiredAgainAfter.Value.TotalSeconds);
        }
    }

    /// <summary>
    /// Reads the steady trigger's firings as a grid: which occurrences fired, which were skipped, and
    /// which of the skipped ones no rebuild accounts for.
    /// </summary>
    /// <remarks>
    /// Shared by the report and the assertions, so that what the run prints and what it fails on are the
    /// same numbers rather than two calculations that could disagree.
    /// </remarks>
    private SteadySchedule AnalyseSteadySchedule()
    {
        List<TenantFiring> firings = TenantLedger.FiringsOf(TenantJobs.Steady)
            .Where(x => x.ScheduledFireTimeUtc is not null)
            .OrderBy(x => x.ScheduledFireTimeUtc.Value)
            .ToList();

        if (firings.Count == 0)
        {
            return new SteadySchedule(firings, [], [], [], [], 0);
        }

        List<TenantFiring> repeated = firings
            .GroupBy(x => x.ScheduledFireTimeUtc.Value)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group)
            .ToList();

        HashSet<DateTimeOffset> fired = firings.Select(x => x.ScheduledFireTimeUtc.Value).ToHashSet();

        List<DateTimeOffset> offGrid = fired
            .Where(at => (at - tenantWorkloadStart).Ticks % TenantSteadyInterval.Ticks != 0)
            .OrderBy(at => at)
            .ToList();

        DateTimeOffset first = firings[0].ScheduledFireTimeUtc.Value;
        DateTimeOffset last = firings[^1].ScheduledFireTimeUtc.Value;

        List<DateTimeOffset> skipped = [];
        for (DateTimeOffset at = first; at <= last; at += TenantSteadyInterval)
        {
            if (!fired.Contains(at))
            {
                skipped.Add(at);
            }
        }

        List<DateTimeOffset> unexplained = skipped
            .Where(at => !tenantRebuilds.Any(rebuild => rebuild.Covers(at, TenantSteadyInterval)))
            .ToList();

        int expected = (int) ((last - first).Ticks / TenantSteadyInterval.Ticks) + 1;

        return new SteadySchedule(firings, repeated, skipped, unexplained, offGrid, expected);
    }

    /// <summary>
    /// The tenant's half of the run's report: what it fired, and what each rebuild cost.
    /// </summary>
    private string TenantReport()
    {
        SteadySchedule schedule = AnalyseSteadySchedule();
        List<TenantFiring> serial = TenantLedger.FiringsOf(TenantJobs.Serial);

        StringBuilder report = new();
        report.AppendLine(CultureInfo.InvariantCulture, $"Runtime tenant '{TenantName}':");
        report.AppendLine(CultureInfo.InvariantCulture,
            $"  {"steady (every 1 s)",-28} {schedule.Firings.Count} fired, {schedule.Skipped.Count} skipped, "
            + $"{schedule.Expected} occurrences, {schedule.Unexplained.Count} unexplained");
        report.AppendLine(CultureInfo.InvariantCulture,
            $"  {"serial (max concurrent)",-28} {serial.Count} ({TenantLedger.SerialPeakConcurrency})");
        report.AppendLine(CultureInfo.InvariantCulture,
            $"  {"rebuilds",-28} {tenantRebuilds.Count}");

        foreach (TenantRebuild rebuild in tenantRebuilds)
        {
            report.AppendLine(CultureInfo.InvariantCulture,
                $"  #{rebuild.Number,-2} {rebuild.Kind,-7} {rebuild.OpenedAt:HH:mm:ss}  "
                + $"{rebuild.PreviousIdentity} -> {rebuild.NextIdentity}  {rebuild.Outcome}; "
                + $"{rebuild.JobsExecuting} executing; fired again after {Describe(rebuild.FiredAgainAfter)}");
        }

        foreach (DateTimeOffset at in schedule.Skipped)
        {
            report.AppendLine(CultureInfo.InvariantCulture,
                $"  skipped {at:HH:mm:ss}  {(schedule.Unexplained.Contains(at) ? "OUTSIDE EVERY REBUILD WINDOW" : "in a rebuild window")}");
        }

        foreach (string abandoned in TenantLedger.AbandonedDrains)
        {
            report.AppendLine("  abandoned drain: " + abandoned);
        }

        foreach (string failure in TenantLedger.TeardownFailures)
        {
            report.AppendLine("  teardown failure: " + failure);
        }

        return report.ToString();

        static string Describe(TimeSpan? elapsed)
        {
            return elapsed is null
                ? "never"
                : string.Create(CultureInfo.InvariantCulture, $"{elapsed.Value.TotalSeconds:F1} s");
        }
    }

    private Task<int> CountTenantRows(string table)
    {
        return CountRows(
            $"SELECT COUNT(*) FROM {table} WHERE SCHED_NAME = @schedulerName",
            ("schedulerName", TenantName));
    }

    /// <summary>
    /// The tenant's trigger and fired-trigger rows, for a failed assertion to say what it was looking at.
    /// The base class's dump is scoped to the cluster's scheduler name, which is not this one.
    /// </summary>
    private async Task<string> DumpTenantState()
    {
        int triggers = await CountTenantRows("QRTZ_TRIGGERS");
        int fired = await CountTenantRows("QRTZ_FIRED_TRIGGERS");
        int state = await CountTenantRows("QRTZ_SCHEDULER_STATE");

        return string.Create(CultureInfo.InvariantCulture,
            $"{TenantName}: {triggers} trigger row(s), {fired} fired-trigger row(s), {state} scheduler-state row(s)");
    }

    /// <summary>Which of the two ways the tenant was taken apart and put back together.</summary>
    private enum TenantRebuildKind
    {
        /// <summary><see cref="ISchedulerRuntime.Restart" />: the runtime orders build, drain and create.</summary>
        Restart,

        /// <summary><see cref="ISchedulerRuntime.Remove" /> then <see cref="ISchedulerRuntime.Add" />, by hand.</summary>
        Recycle,
    }

    /// <summary>One rebuild, and everything about it a reader of the report would want.</summary>
    private sealed record TenantRebuild
    {
        public required int Number { get; init; }

        public required TenantRebuildKind Kind { get; init; }

        /// <summary>When the rebuild was asked for, which is when the tenant stopped being able to fire.</summary>
        public required DateTimeOffset OpenedAt { get; init; }

        /// <summary>When the call came back with a scheduler, or came back without one.</summary>
        public required DateTimeOffset ReturnedAt { get; init; }

        /// <summary>
        /// The outgoing generation, as its instance id and the run of the recipe that built it.
        /// </summary>
        public required string PreviousIdentity { get; init; }

        /// <summary>The incoming generation, named the same way, or <c>none</c> if none was built.</summary>
        public required string NextIdentity { get; init; }

        /// <summary>
        /// How many of the tenant's jobs were in flight when the rebuild was asked for — which is what
        /// the drain then had to wait for.
        /// </summary>
        public required int JobsExecuting { get; init; }

        public required string Outcome { get; init; }

        /// <summary>When the tenant next fired, or <see langword="null" /> if it never did.</summary>
        public required DateTimeOffset? FiredAgainAt { get; init; }

        /// <summary>How long the new generation took to fire, measured from the call coming back.</summary>
        public TimeSpan? FiredAgainAfter => FiredAgainAt is null ? null : FiredAgainAt.Value - ReturnedAt;

        /// <summary>
        /// Whether an occurrence that never fired falls inside this rebuild's window.
        /// </summary>
        /// <remarks>
        /// The window opens one interval early on purpose: an occurrence due in the interval before the
        /// rebuild was asked for may have been acquired and not yet fired when the drain began, and the
        /// shutdown releases it back to waiting. It closes at the first firing of the next generation,
        /// which is the moment the tenant demonstrably works again — a fixed slack would be a number
        /// picked to make the run pass.
        /// </remarks>
        public bool Covers(DateTimeOffset at, TimeSpan slack)
        {
            return at >= OpenedAt - slack && at <= (FiredAgainAt ?? DateTimeOffset.MaxValue);
        }
    }

    /// <summary>
    /// The steady trigger's grid, read out of the ledger.
    /// </summary>
    private sealed record SteadySchedule(
        List<TenantFiring> Firings,
        List<TenantFiring> Repeated,
        List<DateTimeOffset> Skipped,
        List<DateTimeOffset> Unexplained,
        List<DateTimeOffset> OffGrid,
        int Expected);

    /// <summary>
    /// One firing of one of the tenant's jobs, as the gate asked for it: which firing, which occurrence
    /// it was, which generation ran it, and when it began and ended.
    /// </summary>
    private sealed record TenantFiring(
        string Job,
        string FireInstanceId,
        DateTimeOffset? ScheduledFireTimeUtc,
        string Node,
        DateTimeOffset Start,
        DateTimeOffset End);

    /// <summary>The tenant's job names, in one place.</summary>
    private static class TenantJobs
    {
        public const string Steady = "tenantSteadyJob";

        public const string Serial = "tenantSerialJob";
    }

    /// <summary>The tenant's trigger names, in one place.</summary>
    private static class TenantTriggers
    {
        public const string Steady = "tenant-steady";

        public const string Serial = "tenant-serial";
    }

    /// <summary>
    /// Everything the tenant did, in statics because its jobs are constructed by whichever generation is
    /// alive and the fixture never sees them.
    /// </summary>
    private static class TenantLedger
    {
        private static ConcurrentQueue<TenantFiring> firings = new();

        private static int inFlight;
        private static int serialInFlight;
        private static int serialPeak;

        public static ConcurrentQueue<string> SerialOverlaps { get; private set; } = new();

        public static ConcurrentQueue<string> AbandonedDrains { get; private set; } = new();

        public static ConcurrentQueue<string> TeardownFailures { get; private set; } = new();

        /// <summary>
        /// How many of the tenant's jobs are running right now, which is what a rebuild's drain is about
        /// to wait for.
        /// </summary>
        public static int InFlight => Volatile.Read(ref inFlight);

        public static int SerialPeakConcurrency => Volatile.Read(ref serialPeak);

        public static void Reset()
        {
            firings = new ConcurrentQueue<TenantFiring>();
            SerialOverlaps = new ConcurrentQueue<string>();
            AbandonedDrains = new ConcurrentQueue<string>();
            TeardownFailures = new ConcurrentQueue<string>();
            Interlocked.Exchange(ref inFlight, 0);
            Interlocked.Exchange(ref serialInFlight, 0);
            Interlocked.Exchange(ref serialPeak, 0);
        }

        public static void Enter(IJobExecutionContext context, bool serial)
        {
            Interlocked.Increment(ref inFlight);

            if (!serial)
            {
                return;
            }

            int inside = Interlocked.Increment(ref serialInFlight);
            RecordSerialPeak(inside);

            if (inside > 1)
            {
                SerialOverlaps.Enqueue(string.Create(CultureInfo.InvariantCulture,
                    $"{inside} concurrent firings of the tenant's serial job; this one on "
                    + $"'{context.Scheduler.SchedulerInstanceId}' as {context.FireInstanceId}"));
            }
        }

        public static void Exit(string job, IJobExecutionContext context, DateTimeOffset start, bool serial)
        {
            if (serial)
            {
                Interlocked.Decrement(ref serialInFlight);
            }

            Interlocked.Decrement(ref inFlight);

            firings.Enqueue(new TenantFiring(
                job,
                context.FireInstanceId,
                context.ScheduledFireTimeUtc,
                context.Scheduler.SchedulerInstanceId,
                start,
                DateTimeOffset.UtcNow));
        }

        public static void RecordAbandonedDrain(SchedulerRestartException exception)
        {
            AbandonedDrains.Enqueue(string.Create(CultureInfo.InvariantCulture,
                $"'{exception.SchedulerName}' had {exception.JobsStillExecuting} job(s) executing when the "
                + $"{exception.DrainTimeout} drain gave up: {exception.Message}"));
        }

        public static void RecordTeardownFailure(Exception exception) => TeardownFailures.Enqueue(exception.ToString());

        public static List<TenantFiring> FiringsOf(string job)
        {
            return firings.Where(x => string.Equals(x.Job, job, StringComparison.Ordinal)).ToList();
        }

        /// <summary>
        /// The earliest steady firing that began after <paramref name="after" />, which is how a rebuild
        /// learns that the generation it built is doing work.
        /// </summary>
        public static TenantFiring FirstSteadyFiringAfter(DateTimeOffset after)
        {
            TenantFiring earliest = null;
            foreach (TenantFiring firing in firings)
            {
                if (!string.Equals(firing.Job, TenantJobs.Steady, StringComparison.Ordinal) || firing.Start <= after)
                {
                    continue;
                }

                if (earliest is null || firing.Start < earliest.Start)
                {
                    earliest = firing;
                }
            }

            return earliest;
        }

        private static void RecordSerialPeak(int inside)
        {
            int observed = Volatile.Read(ref serialPeak);
            while (inside > observed)
            {
                int previous = Interlocked.CompareExchange(ref serialPeak, inside, observed);
                if (previous == observed)
                {
                    return;
                }

                observed = previous;
            }
        }
    }

    /// <summary>
    /// The tenant's ordinary job. A firing a second, doing just enough work that its start and end are
    /// different instants and that a drain has something to wait for.
    /// </summary>
    public sealed class SoakTenantSteadyJob : IJob
    {
        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            DateTimeOffset start = DateTimeOffset.UtcNow;
            TenantLedger.Enter(context, serial: false);
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                TenantLedger.Exit(TenantJobs.Steady, context, start, serial: false);
            }
        }
    }

    /// <summary>
    /// The tenant's serial job. It holds its slot long enough that a second firing would have to overlap
    /// it rather than merely follow it — including a second firing acquired by a later generation.
    /// </summary>
    [DisallowConcurrentExecution]
    public sealed class SoakTenantSerialJob : IJob
    {
        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            DateTimeOffset start = DateTimeOffset.UtcNow;
            TenantLedger.Enter(context, serial: true);
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                TenantLedger.Exit(TenantJobs.Serial, context, start, serial: true);
            }
        }
    }
}
