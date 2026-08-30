using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using FakeItEasy;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Job;
using Quartz.Listener;
using Quartz.Logging;
using Quartz.Simpl;
using Quartz.Xml;

namespace Quartz.Tests.Unit.Xml;

/// <summary>
/// A trigger that arrives together with its own job is applied to the scheduler exactly once, so a
/// repeating trigger that starts now fires at its start time and then on its interval.
/// </summary>
/// <remarks>
/// <para>
/// Container registrations and a <c>job_scheduling_data</c> file are the same code from
/// <see cref="XMLSchedulingDataProcessor.ScheduleJobs" /> downwards, so both front doors are covered
/// here. The per-job loop used to schedule a trigger and the trailing loop then hand the very same
/// object back as a reschedule; the second pass restored the next fire time the first had computed and
/// left the stored trigger with a next fire time behind its own start time, which fires there and then
/// immediately again at the start — two firings milliseconds apart before the interval took over
/// (#3554).
/// </para>
/// <para>
/// Non-parallelizable because the container test hands the global <see cref="LogProvider" /> a logger
/// factory owned by its own container, and because every test here names a scheduler in the process-wide
/// <see cref="Quartz.Impl.SchedulerRepository" />.
/// </para>
/// </remarks>
[NonParallelizable]
public sealed class TriggerScheduledOnceTest
{
    /// <summary>
    /// How long a test waits for firings before deciding the scheduler is stuck. It is never a
    /// measurement: every assertion here is about the times a trigger computed for itself, not about
    /// when the notification arrived.
    /// </summary>
    private static readonly TimeSpan observationDeadline = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The repeat interval used throughout, short enough to observe three firings quickly and long
    /// enough that the milliseconds a double scheduling puts between two of them cannot be mistaken
    /// for it.
    /// </summary>
    private static readonly TimeSpan interval = TimeSpan.FromSeconds(1);

    private const string JobType = "Quartz.Job.NoOpJob, Quartz.Jobs";

    [Test]
    public async Task ATriggerRegisteredWithItsJobFiresAtItsStartAndThenOnItsInterval()
    {
        try
        {
            ServiceCollection services = new ServiceCollection();
            services.AddLogging();
            services.AddQuartz(q =>
            {
                q.SchedulerName = "registered-with-its-job";
                q.UseInMemoryStore();

                // Deliberately not durable: a durable job is added by itself and its triggers are left to
                // the trailing loop, which is the one path that never scheduled anything twice.
                q.AddJob<NoOpJob>(j => j.WithIdentity("job1"));
                q.AddTrigger(t => t
                    .WithIdentity("trigger1")
                    .ForJob("job1")
                    .StartNow()
                    .WithSimpleSchedule(x => x.WithInterval(interval).RepeatForever()));
            });

            await using ServiceProvider provider = services.BuildServiceProvider();

            IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
            FiringRecorder recorder = new FiringRecorder(firings: 3);
            scheduler.ListenerManager.AddJobListener(recorder);

            try
            {
                await scheduler.Start();
                ShouldBeOnTheInterval(await WithinDeadline(recorder.ScheduledFireTimes));
            }
            finally
            {
                await scheduler.Shutdown(waitForJobsToComplete: true);
            }
        }
        finally
        {
            // AddQuartz points the process-wide log provider at this container's logger factory, which
            // goes away with the container; anything asking for a logger afterwards would get a disposed
            // one.
            LogProvider.SetCurrentLogProvider(null);
        }
    }

    [Test]
    public async Task ATriggerLoadedFromAFileWithItsJobFiresAtItsStartAndThenOnItsInterval()
    {
        string directory = Path.Combine(Path.GetTempPath(), "quartz-scheduled-once-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string file = Path.Combine(directory, "job_scheduling_data.xml");
            File.WriteAllText(file, JobWithRepeatingTrigger);

            IScheduler scheduler = await CreateScheduler("loaded-from-a-file");
            FiringRecorder recorder = new FiringRecorder(firings: 3);
            scheduler.ListenerManager.AddJobListener(recorder);

            try
            {
                await CreateProcessor().ProcessFileAndScheduleJobs(file, scheduler);
                await scheduler.Start();

                ShouldBeOnTheInterval(await WithinDeadline(recorder.ScheduledFireTimes));
            }
            finally
            {
                await scheduler.Shutdown(waitForJobsToComplete: true);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task EachTriggerLoadedWithItsJobIsHandedToTheSchedulerExactlyOnce()
    {
        // The counting scheduler forwards every call to a real one - the store has to end up holding the
        // trigger, because a stored trigger is what the second pass used to find and reschedule.
        IScheduler real = await CreateScheduler("handed-over-once");
        IScheduler scheduler = A.Fake<IScheduler>(options => options.Wrapping(real));
        try
        {
            await CreateProcessor().ProcessStreamAndScheduleJobs(ToStream(JobWithTwoTriggers), scheduler);

            A.CallTo(() => scheduler.ScheduleJob(
                A<IJobDetail>.That.Matches(j => j.Key.Name == "job1"),
                A<ITrigger>.That.Matches(t => t.Key.Name == "trigger1"),
                A<CancellationToken>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => scheduler.ScheduleJob(
                A<ITrigger>.That.Matches(t => t.Key.Name == "trigger2"),
                A<CancellationToken>._)).MustHaveHappenedOnceExactly();

            // A trigger this call has just scheduled is not something to reschedule; doing so restores
            // the fire times the scheduling computed and undoes the start time it settled on.
            A.CallTo(() => scheduler.RescheduleJob(
                A<TriggerKey>._,
                A<ITrigger>._,
                A<CancellationToken>._)).MustNotHaveHappened();

            ITrigger stored = await scheduler.GetTrigger(new TriggerKey("trigger1"));
            stored.GetNextFireTimeUtc().Should().Be(stored.StartTimeUtc,
                "a trigger that starts now fires first at its start time, and a next fire time behind the "
                + "start time is one the scheduler fires twice");
        }
        finally
        {
            await real.Shutdown();
        }
    }

    [Test]
    public async Task ATriggerOfADurableJobInTheSameDataIsStillScheduled()
    {
        // A durable job is added on its own and its triggers are left to the trailing loop, so this is
        // what a fix that skipped that loop for every loaded trigger would break.
        IScheduler real = await CreateScheduler("durable-job-in-the-same-data");
        IScheduler scheduler = A.Fake<IScheduler>(options => options.Wrapping(real));
        try
        {
            await CreateProcessor().ProcessStreamAndScheduleJobs(ToStream(DurableJobWithTrigger), scheduler);

            // The trailing loop is where a durable job's triggers are scheduled.
            A.CallTo(() => scheduler.ScheduleJob(
                A<ITrigger>.That.Matches(t => t.Key.Name == "trigger1"),
                A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        }
        finally
        {
            await real.Shutdown();
        }
    }

    [Test]
    public async Task ATriggerWhoseJobIsNotInTheSameDataIsStillScheduled()
    {
        IScheduler real = await CreateScheduler("job-not-in-the-same-data");
        IScheduler scheduler = A.Fake<IScheduler>(options => options.Wrapping(real));
        try
        {
            await scheduler.AddJob(JobBuilder.Create<NoOpJob>().WithIdentity("job1").StoreDurably().Build(), true);

            await CreateProcessor().ProcessStreamAndScheduleJobs(ToStream(TriggerOnly), scheduler);

            // A trigger loaded without its job is exactly what the trailing loop is for.
            A.CallTo(() => scheduler.ScheduleJob(
                A<ITrigger>.That.Matches(t => t.Key.Name == "trigger1"),
                A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        }
        finally
        {
            await real.Shutdown();
        }
    }

    /// <summary>
    /// The firings a repeating trigger that starts now is meant to have: one at the start time, and
    /// one an interval on from each of those.
    /// </summary>
    /// <remarks>
    /// Asserted on the times the firings were <em>scheduled</em> for rather than on when they arrived,
    /// so a loaded machine changes nothing: those come from the trigger's own arithmetic.
    /// </remarks>
    private static void ShouldBeOnTheInterval(IReadOnlyList<DateTimeOffset> scheduledFireTimes)
    {
        scheduledFireTimes[1].Should().Be(scheduledFireTimes[0] + interval,
            "the firing after the first belongs one interval later; a trigger scheduled twice keeps a "
            + "next fire time behind the start time the second pass gave it, and fires at both");
        scheduledFireTimes[2].Should().Be(scheduledFireTimes[1] + interval,
            "and every firing after that is one interval on from the one before it");
    }

    /// <summary>
    /// Gives up on a scheduler that never fires, rather than hanging the run. The deadline is generous
    /// on purpose — nothing here is timed against it.
    /// </summary>
    private static async Task<IReadOnlyList<DateTimeOffset>> WithinDeadline(Task<IReadOnlyList<DateTimeOffset>> firings)
    {
        Task finished = await Task.WhenAny(firings, Task.Delay(observationDeadline)).ConfigureAwait(false);
        if (!ReferenceEquals(finished, firings))
        {
            throw new TimeoutException($"The scheduler did not fire often enough within {observationDeadline}.");
        }

        return await firings.ConfigureAwait(false);
    }

    private static Task<IScheduler> CreateScheduler(string name)
    {
        return SchedulerBuilder.Create()
            .WithName(name)
            .UseInMemoryStore()
            .BuildScheduler();
    }

    private static XMLSchedulingDataProcessor CreateProcessor()
    {
        return new XMLSchedulingDataProcessor(new SimpleTypeLoadHelper());
    }

    private static Stream ToStream(string xml) => new MemoryStream(Encoding.UTF8.GetBytes(xml));

    private static string Document(string body)
    {
        return $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <job-scheduling-data xmlns="http://quartznet.sourceforge.net/JobSchedulingData" version="2.0">
                {body}
                </job-scheduling-data>
                """;
    }

    /// <summary>
    /// A trigger with no start time starts now, which is the case the double scheduling was visible in.
    /// </summary>
    private static string Trigger(string name) => $"""
                                                   <trigger>
                                                     <simple>
                                                       <name>{name}</name>
                                                       <job-name>job1</job-name>
                                                       <repeat-count>-1</repeat-count>
                                                       <repeat-interval>{(long) interval.TotalMilliseconds}</repeat-interval>
                                                     </simple>
                                                   </trigger>
                                                   """;

    private static string Job(bool durable) => $"""
                                                <job>
                                                  <name>job1</name>
                                                  <job-type>{JobType}</job-type>
                                                  <durable>{(durable ? "true" : "false")}</durable>
                                                </job>
                                                """;

    private static readonly string JobWithRepeatingTrigger = Document($"""
        <schedule>
          {Job(durable: false)}
          {Trigger("trigger1")}
        </schedule>
        """);

    private static readonly string JobWithTwoTriggers = Document($"""
        <schedule>
          {Job(durable: false)}
          {Trigger("trigger1")}
          {Trigger("trigger2")}
        </schedule>
        """);

    private static readonly string DurableJobWithTrigger = Document($"""
        <schedule>
          {Job(durable: true)}
          {Trigger("trigger1")}
        </schedule>
        """);

    private static readonly string TriggerOnly = Document($"""
        <schedule>
          {Trigger("trigger1")}
        </schedule>
        """);

    /// <summary>
    /// Records the time each firing was scheduled for, and completes once enough of them have arrived.
    /// </summary>
    private sealed class FiringRecorder : JobListenerSupport
    {
        private readonly TaskCompletionSource<IReadOnlyList<DateTimeOffset>> enough =
            new TaskCompletionSource<IReadOnlyList<DateTimeOffset>>(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly List<DateTimeOffset> scheduledFireTimes = new List<DateTimeOffset>();
        private readonly int firings;

        public FiringRecorder(int firings)
        {
            this.firings = firings;
        }

        public override string Name => nameof(FiringRecorder);

        public Task<IReadOnlyList<DateTimeOffset>> ScheduledFireTimes => enough.Task;

        public override Task JobWasExecuted(
            IJobExecutionContext context,
            JobExecutionException jobException,
            CancellationToken cancellationToken = default)
        {
            lock (scheduledFireTimes)
            {
                scheduledFireTimes.Add(context.ScheduledFireTimeUtc.Value);
                if (scheduledFireTimes.Count >= firings)
                {
                    enough.TrySetResult(scheduledFireTimes.ToArray());
                }
            }

            return Task.CompletedTask;
        }
    }
}
