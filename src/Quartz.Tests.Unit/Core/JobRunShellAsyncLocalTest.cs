using Quartz.Impl;
using Quartz.Extensibility;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// Verifies that AsyncLocal values set during IJobFactory.CreateJob
/// flow correctly to IJob.Execute (GitHub issue #1528).
/// </summary>
[NonParallelizable]
public class JobRunShellAsyncLocalTest
{
    private static readonly AsyncLocal<string> TenantId = new();

    [SetUp]
    public void SetUp()
    {
        AsyncLocalCapturingJob.Executed.Reset();
        AsyncLocalCapturingJob.CapturedTenantId = null;
        AwaitingJobFactory.AwaitedWorkCompleted = false;
    }

    [Test]
    public async Task AsyncLocal_SetInJobFactory_IsVisibleInJobExecute()
    {
        const string expectedTenant = "tenant-42";

        IScheduler scheduler = await QuartzSchedulerBuilder
            .Create(q => q.UseJobFactory(new AsyncLocalSettingJobFactory(expectedTenant)))
            .BuildScheduler();

        try
        {
            IJobDetail job = JobBuilder.Create<AsyncLocalCapturingJob>()
                .WithIdentity("job1", "asynclocal")
                .Build();

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("trigger1", "asynclocal")
                .ForJob(job)
                .StartNow()
                .Build();

            await scheduler.ScheduleJob(job, trigger);
            await scheduler.Start();

            bool signaled = AsyncLocalCapturingJob.Executed.Wait(TimeSpan.FromSeconds(10));
            Assert.That(signaled, Is.True, "Job did not execute within timeout");
            Assert.That(AsyncLocalCapturingJob.CapturedTenantId, Is.EqualTo(expectedTenant),
                "AsyncLocal value set in IJobFactory.CreateJob must be visible in IJob.Execute");
        }
        finally
        {
            await scheduler.Shutdown(true);
        }
    }

    [Test]
    public async Task AsyncJobFactory_AwaitedWorkCompletesBeforeJobExecute()
    {
        IScheduler scheduler = await QuartzSchedulerBuilder
            .Create(q => q.UseJobFactory(new AwaitingJobFactory()))
            .BuildScheduler();

        try
        {
            IJobDetail job = JobBuilder.Create<AsyncLocalCapturingJob>()
                .WithIdentity("job1", "asyncfactory")
                .Build();

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("trigger1", "asyncfactory")
                .ForJob(job)
                .StartNow()
                .Build();

            await scheduler.ScheduleJob(job, trigger);
            await scheduler.Start();

            bool signaled = AsyncLocalCapturingJob.Executed.Wait(TimeSpan.FromSeconds(10));
            Assert.That(signaled, Is.True, "Job did not execute within timeout");
            Assert.That(AwaitingJobFactory.AwaitedWorkCompleted, Is.True,
                "Awaited work in IJobFactory.CreateJob must complete before IJob.Execute runs");
        }
        finally
        {
            await scheduler.Shutdown(true);
        }
    }

    private sealed class AsyncLocalSettingJobFactory : IJobFactory
    {
        private readonly string tenantId;

        public AsyncLocalSettingJobFactory(string tenantId)
        {
            this.tenantId = tenantId;
        }

        public ValueTask<JobScope> CreateJob(TriggerFiredBundle bundle, IScheduler scheduler, CancellationToken cancellationToken = default)
        {
            TenantId.Value = tenantId;
            return new ValueTask<JobScope>(new JobScope(new AsyncLocalCapturingJob()));
        }

        public ValueTask ReturnJob(JobScope scope, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }

    public sealed class AsyncLocalCapturingJob : IJob
    {
        public static readonly ManualResetEventSlim Executed = new(false);
        public static string CapturedTenantId;

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            CapturedTenantId = TenantId.Value;
            Executed.Set();
            return default;
        }
    }

    private sealed class AwaitingJobFactory : IJobFactory
    {
        public static volatile bool AwaitedWorkCompleted;

        public async ValueTask<JobScope> CreateJob(TriggerFiredBundle bundle, IScheduler scheduler, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            AwaitedWorkCompleted = true;
            return new JobScope(new AsyncLocalCapturingJob());
        }

        public ValueTask ReturnJob(JobScope scope, CancellationToken cancellationToken = default) => default;
    }
}
