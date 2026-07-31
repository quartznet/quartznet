using System;
using System.Collections.Specialized;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Core;
using Quartz.Impl;
using Quartz.Listener;
using Quartz.Spi;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// When a job cannot be instantiated there is no <see cref="IJobExecutionContext"/> yet, so
/// <see cref="ISchedulerListener.SchedulerError"/> is the only notification the scheduler makes.
/// These tests pin down that it carries enough to identify what failed (#3213).
/// </summary>
[NonParallelizable]
public class JobInstantiationExceptionTest
{
    [Test]
    public async Task FactoryThrowingPlainException_ReportsTriggerAndJobIdentity()
    {
        // The DI path: MicrosoftDependencyInjectionJobFactory lets ActivatorUtilities' own
        // InvalidOperationException out unwrapped, so it lands in JobRunShell's generic catch.
        InvalidOperationException cause = new InvalidOperationException("Unable to resolve service for type 'ITaskTracker'");

        (SchedulerException reported, IScheduler scheduler) = await RunFailingJob(cause);

        try
        {
            JobInstantiationException failure = reported.Should().BeOfType<JobInstantiationException>().Subject;

            failure.Trigger.Key.Should().Be(new TriggerKey("trigger1", "instantiation"));
            failure.JobDetail.Key.Should().Be(new JobKey("job1", "instantiation"));
            failure.FireInstanceId.Should().NotBeNullOrEmpty();
            failure.InnerException.Should().BeSameAs(cause);
        }
        finally
        {
            await scheduler.Shutdown(true);
        }
    }

    [Test]
    public async Task FactoryThrowingSchedulerException_KeepsOriginalAsInnerException()
    {
        // The non-DI path: SimpleJobFactory wraps whatever went wrong in a SchedulerException,
        // so enriching only the generic catch would leave these users where they were.
        SchedulerException cause = new SchedulerException("Problem instantiating class 'MyJob'", new MissingMethodException());

        (SchedulerException reported, IScheduler scheduler) = await RunFailingJob(cause);

        try
        {
            JobInstantiationException failure = reported.Should().BeOfType<JobInstantiationException>().Subject;

            failure.Trigger.Key.Should().Be(new TriggerKey("trigger1", "instantiation"));
            failure.InnerException.Should().BeSameAs(cause,
                "the factory's own exception has to stay reachable, it says what actually went wrong");
            failure.Message.Should().Be(cause.Message, "the reported message text is unchanged from before");
        }
        finally
        {
            await scheduler.Shutdown(true);
        }
    }

    [Test]
    public async Task FactoryThrowingOperationCanceled_LeavesTriggerOutOfErrorState()
    {
        // Shutdown races must not poison the schedule; only real failures set Error.
        (SchedulerException reported, IScheduler scheduler) = await RunFailingJob(new OperationCanceledException());

        try
        {
            reported.Should().BeOfType<JobInstantiationException>();

            TriggerState state = await scheduler.GetTriggerState(new TriggerKey("trigger1", "instantiation"));
            state.Should().NotBe(TriggerState.Error,
                "a cancelled instantiation is not a configuration problem and must not require manual reset");
        }
        finally
        {
            await scheduler.Shutdown(true);
        }
    }

    /// <summary>
    /// Schedules a single job whose instantiation fails with <paramref name="cause"/> and returns the
    /// exception handed to <see cref="ISchedulerListener.SchedulerError"/>. The scheduler is returned
    /// still running so that trigger state can be inspected before shutdown.
    /// </summary>
    private static async Task<(SchedulerException Reported, IScheduler Scheduler)> RunFailingJob(Exception cause)
    {
        NameValueCollection properties = new NameValueCollection
        {
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType,
            ["quartz.scheduler.instanceName"] = "JobInstantiationExceptionTest",
        };

        IScheduler scheduler = await new StdSchedulerFactory(properties).GetScheduler();
        scheduler.JobFactory = new ThrowingJobFactory(cause);

        ErrorCapturingListener listener = new ErrorCapturingListener();
        scheduler.ListenerManager.AddSchedulerListener(listener);

        IJobDetail job = JobBuilder.Create<NeverRunsJob>()
            .WithIdentity("job1", "instantiation")
            .Build();

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("trigger1", "instantiation")
            .ForJob(job)
            .StartNow()
            .Build();

        await scheduler.ScheduleJob(job, trigger);
        await scheduler.Start();

        SchedulerException reported = await listener.WaitForError();
        return (reported, scheduler);
    }

    private sealed class ThrowingJobFactory : IJobFactory
    {
        private readonly Exception cause;

        public ThrowingJobFactory(Exception cause) => this.cause = cause;

        public IJob NewJob(TriggerFiredBundle bundle, IScheduler scheduler) => throw cause;

        public void ReturnJob(IJob job)
        {
        }
    }

    private sealed class ErrorCapturingListener : SchedulerListenerSupport
    {
        private readonly TaskCompletionSource<SchedulerException> reported =
            new TaskCompletionSource<SchedulerException>(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <remarks>
        /// Task.WaitAsync would say this in one line, but the test project also targets net472.
        /// </remarks>
        public async Task<SchedulerException> WaitForError()
        {
            Task completed = await Task.WhenAny(reported.Task, Task.Delay(TimeSpan.FromSeconds(30)));
            completed.Should().BeSameAs(reported.Task, "the scheduler never reported an error");
            return await reported.Task;
        }

        public override Task SchedulerError(string msg, SchedulerException cause, CancellationToken cancellationToken = default)
        {
            reported.TrySetResult(cause);
            return Task.CompletedTask;
        }
    }

    public sealed class NeverRunsJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => throw new InvalidOperationException("must never be constructed");
    }
}
