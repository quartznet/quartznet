using System.Collections.Specialized;

using Quartz.Impl;
using Quartz.Spi;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// Verifies that AsyncLocal values set during IJobFactory.NewJob
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
    }

    [Test]
    public async Task AsyncLocal_SetInJobFactory_IsVisibleInJobExecute()
    {
        const string expectedTenant = "tenant-42";

        NameValueCollection properties = new NameValueCollection
        {
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType,
            ["quartz.scheduler.instanceName"] = "AsyncLocalTest",
        };

        ISchedulerFactory sf = new StdSchedulerFactory(properties);
        IScheduler scheduler = await sf.GetScheduler();
        scheduler.JobFactory = new AsyncLocalSettingJobFactory(expectedTenant);

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
                "AsyncLocal value set in IJobFactory.NewJob must be visible in IJob.Execute");
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

        public IJob NewJob(TriggerFiredBundle bundle, IScheduler scheduler)
        {
            TenantId.Value = tenantId;
            return new AsyncLocalCapturingJob();
        }

        public ValueTask ReturnJob(IJob job)
        {
            return default;
        }
    }

    public sealed class AsyncLocalCapturingJob : IJob
    {
        public static readonly ManualResetEventSlim Executed = new(false);
        public static string CapturedTenantId;

        public ValueTask Execute(IJobExecutionContext context)
        {
            CapturedTenantId = TenantId.Value;
            Executed.Set();
            return default;
        }
    }
}
