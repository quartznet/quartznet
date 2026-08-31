using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Listeners;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// A trigger can be parked in <see cref="TriggerState.Error"/> without anything observing it, which
/// leaves it silently dead until someone polls its state or reads the log (#3214).
/// </summary>
[NonParallelizable]
public class TriggerInErrorNotificationTest
{
    [Test]
    public async Task JobThatCannotBeInstantiated_NotifiesTriggersInError()
    {
        ErrorStateListener listener = new ErrorStateListener();

        QuartzSchedulerBuilder builder = QuartzSchedulerBuilder.Create();
        builder.UseJobFactory(new ThrowingJobFactory());

        IScheduler scheduler = await builder.BuildScheduler();

        try
        {
            scheduler.ListenerManager.AddSchedulerListener(listener);

            await ScheduleOneShot(scheduler, "job1", "trigger1");
            await scheduler.Start();

            JobKey reported = await listener.JobTriggers.WaitAsync(TimeSpan.FromSeconds(30));

            reported.Should().Be(new JobKey("job1", "errorstate"),
                "instantiation failure moves every trigger of the job to Error, so the notification is keyed by job");

            TriggerState state = await scheduler.GetTriggerState(new TriggerKey("trigger1", "errorstate"));
            state.Should().Be(TriggerState.Error, "the notification has to describe a state the store actually reached");
        }
        finally
        {
            await scheduler.Shutdown(true);
        }
    }

    [Test]
    public async Task NoFailure_RaisesNoErrorNotification()
    {
        ErrorStateListener listener = new ErrorStateListener();

        IScheduler scheduler = await QuartzSchedulerBuilder.Create().BuildScheduler();

        try
        {
            scheduler.ListenerManager.AddSchedulerListener(listener);

            await ScheduleOneShot(scheduler, "job1", "trigger1");
            await scheduler.Start();

            bool executed = HarmlessJob.Executed.Wait(TimeSpan.FromSeconds(30));
            executed.Should().BeTrue("the job has to run before we can say nothing was reported");

            listener.JobTriggers.IsCompleted.Should().BeFalse();
            listener.SingleTrigger.IsCompleted.Should().BeFalse();
        }
        finally
        {
            await scheduler.Shutdown(true);
        }
    }

    [Test]
    public async Task SetTriggerError_NotifiesForThatTriggerAlone()
    {
        // SetTriggerError parks one trigger rather than the job's whole set, and the store is the only
        // thing that knows it happened. Driven against the store directly: no scheduler instruction
        // reaches this branch without a trigger implementation that asks for it.
        CapturingSignaler signaler = new CapturingSignaler();
        RAMJobStore store = TestJobStores.Ram(signaler);
        await store.Initialize(TestJobStores.Identity());
        await store.SchedulerStarted(CancellationToken.None);

        IJobDetail job = JobBuilder.Create<HarmlessJob>()
            .WithIdentity("job1", "errorstate")
            .StoreDurably()
            .Build();

        await store.AddJob(job);

        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity("trigger1", "errorstate")
            .ForJob(job)
            .StartNow()
            .Build();

        trigger.ComputeFirstFireTimeUtc(null);
        await store.AddTrigger(trigger);

        await store.TriggeredJobComplete(trigger, job, SchedulerInstruction.SetTriggerError);

        signaler.Triggers.Should().ContainSingle().Which.Should().Be(new TriggerKey("trigger1", "errorstate"));
        signaler.Jobs.Should().BeEmpty("only the one trigger moved");
    }

    [SetUp]
    public void SetUp() => HarmlessJob.Executed.Reset();

    private sealed class CapturingSignaler : ISchedulerSignaler
    {
        public List<TriggerKey> Triggers { get; } = [];

        public List<JobKey> Jobs { get; } = [];

        public ValueTask NotifyTriggerListenersMisfired(ITrigger trigger, CancellationToken cancellationToken = default) => default;

        public ValueTask NotifySchedulerListenersFinalized(ITrigger trigger, CancellationToken cancellationToken = default) => default;

        public ValueTask NotifySchedulerListenersJobDeleted(JobKey jobKey, CancellationToken cancellationToken = default) => default;

        public ValueTask SignalSchedulingChange(DateTimeOffset? candidateNewNextFireTimeUtc, CancellationToken cancellationToken = default) => default;

        public ValueTask NotifySchedulerListenersError(SchedulerErrorContext errorContext, CancellationToken cancellationToken = default) => default;

        public ValueTask NotifySchedulerListenersTriggerInError(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            Triggers.Add(triggerKey);
            return default;
        }

        public ValueTask NotifySchedulerListenersTriggersInError(JobKey jobKey, CancellationToken cancellationToken = default)
        {
            Jobs.Add(jobKey);
            return default;
        }
    }

    private static async Task ScheduleOneShot(IScheduler scheduler, string jobName, string triggerName)
    {
        IJobDetail job = JobBuilder.Create<HarmlessJob>()
            .WithIdentity(jobName, "errorstate")
            .Build();

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity(triggerName, "errorstate")
            .ForJob(job)
            .StartNow()
            .Build();

        await scheduler.ScheduleJob(job, trigger);
    }

    private sealed class ThrowingJobFactory : IJobFactory
    {
        public ValueTask<JobScope> CreateJob(TriggerFiredBundle bundle, IScheduler scheduler, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Unable to resolve service for type 'ITaskTracker'");
        }

        public ValueTask ReturnJob(JobScope scope, CancellationToken cancellationToken = default) => default;
    }

    private sealed class ErrorStateListener : ISchedulerListener
    {
        private readonly TaskCompletionSource<JobKey> jobTriggers = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<TriggerKey> singleTrigger = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<JobKey> JobTriggers => jobTriggers.Task;

        public Task<TriggerKey> SingleTrigger => singleTrigger.Task;

        public ValueTask TriggersInError(IScheduler scheduler, JobKey jobKey, CancellationToken cancellationToken = default)
        {
            jobTriggers.TrySetResult(jobKey);
            return default;
        }

        public ValueTask TriggerInError(IScheduler scheduler, TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            singleTrigger.TrySetResult(triggerKey);
            return default;
        }
    }

    public sealed class HarmlessJob : IJob
    {
        public static readonly ManualResetEventSlim Executed = new(false);

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Executed.Set();
            return default;
        }
    }
}
