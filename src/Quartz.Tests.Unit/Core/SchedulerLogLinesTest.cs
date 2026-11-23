#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// The Information lines a scheduler writes about itself, each of which is guarded by
/// <c>ILogger.IsEnabled</c> because its arguments are computed by a method call.
/// </summary>
/// <remarks>
/// A guard is only worth having if the line still appears when the level is on, which is what each test
/// here asserts, by the event id the line is logged under.
/// </remarks>
[NonParallelizable]
public sealed class SchedulerLogLinesTest
{
    private const int SchedulerStarted = 1002;
    private const int SchedulerPaused = 1004;
    private const int SchedulerShuttingDown = 1005;
    private const int SchedulerShutdownComplete = 1007;
    private const int TriggerRetryScheduled = 1056;
    private const int ExecutionGroupRoutingConfigured = 1093;

    private static readonly TimeSpan deadline = TimeSpan.FromSeconds(30);

    private FakeLoggerProvider logs = null!;

    [SetUp]
    public void CreateLogs()
    {
        logs = new FakeLoggerProvider();
    }

    [TearDown]
    public void DisposeLogs()
    {
        logs.Dispose();
    }

    [Test]
    public async Task StartingPausingAndShuttingDownEachSayTheSchedulerIdentifier()
    {
        await using ServiceProvider provider = Build("log-lines-lifecycle");
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        await scheduler.Start();
        await scheduler.Standby();
        await scheduler.Shutdown(waitForJobsToComplete: false);

        foreach (int eventId in (int[]) [SchedulerStarted, SchedulerPaused, SchedulerShuttingDown, SchedulerShutdownComplete])
        {
            // At least one: shutting down pauses firing too, so the paused line is written twice.
            List<FakeLogRecord> records = logs.Collector.GetSnapshot().Where(x => x.Id.Id == eventId).ToList();
            records.Should().NotBeEmpty();
            records.Should().OnlyContain(x => x.Level == LogLevel.Information && x.Message.Contains("log-lines-lifecycle"));
        }
    }

    [Test]
    public async Task ARoutingNodeSaysWhichExecutionGroupsItServes()
    {
        await using ServiceProvider provider = Build("log-lines-routing", q => q.ServeExecutionGroups("reports"));

        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        try
        {
            logs.Collector.GetSnapshot().Should().ContainSingle(x => x.Id.Id == ExecutionGroupRoutingConfigured)
                .Which.Message.Should().Contain("serves execution groups reports");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    [Test]
    public async Task AFailedJobTheTriggerRetriesSaysWhichAttemptIsScheduled()
    {
        await using ServiceProvider provider = Build("log-lines-retry");
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        try
        {
            IJobDetail job = JobBuilder.Create<FailingJob>().WithIdentity("failing", "log-lines").Build();
            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("failing", "log-lines")
                .ForJob(job)
                .StartNow()
                .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
                .WithRetryPolicy(RetryPolicy.Fixed(3, TimeSpan.FromMinutes(1)))
                .Build();

            await scheduler.ScheduleJob(job, trigger);
            await scheduler.Start();

            using CancellationTokenSource timeout = new(deadline);
            while (!logs.Collector.GetSnapshot().Any(x => x.Id.Id == TriggerRetryScheduled))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), timeout.Token);
            }

            FakeLogRecord record = logs.Collector.GetSnapshot().Should().ContainSingle(x => x.Id.Id == TriggerRetryScheduled).Which;
            record.Level.Should().Be(LogLevel.Information);
            record.Message.Should().Contain("log-lines.failing").And.Contain("retry 1 of 3");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    private ServiceProvider Build(string schedulerName, Action<IQuartzBuilder>? configure = null)
    {
        ServiceCollection services = new();
        services.AddLogging(logging => logging.AddProvider(logs));
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = schedulerName);
            configure?.Invoke(q);
        });

        return services.BuildServiceProvider();
    }

    private sealed class FailingJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("the job failed");
        }
    }
}
