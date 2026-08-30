#nullable enable

using System.Text;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Quartz.Impl;
using Quartz.Jobs;
using Quartz.Xml;

namespace Quartz.Tests.Unit.Xml;

/// <summary>
/// A trigger that arrives together with its own job is applied to the scheduler exactly once, so a
/// repeating trigger that starts now fires at its start time and then on its interval.
/// </summary>
/// <remarks>
/// Container registrations and a <c>job_scheduling_data</c> file are the same code from
/// <see cref="XmlSchedulingDataProcessor.ScheduleJobs" /> downwards, so both front doors are covered
/// here. The per-job loop used to schedule a trigger and the trailing loop then hand the very same
/// object back as a reschedule; the second pass restored the next fire time the first had computed and
/// left the stored trigger with a next fire time behind its own start time, which fires there and then
/// immediately again at the start — two firings milliseconds apart before the interval took over
/// (#3554).
/// </remarks>
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

    private const string JobType = "Quartz.Jobs.NoOpJob, Quartz.Jobs";

    [Test]
    public async Task ATriggerRegisteredWithItsJobFiresAtItsStartAndThenOnItsInterval()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(o => o.InstanceName = "registered-with-its-job");

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

        IScheduler scheduler = provider.GetRequiredService<IScheduler>();
        FiringRecorder recorder = new FiringRecorder(firings: 3);
        scheduler.ListenerManager.AddJobListener(recorder);

        try
        {
            await scheduler.Start();
            ShouldBeOnTheInterval(await recorder.ScheduledFireTimes.WaitAsync(observationDeadline));
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }
    }

    [Test]
    public async Task ATriggerLoadedFromAFileWithItsJobFiresAtItsStartAndThenOnItsInterval()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("quartz-scheduled-once-");
        try
        {
            string file = Path.Combine(directory.FullName, "job_scheduling_data.xml");
            await File.WriteAllTextAsync(file, JobWithRepeatingTrigger);

            await using StandaloneSchedulerFactory factory = CreateFactory("loaded-from-a-file");
            IScheduler scheduler = await factory.GetScheduler();
            FiringRecorder recorder = new FiringRecorder(firings: 3);
            scheduler.ListenerManager.AddJobListener(recorder);

            await CreateProcessor().ProcessFileAndScheduleJobs(file, scheduler);
            await scheduler.Start();

            ShouldBeOnTheInterval(await recorder.ScheduledFireTimes.WaitAsync(observationDeadline));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    public async Task EachTriggerLoadedWithItsJobIsHandedToTheSchedulerExactlyOnce()
    {
        await using StandaloneSchedulerFactory factory = CreateFactory("handed-over-once");
        CountingScheduler scheduler = new CountingScheduler(await factory.GetScheduler());

        await CreateProcessor().ProcessStreamAndScheduleJobs(ToStream(JobWithTwoTriggers), scheduler);

        scheduler.Scheduled.Should().Equal(new TriggerKey("trigger1"), new TriggerKey("trigger2"));
        scheduler.Rescheduled.Should().BeEmpty(
            "a trigger this call has just scheduled is not something to reschedule; doing so restores "
            + "the fire times the scheduling computed and undoes the start time it settled on");

        ITrigger stored = (await scheduler.GetTrigger(new TriggerKey("trigger1")))!;
        stored.NextFireTimeUtc.Should().Be(stored.StartTimeUtc,
            "a trigger that starts now fires first at its start time, and a next fire time behind the "
            + "start time is one the scheduler fires twice");
    }

    [Test]
    public async Task ATriggerOfADurableJobInTheSameDataIsStillScheduled()
    {
        // A durable job is added on its own and its triggers are left to the trailing loop, so this is
        // what a fix that skipped that loop for every loaded trigger would break.
        await using StandaloneSchedulerFactory factory = CreateFactory("durable-job-in-the-same-data");
        CountingScheduler scheduler = new CountingScheduler(await factory.GetScheduler());

        await CreateProcessor().ProcessStreamAndScheduleJobs(ToStream(DurableJobWithTrigger), scheduler);

        scheduler.Scheduled.Should()
            .ContainSingle("the trailing loop is where a durable job's triggers are scheduled")
            .Which.Should().Be(new TriggerKey("trigger1"));
    }

    [Test]
    public async Task ATriggerWhoseJobIsNotInTheSameDataIsStillScheduled()
    {
        await using StandaloneSchedulerFactory factory = CreateFactory("job-not-in-the-same-data");
        CountingScheduler scheduler = new CountingScheduler(await factory.GetScheduler());
        await scheduler.AddJob(JobBuilder.Create<NoOpJob>().WithIdentity("job1").StoreDurably().Build());

        await CreateProcessor().ProcessStreamAndScheduleJobs(ToStream(TriggerOnly), scheduler);

        scheduler.Scheduled.Should()
            .ContainSingle("a trigger loaded without its job is exactly what the trailing loop is for")
            .Which.Should().Be(new TriggerKey("trigger1"));
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

    private static StandaloneSchedulerFactory CreateFactory(string name)
    {
        return QuartzSchedulerBuilder.Create()
            .UseInMemoryStore()
            .ConfigureScheduler(o => o.InstanceName = name)
            .Build();
    }

    private static XmlSchedulingDataProcessor CreateProcessor()
    {
        return new XmlSchedulingDataProcessor(
            NullLogger<XmlSchedulingDataProcessor>.Instance,
            new SimpleTypeLoader(),
            TimeProvider.System);
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
    private sealed class FiringRecorder : IJobListener
    {
        private readonly TaskCompletionSource<IReadOnlyList<DateTimeOffset>> enough =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly List<DateTimeOffset> scheduledFireTimes = [];
        private readonly int firings;

        public FiringRecorder(int firings)
        {
            this.firings = firings;
        }

        public Task<IReadOnlyList<DateTimeOffset>> ScheduledFireTimes => enough.Task;

        public ValueTask JobWasExecuted(
            IJobExecutionContext context,
            JobExecutionException? jobException,
            CancellationToken cancellationToken = default)
        {
            lock (scheduledFireTimes)
            {
                scheduledFireTimes.Add(context.ScheduledFireTimeUtc!.Value);
                if (scheduledFireTimes.Count >= firings)
                {
                    enough.TrySetResult(scheduledFireTimes.ToArray());
                }
            }

            return default;
        }
    }

    /// <summary>
    /// Counts what <see cref="XmlSchedulingDataProcessor.ScheduleJobs" /> asks of a scheduler, and
    /// forwards every call to a real one — the store has to end up holding the trigger, because a
    /// stored trigger is what the second pass used to find and reschedule.
    /// </summary>
    private sealed class CountingScheduler : DelegatingScheduler
    {
        public CountingScheduler(IScheduler scheduler) : base(scheduler)
        {
        }

        public List<TriggerKey> Scheduled { get; } = [];

        public List<TriggerKey> Rescheduled { get; } = [];

        public override ValueTask<DateTimeOffset> ScheduleJob(
            IJobDetail jobDetail,
            ITrigger trigger,
            CancellationToken cancellationToken = default)
        {
            Scheduled.Add(trigger.Key);
            return base.ScheduleJob(jobDetail, trigger, cancellationToken);
        }

        public override ValueTask<DateTimeOffset> ScheduleJob(
            ITrigger trigger,
            CancellationToken cancellationToken = default)
        {
            Scheduled.Add(trigger.Key);
            return base.ScheduleJob(trigger, cancellationToken);
        }

        public override ValueTask<DateTimeOffset?> RescheduleJob(
            TriggerKey triggerKey,
            ITrigger newTrigger,
            CancellationToken cancellationToken = default)
        {
            Rescheduled.Add(triggerKey);
            return base.RescheduleJob(triggerKey, newTrigger, cancellationToken);
        }
    }
}
