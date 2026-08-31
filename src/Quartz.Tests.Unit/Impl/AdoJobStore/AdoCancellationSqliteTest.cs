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

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// Cancelling a store operation that has a lock and a transaction open, against a real database.
/// </summary>
/// <remarks>
/// <para>
/// <c>AdoJobStoreCancellationTest</c> states which exception leaves each block; what it cannot show is
/// what a cancellation does to the work already in the transaction and to the lock the operation is
/// holding, because neither of those exists over a connection double. SQLite is a file, so both can be
/// shown here rather than only in the container legs: the row the cancelled operation wrote is gone,
/// and the operation after it takes the same lock without waiting.
/// </para>
/// <para>
/// The cancellation is raised from a driver delegate rather than by cancelling before the call, so it
/// happens where #3503 is about — after <c>ExecuteInLocalTransactionLock</c> has taken the lock, opened
/// the connection and begun the transaction.
/// </para>
/// </remarks>
public sealed class AdoCancellationSqliteTest
{
    private const string Group = "cancellation";

    private string databaseFile = null!;
    private string connectionString = null!;
    private CancellingSqliteDelegate driverDelegate = null!;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        databaseFile = Path.Combine(Path.GetTempPath(), $"quartz-cancellation-{Guid.NewGuid():N}.db");
        connectionString = $"Data Source={databaseFile}";
        driverDelegate = new CancellingSqliteDelegate();
    }

    [TearDown]
    public void DeleteDatabase()
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(databaseFile))
        {
            File.Delete(databaseFile);
        }
    }

    /// <summary>
    /// The case #3503 reported. Before the fix this came back as
    /// <c>JobPersistenceException("Unexpected runtime exception: …")</c>, so a caller could not tell a
    /// shutdown from a database that had fallen over, and their retry treated it as one.
    /// </summary>
    [Test]
    public async Task ACancellationInsideALockedTransactedOperationIsReportedAsCancellation()
    {
        await using SchedulerHandle handle = await BuildScheduler();
        IScheduler scheduler = handle.Scheduler;

        using CancellationTokenSource cancellation = new();

        // The row is written first and the token fires after it, so the transaction has work in it at
        // the moment the caller gives up - which is what makes the rollback assertion below mean
        // something.
        driverDelegate.AfterInsertingJobDetail = token =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return default;
        };

        Func<Task> act = async () => await Schedule(scheduler, "cancelled", cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>(
            "the caller asked to stop, and reporting that as a persistence failure sends their retry "
            + "logic after a database that is perfectly healthy");
    }

    /// <summary>
    /// The transaction the cancelled operation had open is rolled back, so a cancellation does not
    /// leave half of a scheduling behind.
    /// </summary>
    [Test]
    public async Task ACancelledOperationLeavesNothingItHadWrittenBehind()
    {
        await using SchedulerHandle handle = await BuildScheduler();
        IScheduler scheduler = handle.Scheduler;

        using CancellationTokenSource cancellation = new();
        driverDelegate.AfterInsertingJobDetail = token =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return default;
        };

        Func<Task> act = async () => await Schedule(scheduler, "cancelled", cancellation.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        (await scheduler.Exists(new JobKey("cancelled", Group))).Should().BeFalse(
            "the job row was inserted inside the transaction the cancellation ended, so it has to have "
            + "been rolled back rather than left as a job with no trigger");
    }

    /// <summary>
    /// The lock is handed back on the way out, so the next operation is not queued behind a lock whose
    /// owner has gone.
    /// </summary>
    [Test]
    public async Task ACancelledOperationReleasesTheLockItWasHolding()
    {
        await using SchedulerHandle handle = await BuildScheduler();
        IScheduler scheduler = handle.Scheduler;

        using CancellationTokenSource cancellation = new();
        driverDelegate.AfterInsertingJobDetail = token =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return default;
        };

        Func<Task> act = async () => await Schedule(scheduler, "cancelled", cancellation.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        driverDelegate.AfterInsertingJobDetail = null;

        // SQLite serializes every operation through one lock, so a lock the cancelled operation kept
        // would park this until the test timed out rather than fail it - hence the deadline.
        Task next = Schedule(scheduler, "afterwards", CancellationToken.None);
        await next.WaitAsync(TimeSpan.FromSeconds(30));

        (await scheduler.Exists(new JobKey("afterwards", Group))).Should().BeTrue(
            "the operation after a cancelled one takes the same lock and runs normally");
    }

    /// <summary>
    /// A shutdown is the cancellation this is really about: the host stops, every in-flight store call
    /// is given the stopping token, and what comes back has to be cancellation rather than a store
    /// failure the scheduler then reports to its listeners on the way down.
    /// </summary>
    [Test]
    public async Task AShutdownCancellingAnInFlightOperationLeavesTheSchedulerAbleToShutDownCleanly()
    {
        await using SchedulerHandle handle = await BuildScheduler();
        IScheduler scheduler = handle.Scheduler;

        // Started, so the shutdown has a scheduler thread and a store to take down rather than only a
        // status to set. Nothing fires: the triggers are a year out.
        await scheduler.Start();

        using CancellationTokenSource stopping = new();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        driverDelegate.AfterInsertingJobDetail = async token =>
        {
            entered.TrySetResult();
            await released.Task.ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
        };

        Task scheduling = Schedule(scheduler, "in-flight", stopping.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));

        // The host stops while that operation is inside the lock and the transaction.
        await stopping.CancelAsync();
        released.SetResult();

        Func<Task> act = async () => await scheduling;
        await act.Should().ThrowAsync<OperationCanceledException>();

        driverDelegate.AfterInsertingJobDetail = null;

        Task shutdown = scheduler.Shutdown(waitForJobsToComplete: true).AsTask();
        await shutdown.WaitAsync(TimeSpan.FromSeconds(30));

        scheduler.Status.Should().Be(SchedulerStatus.Shutdown,
            "the shutdown ran to the end - the cancelled operation released its lock and its connection "
            + "on the way out, so nothing was left for it to wait on");
    }

    private static Task Schedule(IScheduler scheduler, string name, CancellationToken cancellationToken)
    {
        IJobDetail job = JobBuilder.Create<NoOpCancellationJob>()
            .WithIdentity(name, Group)
            .Build();

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity(name, Group)
            .ForJob(job)
            // Far enough out that nothing fires while the test drives the store by hand.
            .StartAt(DateTimeOffset.UtcNow.AddYears(1))
            .Build();

        return scheduler.ScheduleJob(job, trigger, cancellationToken).AsTask();
    }

    private async Task<SchedulerHandle> BuildScheduler()
    {
        await ProvisionSchema();

        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = "cancellation";
                options.InstanceId = "one";
            });

            q.UsePersistentStore(store =>
            {
                // Registered before the dialect is chosen, because UseSqlite registers SQLiteDelegate
                // with TryAdd and the first registration is the one that wins.
                store.UseDriverDelegate(_ => driverDelegate);
                store.UseSqlite(SqliteFactory.Instance, connectionString);
            });
        });

        ServiceProvider container = services.BuildServiceProvider();
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();

        return new SchedulerHandle(container, scheduler);
    }

    /// <summary>
    /// Creates the tables with the stock delegate, because a delegate defined in a test assembly cannot
    /// create them: <c>StdAdoDelegate</c> reads its schema script out of the assembly that named it, so
    /// a subclass over here would be looking for the script in <c>Quartz.Tests.Unit</c>.
    /// </summary>
    private async Task ProvisionSchema()
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = "cancellation-schema";
                options.InstanceId = "provisioning";
            });

            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, connectionString);
                // #3550: the store creates the schema it needs, so the test needs no script of its own.
                store.ProvisionSchema();
            });
        });

        await using ServiceProvider container = services.BuildServiceProvider();
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Shutdown();
    }

    /// <summary>
    /// A SQLite delegate that hands the test control at one point inside a locked, transacted
    /// operation — after a row has been written, and while the connection, the transaction and the
    /// lock are all still held.
    /// </summary>
    private sealed class CancellingSqliteDelegate : SQLiteDelegate
    {
        public Func<CancellationToken, ValueTask>? AfterInsertingJobDetail { get; set; }

        public override async ValueTask<int> InsertJobDetail(
            ConnectionAndTransactionHolder conn,
            IJobDetail job,
            CancellationToken cancellationToken = default)
        {
            int inserted = await base.InsertJobDetail(conn, job, cancellationToken).ConfigureAwait(false);

            Func<CancellationToken, ValueTask>? hook = AfterInsertingJobDetail;
            if (hook is not null)
            {
                await hook(cancellationToken).ConfigureAwait(false);
            }

            return inserted;
        }
    }

    private sealed class SchedulerHandle : IAsyncDisposable
    {
        private readonly ServiceProvider container;

        public SchedulerHandle(ServiceProvider container, IScheduler scheduler)
        {
            this.container = container;
            Scheduler = scheduler;
        }

        public IScheduler Scheduler { get; }

        public async ValueTask DisposeAsync()
        {
            await Scheduler.Shutdown(waitForJobsToComplete: false);
            await container.DisposeAsync();
        }
    }

    public sealed class NoOpCancellationJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
