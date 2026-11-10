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

using System.Globalization;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Tests.Unit.Plugin.History;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// A firing that ends after its scheduler stopped waiting for it and shut its store down is refused by
/// the store, so its fired-trigger row stays and recovery runs the job again (#4260).
/// </summary>
/// <remarks>
/// <para>
/// The store refuses a completion that arrives after it has shut down: the locked path loops
/// <c>while (!shutdown)</c> and throws <c>JobStore is shutdown - aborting retry</c> without a statement,
/// which is what log event 1006 warns of. The lock-free completion (#3863) read no such flag, so the
/// completion of a durable job that may run beside itself committed after the shutdown, deleting the
/// fired-trigger row that recovery reads, and the job never ran again.
/// </para>
/// <para>
/// SQLite serializes every operation, which keeps every completion under the lock. The serialization is
/// switched off once the scheduler is down and the late completion is the only work left, so that the
/// completion takes the path it takes on every other database. SQLite cannot be clustered, so "runs
/// again" is this node starting again under the same instance id.
/// </para>
/// </remarks>
[NonParallelizable]
public sealed class CompletionAfterShutdownSqliteTest
{
    private const string SchedulerName = "completion-after-shutdown";
    private const string InstanceId = "node-a";

    /// <summary>Log event <c>CoreLog.GaveUpWaitingForRunningJobs</c>.</summary>
    private const int GaveUpWaitingForRunningJobs = 1006;

    private static readonly TimeSpan waitLimit = TimeSpan.FromSeconds(30);

    private static readonly JobKey jobKey = new("blocking", "late");
    private static readonly TriggerKey triggerKey = new("t-blocking", "late");

    private SqliteTestDatabase database = null!;
    private RecordingLoggerProvider logs = null!;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        database = new SqliteTestDatabase("completion-after-shutdown");
        logs = new RecordingLoggerProvider();
        BlockingJob.Reset();
    }

    [TearDown]
    public void DeleteDatabase()
    {
        logs.Dispose();
        database.Dispose();
    }

    /// <summary>
    /// The job returns normally after the shutdown gave up waiting for it. Its completion is refused, the
    /// fired-trigger row is still <c>EXECUTING</c> and asking for recovery, and the next start of the node
    /// runs the job again, told it is recovering.
    /// </summary>
    [Test]
    public async Task AFiringThatEndsAfterTheStoreShutDownIsLeftForRecoveryAndRunsAgain()
    {
        await using (ServiceProvider first = BuildContainer())
        {
            IScheduler scheduler = await first.GetRequiredService<ISchedulerFactory>().GetScheduler();
            WatchingJobStore watched = (WatchingJobStore) first.GetRequiredService<IJobStore>();

            // A durable job, so that the completion of its spent one-off deletes no job and takes no lock.
            await scheduler.ScheduleJob(
                JobBuilder.Create<BlockingJob>().WithIdentity(jobKey).StoreDurably().RequestRecovery().Build(),
                TriggerBuilder.Create().WithIdentity(triggerKey).StartNow().Build());

            await scheduler.Start();
            await BlockingJob.Entered.WaitAsync(waitLimit);

            // A deadline that has passed already, as a host's shutdown budget has when a job outlives it:
            // the scheduler gives up waiting at once and shuts the store down under the running job.
            await scheduler.Shutdown(waitForJobsToComplete: true, new CancellationToken(canceled: true));

            logs.Entries.Should().ContainSingle(x => x.EventId.Id == GaveUpWaitingForRunningJobs,
                "the premise: the scheduler stopped waiting while the job ran");
            (await FiredRows()).Should().Equal([new FiredRow("EXECUTING", RequestsRecovery: true)],
                "the premise: the firing is in flight, and its row asks for recovery");

            watched.Store.LockAllOperations = false;

            BlockingJob.Release();
            Exception? refusal = await watched.Completed.WaitAsync(waitLimit);

            (await FiredRows()).Should().Equal([new FiredRow("EXECUTING", RequestsRecovery: true)],
                "a completion that arrives after the store has shut down is refused, and the row is what recovery reads");
            refusal.Should().BeOfType<InvalidOperationException>(
                    "the store refuses the completion as its locked path always has")
                .Which.Message.Should().Contain("JobStore is shutdown");
        }

        await using ServiceProvider restarted = BuildContainer();
        IScheduler again = await restarted.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await again.Start();

        Recovered recovered = await BlockingJob.Recovered.WaitAsync(waitLimit);
        await again.Shutdown(waitForJobsToComplete: true);

        recovered.Recovering.Should().BeTrue("the job runs again as the recovery of the firing the shutdown left behind");
        recovered.RecoveringTriggerKey.Should().Be(triggerKey, "the recovery run names the trigger whose firing it stands in for");
        (await FiredRows()).Should().BeEmpty("the recovery run completed, with the store up");
    }

    private async Task<List<FiredRow>> FiredRows()
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT STATE, REQUESTS_RECOVERY FROM QRTZ_FIRED_TRIGGERS";

        List<FiredRow> rows = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new FiredRow(reader.GetString(0), Convert.ToBoolean(reader.GetValue(1), CultureInfo.InvariantCulture)));
        }

        return rows;
    }

    private ServiceProvider BuildContainer()
    {
        ServiceCollection services = new();
        services.AddLogging(logging => logging.AddProvider(logs));
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = SchedulerName;
                options.InstanceId = InstanceId;
            });

            // First wins: the persistent store below is configured, and this is the store built.
            q.UseJobStore(provider => new WatchingJobStore(ActivatorUtilities.CreateInstance<LocalTransactionJobStore>(provider)));

            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                store.ProvisionSchema();
            });
        });

        return services.BuildServiceProvider();
    }

    private sealed record FiredRow(string State, bool RequestsRecovery);

    public sealed record Recovered(bool Recovering, TriggerKey? RecoveringTriggerKey);

    /// <summary>
    /// The store, saying when the first completion has been through it and how that ended.
    /// </summary>
    private sealed class WatchingJobStore(LocalTransactionJobStore inner) : DelegatingJobStore(inner)
    {
        private readonly TaskCompletionSource<Exception?> completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public LocalTransactionJobStore Store { get; } = inner;

        /// <summary>What the first completion threw, or <see langword="null" /> when it committed.</summary>
        public Task<Exception?> Completed => completed.Task;

        public override async ValueTask FiringComplete(TriggeredJobCompleteContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                await base.FiringComplete(context, cancellationToken);
                completed.TrySetResult(null);
            }
            catch (Exception e)
            {
                completed.TrySetResult(e);
                throw;
            }
        }
    }

    /// <summary>
    /// Waits for the test to let it go, ignoring its token, and returns normally; a recovering run records
    /// what it was told and returns at once.
    /// </summary>
    public sealed class BlockingJob : IJob
    {
        private static TaskCompletionSource<bool> entered = NewSource<bool>();
        private static TaskCompletionSource<bool> release = NewSource<bool>();
        private static TaskCompletionSource<Recovered> recovered = NewSource<Recovered>();

        public static Task Entered => entered.Task;

        public static Task<Recovered> Recovered => recovered.Task;

        public static void Reset()
        {
            entered = NewSource<bool>();
            release = NewSource<bool>();
            recovered = NewSource<Recovered>();
        }

        public static void Release() => release.TrySetResult(true);

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            if (context.Recovering)
            {
                recovered.TrySetResult(new Recovered(context.Recovering, context.RecoveringTriggerKey));
                return;
            }

            entered.TrySetResult(true);

            // Deliberately not the job's own token: a job that stopped on it would be a cancellation, which
            // is the hand-back's case (#4014), not this one.
            await release.Task.ConfigureAwait(false);
        }

        private static TaskCompletionSource<T> NewSource<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
