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

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Quartz.Extensibility;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// <c>UseExecutionLogCapture()</c> end to end: a job logs through the container's logger, and what it
/// logged is on its history row — its own lines, only its own, and only while it ran.
/// </summary>
public sealed class ExecutionLogCaptureSchedulerTest
{
    private static readonly TimeSpan waitLimit = TimeSpan.FromSeconds(20);

    private SqliteTestDatabase? database;

    [SetUp]
    public void SetUp()
    {
        MeetingJob.Reset(expected: 2);
    }

    [TearDown]
    public void TearDown()
    {
        database?.Dispose();
        database = null;
    }

    [Test]
    public async Task WhatAJobLogsIsKeptWithItsHistoryRow()
    {
        await using ServiceProvider container = BuildContainer(q => q.UseExecutionLogCapture());
        IScheduler scheduler = await Start(container);

        ILogger outside = container.GetRequiredService<ILoggerFactory>().CreateLogger("Outside");
        outside.LogInformation("logged before the firing");

        await RunOnce<LoggingJob>(scheduler, "logging");

        outside.LogInformation("logged after the firing");

        ExecutionHistoryEntry row = await SingleRow(container, scheduler.SchedulerName);

        row.Log.Should().NotBeNull("the scheduler captures, and the job logged");
        // The category a logger for a nested type is created under, which spells the nesting with a dot.
        string category = typeof(LoggingJob).FullName!.Replace('+', '.');

        row.Log.Should().Contain("info " + category + ": exporting page 1 of 2")
            .And.Contain("warn " + category + ": page 2 was slow",
                "every line the job logged on its own flow is kept, in order, with its level and category");
        row.Log.Should().NotContain("logged before the firing").And.NotContain("logged after the firing",
            "a line logged outside the firing belongs to no execution");

        row.EntryId.Should().NotBeNullOrEmpty("the recorder names every row it writes");

        IExecutionHistoryStore store = container.GetRequiredService<IExecutionHistoryStore>();
        (await store.GetExecution(scheduler.SchedulerName, row.EntryId!))!.Log.Should().Be(row.Log,
            "the single read answers the row the listing showed, log and all");

        await scheduler.Shutdown(waitForJobsToComplete: true);
    }

    [Test]
    public async Task WhatAFailingJobThrewIsKeptUnderItsLastLine()
    {
        await using ServiceProvider container = BuildContainer(q => q.UseExecutionLogCapture());
        IScheduler scheduler = await Start(container);

        await RunOnce<FailingJob>(scheduler, "failing");

        ExecutionHistoryEntry row = await SingleRow(container, scheduler.SchedulerName);

        row.Succeeded.Should().BeFalse();
        row.Log.Should().Contain("about to fail").And.Contain("the upstream system is down",
            "the run shell logs what the job threw while the firing is still in progress, so it is kept too");

        await scheduler.Shutdown(waitForJobsToComplete: true);
    }

    [Test]
    public async Task TwoFiringsAtOnceKeepOnlyTheirOwnLines()
    {
        await using ServiceProvider container = BuildContainer(q => q.UseExecutionLogCapture());
        IScheduler scheduler = await Start(container);

        JobKey first = new("meeting-first", "capture");
        JobKey second = new("meeting-second", "capture");
        await scheduler.AddJob(JobBuilder.Create<MeetingJob>().WithIdentity(first).UsingJobData("marker", "FIRST").StoreDurably().Build());
        await scheduler.AddJob(JobBuilder.Create<MeetingJob>().WithIdentity(second).UsingJobData("marker", "SECOND").StoreDurably().Build());

        await scheduler.TriggerJob(first);
        await scheduler.TriggerJob(second);

        List<ExecutionHistoryEntry> rows = await Rows(container, scheduler.SchedulerName, count: 2);

        foreach (ExecutionHistoryEntry row in rows)
        {
            string mine = row.JobName == "meeting-first" ? "FIRST" : "SECOND";
            string theirs = mine == "FIRST" ? "SECOND" : "FIRST";

            row.Log.Should().Contain(mine + " line 1").And.Contain(mine + " line 5");
            row.Log.Should().NotContain(theirs,
                "the two firings ran at the same time and interleaved their lines, and each row keeps only its own");
        }

        await scheduler.Shutdown(waitForJobsToComplete: true);
    }

    [Test]
    public async Task ASchedulerThatDoesNotCaptureKeepsNoLog()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddQuartzExecutionHistory();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = "capturing-" + Guid.NewGuid().ToString("N"));
            q.UseExecutionLogCapture();
        });
        string plainName = "plain-" + Guid.NewGuid().ToString("N");
        services.AddQuartz(plainName, q => q.ConfigureScheduler(options => options.InstanceId = "plain-node"));

        await using ServiceProvider container = services.BuildServiceProvider();
        ISchedulerFactory factory = container.GetRequiredService<ISchedulerFactory>();

        IScheduler capturing = await factory.GetScheduler();
        IScheduler plain = await container.GetRequiredKeyedService<ISchedulerFactory>(plainName).GetScheduler();
        await capturing.Start();
        await plain.Start();

        await RunOnce<LoggingJob>(capturing, "logging");
        await RunOnce<LoggingJob>(plain, "logging");

        (await SingleRow(container, capturing.SchedulerName)).Log.Should().NotBeNull();
        (await SingleRow(container, plain.SchedulerName)).Log.Should().BeNull(
            "capture is one scheduler's choice: the container's logger provider sees every scheduler's lines and "
            + "keeps only those of the firings a capturing scheduler began a buffer for");

        await capturing.Shutdown(waitForJobsToComplete: true);
        await plain.Shutdown(waitForJobsToComplete: true);
    }

    [Test]
    public async Task TheBoundsApplyToWhatIsKept()
    {
        await using ServiceProvider container = BuildContainer(q => q.UseExecutionLogCapture(options => options.MaxLines = 1));
        IScheduler scheduler = await Start(container);

        await RunOnce<LoggingJob>(scheduler, "logging");

        ExecutionHistoryEntry row = await SingleRow(container, scheduler.SchedulerName);
        row.Log.Should().StartWith("[1 earlier log entries dropped")
            .And.Contain("page 2 was slow").And.NotContain("exporting page 1",
                "the newest entry is the one kept when the bound is one");

        await scheduler.Shutdown(waitForJobsToComplete: true);
    }

    [Test]
    public async Task BoundsThatKeepNothingAreRefusedWhenTheSchedulerIsBuilt()
    {
        await using ServiceProvider container = BuildContainer(q => q.UseExecutionLogCapture(options => options.MaxLines = 0));

        Func<Task> build = async () => await container.GetRequiredService<ISchedulerFactory>().GetScheduler();

        (await build.Should().ThrowAsync<SchedulerConfigException>()
                .WithMessage("*MaxLines*"))
            .WithInnerException<OptionsValidationException>();
    }

    [Test]
    public async Task ThePersistentHistoryKeepsTheLogInItsOwnColumn()
    {
        database = new SqliteTestDatabase("execution-log");
        string connectionString = database.ConnectionString;

        await using ServiceProvider container = BuildContainer(q =>
        {
            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, connectionString);
                store.ProvisionSchema();
                store.UseExecutionHistory();
            });
            q.UseExecutionLogCapture();
        });

        IScheduler scheduler = await Start(container);
        await RunOnce<LoggingJob>(scheduler, "logging");

        ExecutionHistoryEntry listed = await SingleRow(container, scheduler.SchedulerName);
        listed.Log.Should().BeNull("the persistent listing does not select EXECUTION_LOG");

        ExecutionHistoryEntry? read = await container.GetRequiredService<IExecutionHistoryStore>()
            .GetExecution(scheduler.SchedulerName, listed.EntryId!);

        read.Should().NotBeNull();
        read!.Log.Should().Contain("exporting page 1 of 2", "the single read is the one that carries the log");

        await scheduler.Shutdown(waitForJobsToComplete: true);
    }

    private static ServiceProvider BuildContainer(Action<IQuartzBuilder> configure)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddQuartzExecutionHistory();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = "capture-" + Guid.NewGuid().ToString("N");
                options.InstanceId = "one";
            });

            configure(q);
        });

        return services.BuildServiceProvider();
    }

    private static async Task<IScheduler> Start(ServiceProvider container)
    {
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Start();
        return scheduler;
    }

    private static async Task RunOnce<TJob>(IScheduler scheduler, string name) where TJob : IJob
    {
        JobKey key = new(name, "capture");
        await scheduler.AddJob(JobBuilder.Create<TJob>().WithIdentity(key).StoreDurably().Build());
        await scheduler.TriggerJob(key);
    }

    private static async Task<ExecutionHistoryEntry> SingleRow(ServiceProvider container, string schedulerName)
    {
        return (await Rows(container, schedulerName, count: 1)).Single();
    }

    private static async Task<List<ExecutionHistoryEntry>> Rows(ServiceProvider container, string schedulerName, int count)
    {
        IExecutionHistoryStore store = container.GetRequiredService<IExecutionHistoryStore>();
        DateTimeOffset deadline = DateTimeOffset.UtcNow + waitLimit;

        while (true)
        {
            PagedResult<ExecutionHistoryEntry> page = await store.QueryExecutions(new ExecutionHistoryQuery { SchedulerName = schedulerName });
            if (page.Items.Count >= count)
            {
                return page.Items.ToList();
            }

            if (DateTimeOffset.UtcNow > deadline)
            {
                Assert.Fail($"{count} history rows for {schedulerName} were not recorded in time; found {page.Items.Count}");
            }

            await Task.Delay(20);
        }
    }

    public sealed class LoggingJob : IJob
    {
        private readonly ILogger<LoggingJob> logger;

        public LoggingJob(ILogger<LoggingJob> logger)
        {
            this.logger = logger;
        }

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            logger.LogInformation("exporting page {Page} of {Pages}", 1, 2);

            // On another thread, as a job's awaited work often is: the firing flows with it.
            await Task.Run(() => logger.LogWarning("page {Page} was slow", 2), cancellationToken);
        }
    }

    public sealed class FailingJob : IJob
    {
        private readonly ILogger<FailingJob> logger;

        public FailingJob(ILogger<FailingJob> logger)
        {
            this.logger = logger;
        }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            logger.LogInformation("about to fail");
            throw new InvalidOperationException("the upstream system is down");
        }
    }

    /// <summary>
    /// Waits until every firing of it is running, then logs its own marker five times, yielding between
    /// lines so the firings' lines interleave.
    /// </summary>
    public sealed class MeetingJob : IJob
    {
        private static int arrived;
        private static int expected;
        private static TaskCompletionSource everyone = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly ILogger<MeetingJob> logger;

        public MeetingJob(ILogger<MeetingJob> logger)
        {
            this.logger = logger;
        }

        public static void Reset(int expected)
        {
            MeetingJob.expected = expected;
            arrived = 0;
            everyone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref arrived) >= expected)
            {
                everyone.TrySetResult();
            }

            await everyone.Task.WaitAsync(waitLimit, cancellationToken);

            string marker = context.MergedJobDataMap.GetString("marker")!;
            for (int line = 1; line <= 5; line++)
            {
                logger.LogInformation("{Marker} line {Line}", marker, line);
                await Task.Yield();
            }
        }
    }
}
