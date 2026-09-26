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

using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// A running job's progress, from <see cref="IJobExecutionContext.ReportProgress" /> to the fire-instance
/// listing, on both shipped stores — and the store-level contract underneath it.
/// </summary>
/// <remarks>
/// The ADO.NET store runs on a SQLite file, so the statement that writes the two columns and the one
/// that reads them are the real ones; the row is read back directly as well, to prove it is the column
/// that holds the value.
/// </remarks>
public sealed class FireInstanceProgressTest
{
    private static readonly TimeSpan waitLimit = TimeSpan.FromSeconds(20);
    private static readonly DateTimeOffset now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private SqliteTestDatabase? database;

    [SetUp]
    public void SetUp()
    {
        ReportingJob.Reset();
    }

    [TearDown]
    public void TearDown()
    {
        ReportingJob.Release();
        database?.Dispose();
        database = null;
    }

    [Test]
    public async Task TheInMemoryStoreListsWhatARunningJobReported()
    {
        await using ServiceProvider container = BuildContainer(q => q.UseInMemoryStore());

        FireInstance running = await RunAndObserve(container);

        running.Progress.Should().Be(42, "the job's report is what the listing answers with");
        running.ProgressMessage.Should().Be("forty-two");
    }

    [Test]
    public async Task ThePersistentStoreListsWhatARunningJobReportedFromItsOwnRow()
    {
        database = new SqliteTestDatabase("fire-progress");
        string connectionString = database.ConnectionString;

        await using ServiceProvider container = BuildContainer(q => q.UsePersistentStore(store =>
        {
            store.UseSqlite(SqliteFactory.Instance, connectionString);
            store.ProvisionSchema();
        }));

        FireInstance running = await RunAndObserve(container, async observed =>
        {
            await using SqliteConnection connection = new(connectionString);
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT PROGRESS, PROGRESS_MESSAGE FROM QRTZ_FIRED_TRIGGERS WHERE ENTRY_ID = @id";
            command.Parameters.AddWithValue("@id", observed.FireInstanceId);

            await using SqliteDataReader reader = await command.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue("the firing is still running, so its row is still there");
            reader.GetInt32(0).Should().Be(42, "the percentage is kept on the firing's own row");
            reader.GetString(1).Should().Be("forty-two");
        });

        running.Progress.Should().Be(42, "the listing reads the column the write set");
        running.ProgressMessage.Should().Be("forty-two");
    }

    [Test]
    public async Task ThePersistentStoreUpdatesNothingForAFiringThatHasGone()
    {
        database = new SqliteTestDatabase("fire-progress-gone");
        string connectionString = database.ConnectionString;

        await using ServiceProvider container = BuildContainer(q => q.UsePersistentStore(store =>
        {
            store.UseSqlite(SqliteFactory.Instance, connectionString);
            store.ProvisionSchema();
        }));

        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
        IJobStore store = JobStores.Unwrap(((StdScheduler) scheduler).scheduler.resources.JobStore);

        Func<Task> update = async () => await store.UpdateFireInstanceProgress(
            "no-such-firing", new FireInstanceProgress { Percent = 10 });

        await update.Should().NotThrowAsync(
            "a report that lands after its firing completed finds no row, which is not an error");

        await scheduler.Shutdown();
    }

    [Test]
    public async Task TheInMemoryStoreRecordsProgressOnTheExecutionItNames()
    {
        RAMJobStore store = TestJobStores.Ram();
        await store.Initialize(TestJobStores.Identity());
        await store.SchedulerStarted();

        IJobDetail job = JobBuilder.Create<ReportingJob>().WithIdentity("job", "progress").Build();
        IOperableTrigger first = Trigger("first", job);
        IOperableTrigger second = Trigger("second", job);
        await store.ScheduleJob(job, first);
        await store.AddTrigger(second);

        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = now.AddDays(1),
            MaxCount = 2,
        });
        await store.TriggersFired(acquired);

        IOperableTrigger reported = acquired.Single(x => x.Key.Equals(first.Key));
        await store.UpdateFireInstanceProgress(reported.FireInstanceId!, new FireInstanceProgress { Percent = 30, Message = "a third" });
        await store.UpdateFireInstanceProgress("no-such-firing", new FireInstanceProgress { Percent = 99 });

        PagedResult<FireInstance> executing = await store.QueryFireInstances(new FireInstanceQuery());

        FireInstance withProgress = executing.Items.Single(x => x.FireInstanceId == reported.FireInstanceId);
        withProgress.Progress.Should().Be(30);
        withProgress.ProgressMessage.Should().Be("a third");

        executing.Items.Single(x => x.FireInstanceId != reported.FireInstanceId).Progress.Should().BeNull(
            "a report names one firing, and the other execution has reported nothing");

        await store.TriggeredJobComplete(reported, job, SchedulerInstruction.NoInstruction);

        (await store.QueryFireInstances(new FireInstanceQuery())).Items.Should().NotContain(
            x => x.FireInstanceId == reported.FireInstanceId,
            "the progress lives on the execution's entry, and the completion takes the entry away");
    }

    [Test]
    public async Task TheInMemoryStoreCutsALongMessage()
    {
        RAMJobStore store = TestJobStores.Ram();
        await store.Initialize(TestJobStores.Identity());
        await store.SchedulerStarted();

        IJobDetail job = JobBuilder.Create<ReportingJob>().WithIdentity("job", "progress").Build();
        IOperableTrigger trigger = Trigger("only", job);
        await store.ScheduleJob(job, trigger);

        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = now.AddDays(1),
            MaxCount = 1,
        });
        await store.TriggersFired(acquired);

        await store.UpdateFireInstanceProgress(acquired[0].FireInstanceId!, new FireInstanceProgress { Percent = 1, Message = new string('m', 400) });

        FireInstance instance = (await store.QueryFireInstances(new FireInstanceQuery())).Items.Single();
        instance.ProgressMessage.Should().HaveLength(FireInstanceProgress.MaxMessageLength,
            "a store handed a message by something other than the scheduler keeps what the persistent store could");
    }

    private static async Task<FireInstance> RunAndObserve(ServiceProvider container, Func<FireInstance, Task>? whileRunning = null)
    {
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();

        JobKey key = new("reporting", "progress");
        await scheduler.AddJob(JobBuilder.Create<ReportingJob>().WithIdentity(key).StoreDurably().Build());
        await scheduler.Start();
        await scheduler.TriggerJob(key);

        FireInstance? observed = null;
        DateTimeOffset deadline = DateTimeOffset.UtcNow + waitLimit;
        while (DateTimeOffset.UtcNow < deadline)
        {
            PagedResult<FireInstance> page = await scheduler.QueryFireInstances(new FireInstanceQuery());
            observed = page.Items.FirstOrDefault(x => x.Progress is not null);
            if (observed is not null)
            {
                break;
            }

            await Task.Delay(20);
        }

        observed.Should().NotBeNull("the first report of a firing is written at once, so it is listed while the job runs");

        if (whileRunning is not null)
        {
            await whileRunning(observed!);
        }

        ReportingJob.Release();
        await scheduler.Shutdown(waitForJobsToComplete: true);

        return observed!;
    }

    private static ServiceProvider BuildContainer(Action<IQuartzBuilder> configure)
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = "progress-" + Guid.NewGuid().ToString("N");
                options.InstanceId = "one";
            });

            configure(q);
        });

        return services.BuildServiceProvider();
    }

    private static IOperableTrigger Trigger(string name, IJobDetail job)
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(name, "progress")
            .ForJob(job)
            .StartAt(now)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build();

        trigger.ComputeFirstFireTimeUtc(calendar: null);
        return trigger;
    }

    /// <summary>
    /// Reports once and then waits to be let go, so that the firing is still running while the test
    /// reads the listing.
    /// </summary>
    public sealed class ReportingJob : IJob
    {
        private static TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static void Reset()
        {
            release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public static void Release()
        {
            release.TrySetResult();
        }

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            context.ReportProgress(42, "forty-two");
            await release.Task.WaitAsync(waitLimit, cancellationToken);
        }
    }
}
