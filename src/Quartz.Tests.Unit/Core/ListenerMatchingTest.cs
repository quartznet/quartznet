#nullable enable

using Quartz.Core;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// The matchers a listener is registered with are the whole of what decides which notifications reach
/// it, now that registration is the only moment they can be given.
/// </summary>
/// <remarks>
/// They used to be editable afterwards, one matcher at a time, by listener name. Nothing outside those
/// members' own tests ever did, and the notification path paid for the arrangement on every firing: the
/// matchers were held in a dictionary keyed by listener name, so telling three listeners about one fire
/// cost three lookups to learn something the registration already knew. The matchers now travel with
/// the listener, and the cases below are what that has to keep meaning.
/// </remarks>
[NonParallelizable]
public sealed class ListenerMatchingTest
{
    [Test]
    public void AListenerWithNoMatchersHearsEverything()
    {
        AttachedListener<IJobListener, JobKey> attached = new("audit", new RecordingJobListener("audit"), []);

        attached.Matches(new JobKey("nightly", "reports")).Should().BeTrue();
        attached.Matches(new JobKey("anything", "at-all")).Should().BeTrue(
            "no matchers is the absence of a restriction, not a restriction that matches nothing");
    }

    [Test]
    public void AListenerWithSeveralMatchersHearsWhatAnyOfThemSelects()
    {
        AttachedListener<IJobListener, JobKey> attached = new(
            "audit",
            new RecordingJobListener("audit"),
            [GroupMatcher<JobKey>.GroupEquals("reports"), GroupMatcher<JobKey>.GroupEquals("ingest")]);

        attached.Matches(new JobKey("nightly", "reports")).Should().BeTrue();
        attached.Matches(new JobKey("nightly", "ingest")).Should().BeTrue(
            "several matchers are alternatives, so the last one is consulted as readily as the first");
        attached.Matches(new JobKey("nightly", "archive")).Should().BeFalse(
            "a key none of the matchers selects is a notification the listener did not ask for");
    }

    [Test]
    public async Task AJobListenerIsToldOnlyAboutTheJobsItsMatchersSelect()
    {
        RecordingJobListener watched = new("watched", expected: 1);
        RecordingJobListener everything = new("everything", expected: 2);

        IScheduler scheduler = await QuartzSchedulerBuilder.Create()
            .ConfigureScheduler(options => options.InstanceName = "job-listener-matching")
            .BuildScheduler();

        try
        {
            scheduler.ListenerManager.AddJobListener(watched, GroupMatcher<JobKey>.GroupEquals("watched"));
            scheduler.ListenerManager.AddJobListener(everything);

            await ScheduleNow(scheduler, new JobKey("job", "watched"));
            await ScheduleNow(scheduler, new JobKey("job", "ignored"));

            await scheduler.Start();

            await everything.Complete.WaitAsync(TimeSpan.FromSeconds(30));

            watched.Executed.Should().Equal([new JobKey("job", "watched")],
                "a listener registered with a group matcher hears that group and no other");
        }
        finally
        {
            await scheduler.Shutdown(true);
        }
    }

    [Test]
    public async Task ATriggerListenerIsToldOnlyAboutTheTriggersItsMatchersSelect()
    {
        RecordingTriggerListener watched = new("watched", expected: 1);
        RecordingTriggerListener everything = new("everything", expected: 2);

        IScheduler scheduler = await QuartzSchedulerBuilder.Create()
            .ConfigureScheduler(options => options.InstanceName = "trigger-listener-matching")
            .BuildScheduler();

        try
        {
            scheduler.ListenerManager.AddTriggerListener(watched, GroupMatcher<TriggerKey>.GroupEquals("watched"));
            scheduler.ListenerManager.AddTriggerListener(everything);

            await ScheduleNow(scheduler, new JobKey("job", "watched"));
            await ScheduleNow(scheduler, new JobKey("job", "ignored"));

            await scheduler.Start();

            await everything.Complete.WaitAsync(TimeSpan.FromSeconds(30));

            watched.Fired.Should().Equal([new TriggerKey("job", "watched")],
                "the trigger side matches on the trigger's key, which is the job's group here");
        }
        finally
        {
            await scheduler.Shutdown(true);
        }
    }

    [Test]
    public async Task RegisteringAListenerAgainReplacesTheMatchersItHad()
    {
        RecordingJobListener audit = new("audit", expected: 1);
        RecordingJobListener everything = new("everything", expected: 2);

        IScheduler scheduler = await QuartzSchedulerBuilder.Create()
            .ConfigureScheduler(options => options.InstanceName = "matcher-replacement")
            .BuildScheduler();

        try
        {
            // Registering again under the same name is what replaced SetJobListenerMatchers: the listener
            // and the matchers arrive together, so the two can never be out of step.
            scheduler.ListenerManager.AddJobListener(audit, GroupMatcher<JobKey>.GroupEquals("watched"));
            scheduler.ListenerManager.AddJobListener(audit, GroupMatcher<JobKey>.GroupEquals("ignored"));
            scheduler.ListenerManager.AddJobListener(everything);

            scheduler.ListenerManager.GetJobListeners().Should().HaveCount(2,
                "a second registration under the same name replaces the first rather than adding to it");

            await ScheduleNow(scheduler, new JobKey("job", "watched"));
            await ScheduleNow(scheduler, new JobKey("job", "ignored"));

            await scheduler.Start();

            await everything.Complete.WaitAsync(TimeSpan.FromSeconds(30));

            audit.Executed.Should().Equal([new JobKey("job", "ignored")],
                "the matchers of the second registration are the listener's, and the first registration's are gone");
        }
        finally
        {
            await scheduler.Shutdown(true);
        }
    }

    private static ValueTask<DateTimeOffset> ScheduleNow(IScheduler scheduler, JobKey key)
    {
        IJobDetail job = JobBuilder.Create<HarmlessJob>().WithIdentity(key).Build();

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity(key.Name, key.Group)
            .ForJob(job)
            .StartNow()
            .Build();

        return scheduler.ScheduleJob(job, trigger);
    }

    private sealed class RecordingJobListener : IJobListener
    {
        private readonly TaskCompletionSource complete = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<JobKey> executed = [];
        private readonly int expected;

        public RecordingJobListener(string name, int expected = 0)
        {
            Name = name;
            this.expected = expected;
        }

        public string Name { get; }

        public Task Complete => complete.Task;

        public IReadOnlyList<JobKey> Executed
        {
            get
            {
                lock (executed)
                {
                    return executed.ToArray();
                }
            }
        }

        public ValueTask JobWasExecuted(
            IJobExecutionContext context,
            JobExecutionException? jobException,
            CancellationToken cancellationToken = default)
        {
            lock (executed)
            {
                executed.Add(context.JobDetail.Key);
                if (executed.Count >= expected)
                {
                    complete.TrySetResult();
                }
            }

            return default;
        }
    }

    private sealed class RecordingTriggerListener : ITriggerListener
    {
        private readonly TaskCompletionSource complete = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<TriggerKey> fired = [];
        private readonly int expected;

        public RecordingTriggerListener(string name, int expected = 0)
        {
            Name = name;
            this.expected = expected;
        }

        public string Name { get; }

        public Task Complete => complete.Task;

        public IReadOnlyList<TriggerKey> Fired
        {
            get
            {
                lock (fired)
                {
                    return fired.ToArray();
                }
            }
        }

        public ValueTask TriggerFired(
            ITrigger trigger,
            IJobExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            lock (fired)
            {
                fired.Add(trigger.Key);
                if (fired.Count >= expected)
                {
                    complete.TrySetResult();
                }
            }

            return default;
        }
    }

    public sealed class HarmlessJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
