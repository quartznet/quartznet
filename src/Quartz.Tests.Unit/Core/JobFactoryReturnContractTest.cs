using Quartz.Extensibility;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// The job run shell has to hand every job it took from the factory back exactly once, carrying the
/// same <see cref="JobScope" /> the factory produced — whether the job completed or threw. A factory
/// that allocates per-fire resources (a DI scope, a connection, a tenant context) leaks them
/// otherwise.
/// </summary>
[NonParallelizable]
public class JobFactoryReturnContractTest
{
    [Test]
    public async Task ReturnJobIsCalledOnceWithTheCreatedScopeWhenTheJobSucceeds()
    {
        RecordedJob job = new RecordedJob();
        RecordingJobFactory factory = new RecordingJobFactory(job);

        await RunOneShotJob(factory, "returncontractsuccess");

        job.ExecuteCount.Should().Be(1, "the one-shot trigger fires the job exactly once");
        AssertReturnedExactlyOnceWithTheCreatedScope(factory, job);
    }

    [Test]
    public async Task ReturnJobIsCalledOnceWithTheCreatedScopeWhenExecuteThrows()
    {
        RecordedJob job = new RecordedJob { FailWith = new InvalidOperationException("job blew up") };
        RecordingJobFactory factory = new RecordingJobFactory(job);

        await RunOneShotJob(factory, "returncontractfailure");

        job.ExecuteCount.Should().Be(1, "the job ran, it just failed while doing so");
        AssertReturnedExactlyOnceWithTheCreatedScope(factory, job);
    }

    private static void AssertReturnedExactlyOnceWithTheCreatedScope(RecordingJobFactory factory, RecordedJob job)
    {
        factory.CreateCount.Should().Be(1, "a single firing may only ask the factory for one job");
        factory.ReturnedScopes.Should().HaveCount(1,
            "every job the factory hands out has to be returned exactly once, or per-fire resources leak");

        JobScope returned = factory.ReturnedScopes[0];
        returned.Job.Should().BeSameAs(job, "ReturnJob has to receive the very job instance CreateJob handed out");
        returned.State.Should().BeSameAs(factory.State,
            "the per-fire state has to travel back to the factory untouched, since only the factory knows what it is");
    }

    private static async Task RunOneShotJob(RecordingJobFactory factory, string name)
    {
        IScheduler scheduler = await QuartzSchedulerBuilder
            .Create(q => q
                .ConfigureScheduler(options => options.InstanceName = name)
                .UseJobFactory(factory))
            .BuildScheduler();

        try
        {
            IJobDetail jobDetail = JobBuilder.Create<RecordedJob>()
                .WithIdentity("job", name)
                .Build();

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("trigger", name)
                .ForJob(jobDetail)
                .StartNow()
                .Build();

            await scheduler.ScheduleJob(jobDetail, trigger);
            await scheduler.Start();

            await factory.Returned.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }
    }

    /// <summary>
    /// Hands out one known job instance with a sentinel state object, and records what comes back.
    /// </summary>
    private sealed class RecordingJobFactory : IJobFactory
    {
        private readonly IJob job;
        private readonly List<JobScope> returnedScopes = [];
        private int createCount;

        public RecordingJobFactory(IJob job)
        {
            this.job = job;
        }

        /// <summary>
        /// A sentinel that Quartz must not interpret, replace or drop on the way back.
        /// </summary>
        public object State { get; } = new object();

        public TaskCompletionSource Returned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CreateCount => Volatile.Read(ref createCount);

        public IReadOnlyList<JobScope> ReturnedScopes
        {
            get
            {
                lock (returnedScopes)
                {
                    return returnedScopes.ToArray();
                }
            }
        }

        public ValueTask<JobScope> CreateJob(TriggerFiredBundle bundle, IScheduler scheduler, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref createCount);
            return new ValueTask<JobScope>(new JobScope(job, State));
        }

        public ValueTask ReturnJob(JobScope scope, CancellationToken cancellationToken = default)
        {
            lock (returnedScopes)
            {
                returnedScopes.Add(scope);
            }

            Returned.TrySetResult();
            return default;
        }
    }

    private sealed class RecordedJob : IJob
    {
        private int executeCount;

        public Exception FailWith { get; init; }

        public int ExecuteCount => Volatile.Read(ref executeCount);

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref executeCount);

            if (FailWith is not null)
            {
                throw FailWith;
            }

            return default;
        }
    }
}
