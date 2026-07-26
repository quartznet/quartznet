using System.Collections.Specialized;

using Quartz.Impl;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// Verifies that the <see cref="CancellationToken" /> handed to <see cref="IJob.Execute" /> is the
/// interruption token, and that it is the same token as
/// <see cref="IJobExecutionContext.CancellationToken" /> rather than a second, independent one.
/// </summary>
[NonParallelizable]
public class JobCancellationTokenTest
{
    [SetUp]
    public void SetUp()
    {
        InterruptibleJob.Reset();
    }

    [Test]
    public async Task InterruptCancelsTheTokenPassedToExecute()
    {
        NameValueCollection properties = new NameValueCollection
        {
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType,
            ["quartz.scheduler.instanceName"] = "JobCancellationTokenTest",
        };

        ISchedulerFactory sf = new StdSchedulerFactory(properties);
        IScheduler scheduler = await sf.GetScheduler();

        try
        {
            IJobDetail job = JobBuilder.Create<InterruptibleJob>()
                .WithIdentity("job1", "cancellation")
                .Build();

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("trigger1", "cancellation")
                .ForJob(job)
                .StartNow()
                .Build();

            await scheduler.ScheduleJob(job, trigger);
            await scheduler.Start();

            InterruptibleJob.Started.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue("job did not start within timeout");

            (await scheduler.Interrupt(job.Key)).Should().BeTrue("the running job should have been found");

            InterruptibleJob.Finished.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue("job did not observe the interruption within timeout");

            InterruptibleJob.TokensAreTheSame.Should().BeTrue(
                "the parameter and IJobExecutionContext.CancellationToken must be one token, not two");
            InterruptibleJob.ParameterTokenCancelled.Should().BeTrue(
                "awaiting on the parameter token must be what unblocked the job");
            InterruptibleJob.ContextTokenCancelled.Should().BeTrue(
                "the context token must report the same interruption");
        }
        finally
        {
            await scheduler.Shutdown(true);
        }
    }

    public sealed class InterruptibleJob : IJob
    {
        public static readonly ManualResetEventSlim Started = new(false);
        public static readonly ManualResetEventSlim Finished = new(false);

        public static bool TokensAreTheSame;
        public static bool ParameterTokenCancelled;
        public static bool ContextTokenCancelled;

        public static void Reset()
        {
            Started.Reset();
            Finished.Reset();
            TokensAreTheSame = false;
            ParameterTokenCancelled = false;
            ContextTokenCancelled = false;
        }

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            TokensAreTheSame = cancellationToken == context.CancellationToken;
            Started.Set();

            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                ParameterTokenCancelled = cancellationToken.IsCancellationRequested;
                ContextTokenCancelled = context.CancellationToken.IsCancellationRequested;
            }
            finally
            {
                Finished.Set();
            }
        }
    }
}
