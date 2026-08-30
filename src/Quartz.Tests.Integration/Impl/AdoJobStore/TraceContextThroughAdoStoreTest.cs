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

using System.Diagnostics;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Diagnostics;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The link from a firing back to the call that scheduled it, across an actual database.
/// </summary>
/// <remarks>
/// <para>
/// The unit tests for this run against the in-memory store, where the trigger the scheduler was handed
/// is the trigger the job is fired from — so they cannot tell a value that was stored from one that was
/// merely still in memory. Here the trigger is written to a SQLite file as job data, read back out by
/// the scheduler thread as a row, and the firing's span still carries the link. That is the round trip
/// the feature is actually for: the node that schedules the job and the node that runs it are usually
/// not the same process, and everything they share went through the store.
/// </para>
/// <para>
/// SQLite on a file needs no container, so this runs wherever the unit tests do.
/// </para>
/// </remarks>
[NonParallelizable]
public sealed class TraceContextThroughAdoStoreTest
{
    private string databaseFile;
    private ActivityListener listener;
    private readonly List<Activity> stoppedActivities = [];

    [SetUp]
    public void SetUp()
    {
        RecordingJob.Reset();

        lock (stoppedActivities)
        {
            stoppedActivities.Clear();
        }

        listener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name == QuartzInstrumentation.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (stoppedActivities)
                {
                    stoppedActivities.Add(activity);
                }
            }
        };
        ActivitySource.AddActivityListener(listener);
    }

    [TearDown]
    public void TearDown()
    {
        listener?.Dispose();

        // Pools first, or the handle the store left behind keeps the file locked on Windows.
        SqliteConnection.ClearAllPools();

        if (databaseFile is not null && File.Exists(databaseFile))
        {
            try
            {
                File.Delete(databaseFile);
            }
            catch (IOException)
            {
                // scratch space; leaving one behind is not worth failing a passing test over
            }
        }
    }

    [Test]
    public async Task TheLinkToTheSchedulingActivitySurvivesTheStore()
    {
        await PrepareDatabase();

        string id = Guid.NewGuid().ToString("N");
        JobKey jobKey = new($"traced-{id}", "trace");
        TriggerKey triggerKey = new($"traced-{id}", "trace");

        ServiceCollection services = new();
        services.AddQuartz(quartz =>
        {
            quartz.ConfigureScheduler(options => options.InstanceName = $"trace-ado-{id}");
            quartz.UsePersistentStore(store => store.UseSqlite(ConnectionString));
        });

        await using ServiceProvider provider = services.BuildServiceProvider();

        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        try
        {
            IJobDetail job = JobBuilder.Create<RecordingJob>()
                .WithIdentity(jobKey)
                .StoreDurably()
                .Build();

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .ForJob(jobKey)
                .StartAt(DateTimeOffset.UtcNow.AddSeconds(1))
                .Build();

            // Started rather than merely constructed, because Activity.Current refuses a finished
            // activity — and put back to null as soon as the scheduling calls are done, so that the
            // scheduler's worker cannot inherit it and make the firing a child instead of a link.
            using Activity scheduling = new Activity("caller.schedules").SetIdFormat(ActivityIdFormat.W3C).Start();
            try
            {
                await scheduler.AddJob(job);
                await scheduler.ScheduleJob(trigger);
            }
            finally
            {
                Activity.Current = null;
            }

            // Read back from the store rather than from the object that was handed to it: everything
            // between here and the firing is rows.
            ITrigger stored = await scheduler.GetTrigger(triggerKey);
            stored.Should().NotBeSameAs(trigger, "the store returns a trigger it materialized from a row");
            stored.JobDataMap.Should().ContainKey(SchedulerConstants.TraceParent)
                .WhoseValue.Should().Be(scheduling.Id,
                    "the traceparent is an ordinary job data value, so it goes through whatever the store "
                    + "does with job data — including StoreJobDataAsStrings and the JSON write gate");

            await scheduler.Start();

            (await RecordingJob.Fired.WaitAsync(TimeSpan.FromSeconds(60))).Should().Be(jobKey);

            Activity execute = await WaitForExecuteActivity(jobKey);

            ActivityLink link = execute.Links.Should().ContainSingle(
                "the firing links back to the one call that scheduled it").Subject;
            link.Context.TraceId.Should().Be(scheduling.TraceId,
                "the whole point is walking from a firing back to the request that asked for it, and the "
                + "trace id is what a backend searches by");
            link.Context.SpanId.Should().Be(scheduling.SpanId);
            link.Context.IsRemote.Should().BeTrue(
                "the context came out of storage rather than being created in this process");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    private async Task<Activity> WaitForExecuteActivity(JobKey jobKey)
    {
        // The job signals from inside Execute, and the span is closed after Execute returns.
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            lock (stoppedActivities)
            {
                Activity found = stoppedActivities.Find(a =>
                    a.OperationName == OperationName.Job.Execute
                    && Equals(a.GetTagItem(ActivityTags.JobName), jobKey.Name));

                if (found is not null)
                {
                    return found;
                }
            }

            await Task.Delay(50);
        }

        Assert.Fail($"No {OperationName.Job.Execute} activity was recorded for {jobKey}.");
        return null;
    }

    private string ConnectionString => $"Data Source={databaseFile};";

    private async Task PrepareDatabase()
    {
        databaseFile = $"trace-context-{Guid.NewGuid():N}.db";

        await using SqliteConnection connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();

        await using SqliteCommand command = new SqliteCommand(LoadTableScript(), connection);
        await command.ExecuteNonQueryAsync();
    }

    private static string LoadTableScript()
    {
        DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "database", "tables", "tables_sqlite.sql");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate database/tables/tables_sqlite.sql from " + AppContext.BaseDirectory);
    }

    /// <summary>
    /// Reports that it ran, so the assertions never race the firing they are about.
    /// </summary>
    public sealed class RecordingJob : IJob
    {
        private static volatile TaskCompletionSource<JobKey> fired = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static Task<JobKey> Fired => fired.Task;

        public static void Reset() => fired = new TaskCompletionSource<JobKey>(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            fired.TrySetResult(context.JobDetail.Key);
            return default;
        }
    }
}
