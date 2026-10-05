using System.Collections.Concurrent;
using System.Collections.Specialized;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// One trigger's fire failing on the database undoes that fire alone, and the rest of the batch commits
/// as it is reported (#3931).
/// </summary>
/// <remarks>
/// <para>
/// <c>ApplyTriggerFired</c> writes several rows: the fired row, the <c>BLOCKED</c> state of a
/// <see cref="DisallowConcurrentExecutionAttribute" /> job's other triggers, the trigger row. A statement
/// that fails partway used to leave the writes before it in the transaction, and the batch committed
/// them: on SQL Server and MySQL a job's other triggers stayed <c>BLOCKED</c> with nothing executing to
/// let go of them; on PostgreSQL the failure aborted the transaction, every fire after it failed too, and
/// the commit — a rollback in disguise — took the fires reported <em>before</em> it with it, underneath
/// the jobs the scheduler was about to run.
/// </para>
/// <para>
/// The first test drives one store by hand through a batch whose middle trigger fails, on the engine's
/// own delegate with the failure injected after the fire's writes, and reads the rows. The second runs
/// a scheduler beside a trigger whose every fire fails, which is stored <c>ERROR</c> after five failures
/// in a row (#3963), and watches the rest of its batch and its job's other trigger fire.
/// </para>
/// <para>
/// The last two complete a <see cref="DisallowConcurrentExecutionAttribute" /> job beside a trigger it
/// held back whose calendar throws as its misfire is handled (#4006). The completion commits, and the
/// failure counts toward the limit; a database failure in the same step is still rolled back and retried.
/// </para>
/// </remarks>
public abstract class TriggerFireFailureTestBase : ClusteredJobStoreTestBase
{
    private const string Group = "fireFailure";
    private const string Node = "fire-failure-node";
    private static readonly JobKey serialJobKey = new("serial", Group);
    private static readonly JobKey ordinaryJobKey = new("ordinary", Group);

    private readonly string faultingDelegateType;

    protected TriggerFireFailureTestBase(string provider, Type faultingDelegate) : base(provider)
    {
        faultingDelegateType = faultingDelegate.AssemblyQualifiedName;
    }

    protected override string SchedulerName => "TriggerFireFailureTest";

    [SetUp]
    public void ResetFault()
    {
        FireFault.Reset();
        NamingJob.Reset();
    }

    [TearDown]
    public void ClearFault() => FireFault.Reset();

    /// <summary>
    /// A batch of three, the middle one failing: the two beside it commit as fired, the failed one's
    /// writes are gone, and the failed trigger's job-mate is not left <c>BLOCKED</c>.
    /// </summary>
    [Test]
    public async Task AFailedFireIsRolledBackAndTheRestOfTheBatchCommitsAsReported()
    {
        // Built, never started: every step is a store call awaited in sequence.
        SchedulerHost host = await CreateSchedulerHost(Node, configure: UseFaultingDelegate);
        try
        {
            IScheduler scheduler = host.Scheduler;
            IJobStore store = host.Services.GetRequiredService<IJobStore>();

            await scheduler.AddJob(JobBuilder.Create<SerialNamingJob>().WithIdentity(serialJobKey).StoreDurably().Build());
            await scheduler.AddJob(JobBuilder.Create<NamingJob>().WithIdentity(ordinaryJobKey).StoreDurably().Build());

            // Ahead of now by a known margin, so the misfire cutoff stays out of the read and the fire-time
            // order is the one below: ordinary-1, poison, ordinary-2, sibling. The sibling is the poison
            // trigger's job-mate — what the poison's fire moves to BLOCKED and must not leave there.
            DateTimeOffset due = TimeProvider.System.GetUtcNow().AddSeconds(30);
            await Schedule(scheduler, "ordinary-1", ordinaryJobKey, due);
            await Schedule(scheduler, "poison", serialJobKey, due.AddMilliseconds(1));
            await Schedule(scheduler, "ordinary-2", ordinaryJobKey, due.AddMilliseconds(2));
            await Schedule(scheduler, "sibling", serialJobKey, due.AddMilliseconds(3));

            List<IOperableTrigger> acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest
            {
                NoLaterThan = due.AddMinutes(1),
                MaxCount = 4,
                // Wide enough that triggers due milliseconds apart make one batch; a batch ends at the
                // first trigger's fire time plus this.
                TimeWindow = TimeSpan.FromSeconds(5),
            });
            acquired.Select(x => x.Key.Name).Should().Equal(["ordinary-1", "poison", "ordinary-2"],
                "a batch takes one trigger of a serial job, so the sibling stays behind");

            FireFault.FailFireOf = "poison";

            List<TriggerFiredResult> results = await store.TriggersFired(acquired);

            results.Should().HaveCount(3, "one answer per trigger, in the order asked");
            results[0].TriggerFiredBundle.Should().NotBeNull("ordinary-1 fired before the failure");

            // Read before the rest of the results are looked at, because it is the issue's PostgreSQL
            // question: there the failure aborted the transaction and the commit became a rollback, so a
            // fire reported to the scheduler had no row behind it by the time the job ran.
            (await FiredState("ordinary-1")).Should().Be("EXECUTING",
                "a result reported fired is a fire that committed; on PostgreSQL the failure used to abort the transaction, "
                + "and the commit became a rollback underneath this reported fire");

            results[1].TriggerFiredBundle.Should().BeNull("the poison fire failed");
            results[1].Exception.Should().NotBeNull("and says so, which is what the scheduler releases the trigger on");
            results[1].IsDeclined.Should().BeFalse();
            results[2].TriggerFiredBundle.Should().NotBeNull(
                "ordinary-2 fired after the failure; on PostgreSQL every statement after a failed one is refused until the "
                + "transaction ends, so this is the answer that says the failure was undone rather than carried");

            (await TriggerState("sibling")).Should().Be("WAITING",
                "the poison fire's BLOCKED of its job-mates went with the fire; left BLOCKED, nothing executing would ever let go of it");
            (await TriggerState("poison")).Should().Be("ACQUIRED", "the reservation is the scheduler's to release, not the store's");
            (await FiredState("poison")).Should().Be("ACQUIRED", "its fired row is the reservation as acquisition wrote it, the fire's update undone");
            (await FiredState("ordinary-2")).Should().Be("EXECUTING");
            FireFault.FireAttempts.Should().Equal(["ordinary-1", "poison", "ordinary-1", "ordinary-2"],
                "the attempt that met the failure is rolled back whole, and the batch is fired again without the failed trigger");

            // What the scheduler thread does with a failed result.
            await store.ReleaseAcquiredTrigger(acquired[1]);

            (await TriggerState("poison")).Should().Be("WAITING", "released, for the next acquisition to pick up");
            (await ExecuteScalar("SELECT COUNT(*) FROM QRTZ_FIRED_TRIGGERS WHERE SCHED_NAME = @schedulerName AND TRIGGER_NAME = @name",
                ("schedulerName", SchedulerName), ("name", "poison"))).Should().Be(0);
            (await FiredState("ordinary-1")).Should().Be("EXECUTING", "the release lets go of the reservation it was asked to, and nothing else");
        }
        finally
        {
            await host.Scheduler.Shutdown();
            await host.Services.DisposeAsync();
        }
    }

    /// <summary>
    /// A scheduler with a trigger whose every fire fails: the ordinary trigger in the same batch keeps
    /// firing on schedule, the poison trigger never runs and is stored <c>ERROR</c> after the failures in
    /// a row the store allows, its job-mate fires after that, and nothing is left <c>BLOCKED</c> once the
    /// scheduler is down.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The poison trigger's fire time never advances, so it is first in the order every round and is in
    /// every batch, and its job-mate — one trigger of a serial job per batch — is skipped behind it for
    /// as long as it is acquired. Before #3963 that was for good: the scheduler released the failed
    /// trigger to be acquired again, however often it failed. Now it is stored <c>ERROR</c> on its fifth
    /// failure in a row, and the job-mate is first in the order.
    /// </para>
    /// <para>
    /// Before #3931 the job-mate was also moved to <c>BLOCKED</c> by each failed fire and left there, on
    /// SQL Server and MySQL; on PostgreSQL the ordinary trigger, refused by the aborted transaction after
    /// the poison in every batch, never fired at all.
    /// </para>
    /// </remarks>
    [Test]
    public async Task ARunningSchedulerKeepsFiringTheRestOfTheBatchBesideATriggerWhoseFireKeepsFailing()
    {
        void ConfigureNode(NameValueCollection properties)
        {
            UseFaultingDelegate(properties);

            // Batched, so the poison trigger shares its batch with the ordinary one.
            properties["quartz.scheduler.batchTriggerAcquisitionMaxCount"] = "4";
            properties["quartz.threadPool.maxConcurrency"] = "4";
        }

        FireFault.FailFireOf = "poison";

        IScheduler node = await CreateScheduler(Node, configure: ConfigureNode);
        try
        {
            IJobDetail serialJob = JobBuilder.Create<SerialNamingJob>().WithIdentity(serialJobKey).StoreDurably().Build();
            IJobDetail ordinaryJob = JobBuilder.Create<NamingJob>().WithIdentity(ordinaryJobKey).StoreDurably().Build();
            await node.AddJob(serialJob, new AddJobOptions { Replace = true });
            await node.AddJob(ordinaryJob, new AddJobOptions { Replace = true });

            foreach ((string name, JobKey job) in ((string, JobKey)[]) [("poison", serialJobKey), ("sibling", serialJobKey), ("ordinary", ordinaryJobKey)])
            {
                await node.ScheduleJob(TriggerBuilder.Create()
                    .WithIdentity(name, Group)
                    .ForJob(job)
                    .StartNow()
                    .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromSeconds(1)).RepeatForever())
                    .Build());
            }

            await node.Start();
            await Task.Delay(TimeSpan.FromSeconds(8));
        }
        finally
        {
            await node.Shutdown(waitForJobsToComplete: true);
        }

        List<string> firings = [.. NamingJob.Firings];
        TestContext.Out.WriteLine("Firings: " + string.Join(", ", firings.GroupBy(x => x, StringComparer.Ordinal).Select(x => $"{x.Key}={x.Count()}")));
        TestContext.Out.WriteLine("Fires attempted: " + string.Join(", ", FireFault.FireAttempts));

        firings.Count(x => x == "ordinary").Should().BeGreaterThanOrEqualTo(5,
            "the ordinary trigger is due every second and shares every batch with the poison; the rest of a batch fires whatever "
            + "one trigger of it does, and on PostgreSQL it used to be refused by the aborted transaction, round after round");
        firings.Should().NotContain("poison", "a fire that fails is not a fire");
        (await TriggerState("poison")).Should().Be("ERROR",
            "its every fire failed, five times in a row, which is what AdoJobStoreOptions.MaxConsecutiveFireFailures allows by default");
        FireFault.FireAttempts.Count(x => x == "poison").Should().Be(5, "stored ERROR on its fifth failure, it is not acquired again");
        firings.Should().Contain("sibling",
            "the poison's job-mate is behind it in the order and one trigger of a serial job per batch; it fires once the poison is stored ERROR");
        (await ExecuteScalar("SELECT COUNT(*) FROM QRTZ_TRIGGERS WHERE SCHED_NAME = @schedulerName AND TRIGGER_STATE = 'BLOCKED'",
            ("schedulerName", SchedulerName))).Should().Be(0,
            "nothing is executing after the shutdown, so nothing may be blocked; each failed fire used to move the poison's job-mate to "
            + "BLOCKED and commit it, with nothing executing to let go of it");
    }

    /// <summary>
    /// A serial job's completion handles the misfires of the triggers it unblocks, and the calendar of one
    /// of them throws. The completion commits and lets go of the job's other triggers, and the one that
    /// threw is <c>WAITING</c> with its fire time as it was. The failure counts: with a limit of two, the
    /// misfire handler's failure after it stores the trigger <c>ERROR</c> (#4006).
    /// </summary>
    /// <remarks>
    /// Before, the throw rolled the completion back, and the completion is retried until it commits: the
    /// job stayed <c>BLOCKED</c>, its fired row stayed, and the worker that ran it never returned.
    /// </remarks>
    [Test]
    public async Task ACompletionCommitsBesideABlockedTriggerWhoseCalendarThrowsAndTheFailureCounts()
    {
        SchedulerHost host = await CreateSchedulerHost(Node, configure: properties =>
        {
            UseFaultingDelegate(properties);
            properties["quartz.jobStore.misfireThreshold"] = "1000";
            properties["quartz.jobStore.maxConsecutiveFireFailures"] = "2";
        });
        try
        {
            IJobStore store = host.Services.GetRequiredService<IJobStore>();
            (TriggerFiredBundle firing, DateTimeOffset due) = await GivenASerialJobRunningPastItsTriggersFireTime(host.Scheduler, store);
            FireFault.CalendarFault.ThrowAlways();

            await CompleteWithin(store, firing);

            FireFault.CalendarFault.Thrown.Should().Be(1);
            (await ExecuteScalar("SELECT COUNT(*) FROM QRTZ_FIRED_TRIGGERS WHERE SCHED_NAME = @schedulerName AND TRIGGER_NAME = @name",
                ("schedulerName", SchedulerName), ("name", "first"))).Should().Be(0, "the completion committed");
            (await TriggerState("mate")).Should().Be("WAITING", "the job's other triggers are let go all the same");
            (await TriggerState("calendared")).Should().Be("WAITING", "released, with nothing of its misfire written");
            (await store.GetTrigger(new TriggerKey("calendared", Group)))!.NextFireTimeUtc.Should().Be(due);

            // The misfire handler, which a started scheduler runs, meets the same calendar.
            await host.Scheduler.Start();
            await WaitForCondition(
                async () => await TriggerState("calendared") == "ERROR",
                20_000,
                "the misfire handler's failure, the second in a row, to store the trigger ERROR");

            FireFault.CalendarFault.Thrown.Should().Be(2, "the completion's failure and the misfire handler's are one run of failures");
        }
        finally
        {
            await host.Scheduler.Shutdown();
            await host.Services.DisposeAsync();
        }
    }

    /// <summary>
    /// The database failing to read the calendar in the same step is not the trigger's failure: the
    /// completion is rolled back and retried, and nothing is counted. With a limit of one, the trigger is
    /// not stored <c>ERROR</c>, and the retry handles its misfire.
    /// </summary>
    [Test]
    public async Task ADatabaseFailureAsACompletionLetsGoOfTheJobsTriggersIsRetriedAndNotCounted()
    {
        SchedulerHost host = await CreateSchedulerHost(Node, configure: properties =>
        {
            UseFaultingDelegate(properties);
            properties["quartz.jobStore.misfireThreshold"] = "1000";
            properties["quartz.jobStore.maxConsecutiveFireFailures"] = "1";
            properties["quartz.jobStore.dbRetryInterval"] = "200";
        });
        try
        {
            IJobStore store = host.Services.GetRequiredService<IJobStore>();
            (TriggerFiredBundle firing, DateTimeOffset due) = await GivenASerialJobRunningPastItsTriggersFireTime(host.Scheduler, store);
            int readsBefore = FireFault.CalendarFault.Reads;
            FireFault.CalendarFault.FailNextRead();

            await CompleteWithin(store, firing);

            (FireFault.CalendarFault.Reads - readsBefore).Should().Be(2, "the rolled-back attempt read the calendar, and so did the retry");
            (await TriggerState("calendared")).Should().Be("WAITING", "a database failure is not the trigger's, so nothing was counted");
            (await store.GetTrigger(new TriggerKey("calendared", Group)))!.NextFireTimeUtc.Should().BeAfter(due, "the retry handled its misfire");
            (await TriggerState("mate")).Should().Be("WAITING");
        }
        finally
        {
            await host.Scheduler.Shutdown();
            await host.Services.DisposeAsync();
        }
    }

    /// <summary>
    /// <c>first</c>, <c>calendared</c> and <c>mate</c>, in that order, on the serial job and all due at
    /// once, <c>calendared</c> on the faulty calendar: <c>first</c> fires, which blocks the other two, and
    /// the job runs past their fire time and the one-second misfire threshold. Answers <c>first</c>'s
    /// firing and the fire time the three were due at.
    /// </summary>
    private async Task<(TriggerFiredBundle Firing, DateTimeOffset Due)> GivenASerialJobRunningPastItsTriggersFireTime(IScheduler scheduler, IJobStore store)
    {
        await scheduler.AddJob(JobBuilder.Create<SerialNamingJob>().WithIdentity(serialJobKey).StoreDurably().Build());

        DateTimeOffset due = TimeProvider.System.GetUtcNow().AddMilliseconds(500);
        foreach ((string name, int priority, string calendar) in ((string, int, string)[]) [("first", 10, null), ("calendared", 5, FireFault.FaultyCalendarName), ("mate", 1, null)])
        {
            await scheduler.ScheduleJob(TriggerBuilder.Create()
                .WithIdentity(name, Group)
                .ForJob(serialJobKey)
                .StartAt(due)
                .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
                .WithPriority(priority)
                .WithCalendarName(calendar)
                .Build());
        }

        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = due.AddMinutes(1),
            MaxCount = 4,
            TimeWindow = TimeSpan.FromSeconds(5),
        });
        acquired.Select(x => x.Key.Name).Should().Equal(["first"], "a batch takes one trigger of a serial job");

        TriggerFiredBundle firing = (await store.TriggersFired(acquired)).Should().ContainSingle().Subject.TriggerFiredBundle;
        firing.Should().NotBeNull();
        (await TriggerState("calendared")).Should().Be("BLOCKED");
        (await TriggerState("mate")).Should().Be("BLOCKED");

        // Past the fire time and the misfire threshold, while the job runs.
        TimeSpan pastThreshold = due.AddMilliseconds(1500) - TimeProvider.System.GetUtcNow();
        if (pastThreshold > TimeSpan.Zero)
        {
            await Task.Delay(pastThreshold);
        }

        return (firing, due);
    }

    /// <summary>
    /// Completes the firing, failing the test rather than hanging it if the completion does not return:
    /// one that rolls back is retried until it commits.
    /// </summary>
    private static async Task CompleteWithin(IJobStore store, TriggerFiredBundle firing)
    {
        Task completing = store.TriggeredJobComplete(firing.Trigger, firing.JobDetail, SchedulerInstruction.NoInstruction).AsTask();
        Task finished = await Task.WhenAny(completing, Task.Delay(TimeSpan.FromSeconds(30)));
        finished.Should().BeSameAs(completing, "the completion commits; one that rolled back for the calendar's throw was retried for good");
        await completing;
    }

    private void UseFaultingDelegate(NameValueCollection properties)
    {
        properties["quartz.jobStore.driverDelegateType"] = faultingDelegateType;
    }

    private static Task Schedule(IScheduler scheduler, string name, JobKey job, DateTimeOffset at)
    {
        return scheduler.ScheduleJob(TriggerBuilder.Create()
            .WithIdentity(name, Group)
            .ForJob(job)
            .StartAt(at)
            .Build()).AsTask();
    }

    private async Task<string> TriggerState(string triggerName)
    {
        return (string) await ExecuteScalar(
            "SELECT TRIGGER_STATE FROM QRTZ_TRIGGERS WHERE SCHED_NAME = @schedulerName AND TRIGGER_NAME = @name",
            ("schedulerName", SchedulerName), ("name", triggerName));
    }

    private async Task<string> FiredState(string triggerName)
    {
        return (string) await ExecuteScalar(
            "SELECT STATE FROM QRTZ_FIRED_TRIGGERS WHERE SCHED_NAME = @schedulerName AND TRIGGER_NAME = @name",
            ("schedulerName", SchedulerName), ("name", triggerName));
    }

    /// <summary>
    /// Records the name of the trigger that fired it.
    /// </summary>
    public class NamingJob : IJob
    {
        private static volatile ConcurrentQueue<string> firings = new();

        public static ConcurrentQueue<string> Firings => firings;

        public static void Reset() => Interlocked.Exchange(ref firings, new ConcurrentQueue<string>());

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Firings.Enqueue(context.Trigger.Key.Name);
            return default;
        }
    }

    /// <summary>
    /// The same, for a job whose fire moves its other triggers to <c>BLOCKED</c>.
    /// </summary>
    [DisallowConcurrentExecution]
    public sealed class SerialNamingJob : NamingJob
    {
    }
}
