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

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// A firing that ends while an unwaited shutdown is still under way is recorded by the node that ran
/// it, rather than left in the database for somebody else to settle (#3746).
/// </summary>
/// <remarks>
/// <para>
/// The ADO store is where the loss is visible, because it is the store that refuses a late completion:
/// <c>RetryExecuteInLocalTransactionLock</c> loops <c>while (!shutdown)</c> and throws
/// <c>JobStore is shutdown - aborting retry</c> without attempting the write. What is left behind is a
/// fired-trigger row that still says <c>EXECUTING</c> and, for a
/// <see cref="DisallowConcurrentExecutionAttribute" /> job, a trigger still <c>BLOCKED</c> — which
/// outside a cluster nothing settles until the node next starts, and inside one nothing settles until
/// the leaver's check-in lapses.
/// </para>
/// <para>
/// The clustered two-node case is <c>UnwaitedShutdownClusteredPostgresTest</c>. This is the same claim
/// at unit cost: SQLite is a file, so a whole persistent store is a temporary path, and the rows are
/// read back with SQL rather than through the store that wrote them.
/// </para>
/// </remarks>
[NonParallelizable]
public sealed class UnwaitedShutdownSqliteTest
{
    private static readonly JobKey jobKey = new("parked", "unwaited");
    private static readonly TriggerKey triggerKey = new("t-parked", "unwaited");

    private SqliteTestDatabase database = null!;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        database = new SqliteTestDatabase("unwaited-shutdown");
        ParkingJob.Reset();
    }

    [TearDown]
    public void DeleteDatabase()
    {
        database.Dispose();
    }

    [Test]
    public async Task AnUnwaitedShutdownRecordsTheCompletionOfAFiringThatEndsOnTheWayOut()
    {
        await using ServiceProvider container = BuildContainer();
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();

        await scheduler.ScheduleJob(
            JobBuilder.Create<ParkingJob>().WithIdentity(jobKey).Build(),
            TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .StartNow()
                // Repeating, so that the trigger row is still there to be read afterwards — and so that
                // the state it is left in is one that matters to a schedule with firings still to come.
                .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
                .Build());

        await scheduler.Start();
        await ParkingJob.Started.WaitAsync(TimeSpan.FromSeconds(30));

        (await FiredTriggerStates()).Should().Equal(["EXECUTING"],
            "the premise: the firing is in flight and the store knows it, so what the shutdown leaves "
            + "behind is a question about this row");

        // The firing ends a fraction of a second after the node is told to come down, which is the
        // ordinary shape of it: it was already running, and it finishes while the shutdown is still
        // working through its steps.
        Task release = Task.Run(async () =>
        {
            await Task.Delay(200);
            ParkingJob.Release();
        });

        await scheduler.Shutdown(waitForJobsToComplete: false);
        await release;

        (await FiredTriggerStates()).Should().BeEmpty(
            "a firing that ran to completion has to be recorded as complete by the node that ran it, "
            + "whether or not that node waited for it — a fired-trigger row left behind is one a peer "
            + "has to time the leaver out to clear, and outside a cluster nobody clears it until this "
            + "node next starts");

        (await TriggerState()).Should().Be("WAITING",
            "the execution that was holding the trigger has finished, and the completion is what "
            + "unblocks a DisallowConcurrentExecution job's triggers — a trigger left BLOCKED is a "
            + "schedule that stops dead");
    }

    private async Task<List<string>> FiredTriggerStates()
    {
        return await ReadColumn("SELECT STATE FROM QRTZ_FIRED_TRIGGERS");
    }

    private async Task<string> TriggerState()
    {
        List<string> states = await ReadColumn(
            $"SELECT TRIGGER_STATE FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = '{triggerKey.Name}'");

        return states.Should().ContainSingle("the trigger repeats for ever, so its row is still there").Subject;
    }

    private async Task<List<string>> ReadColumn(string sql)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;

        List<string> values = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private ServiceProvider BuildContainer()
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = "unwaited-shutdown-sqlite";
                options.InstanceId = "one";
            });

            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                store.ProvisionSchema();
            });
        });

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Holds the trigger — which is what <see cref="DisallowConcurrentExecutionAttribute" /> makes the
    /// store do while an execution is inside — until the test lets it go.
    /// </summary>
    [DisallowConcurrentExecution]
    public sealed class ParkingJob : IJob
    {
        private static TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private static TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static Task Started => started.Task;

        public static void Reset()
        {
            started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public static void Release() => release.TrySetResult();

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            started.TrySetResult();

            // Deliberately not the job's own token: what is under test is a node coming down while a
            // firing is in flight, and a firing that turned into a cancellation would be a different
            // case with a different answer.
            await release.Task.ConfigureAwait(false);
        }
    }
}
