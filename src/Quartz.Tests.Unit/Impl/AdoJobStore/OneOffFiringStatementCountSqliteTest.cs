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

using System.Diagnostics;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// How many statements one <em>one-off</em> firing costs an ADO store, counted at the driver delegate
/// and proved against a real database.
/// </summary>
/// <remarks>
/// <para>
/// A one-off is the shape <c>IScheduler.ScheduleJob&lt;TJob, TInput&gt;</c> produces — a durable job
/// per job type and a single-shot trigger per firing — and it is the workload #3824 is about: its
/// completion deletes the trigger rather than writing it forward, and the deletion used to repeat
/// work the completion had already done.
/// </para>
/// <para>
/// The counting is at <see cref="IDriverDelegate" /> rather than at the database, because that is the
/// level a regression would be introduced at and the level a reader can check against the code. What
/// the database is here for is the other half of every assertion: the rows the firing leaves behind
/// have to be the same rows, or a round trip was removed by removing work.
/// </para>
/// <para>
/// SQLite because it is a file. <c>JobStoreContractTest</c> makes the behavioural claims on every
/// dialect leg, and those legs need Docker; the statement <em>count</em> is dialect-independent.
/// </para>
/// </remarks>
public sealed class OneOffFiringStatementCountSqliteTest
{
    private const string Group = "oneOff";

    private static readonly TimeSpan observationDeadline = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long after the scheduler is started the firing is due, so that the counters can be zeroed
    /// after start-up recovery and before anything fires.
    /// </summary>
    private static readonly TimeSpan dueIn = TimeSpan.FromSeconds(2);

    private SqliteTestDatabase database = null!;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        database = new SqliteTestDatabase("one-off-statements");
        CountingSqliteDelegate.Reset();
        RecordingJob.Reset();
    }

    [TearDown]
    public void DeleteDatabase()
    {
        database.Dispose();
    }

    /// <summary>
    /// The completion settles the triggers awaiting this one against the outcome its firing reached,
    /// and the deletion that follows used to ask the same question again — a round trip whose answer
    /// the settlement had just made empty.
    /// </summary>
    [Test]
    public async Task ACompletionThatDeletesItsTriggerScansForContinuationsOnce()
    {
        await using ServiceProvider container = BuildContainer();
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();

        await ScheduleOneOff(scheduler, "solo");
        await scheduler.Start();
        CountingSqliteDelegate.Reset();

        await WaitForTheFiringToBeComplete();

        CountingSqliteDelegate.AwaitingContinuationScans.Should().Be(1,
            "the completion settles this trigger's continuations before deleting it, so the deletion's own scan would find nothing AWAITING and is a round trip for an answer already known");

        await scheduler.Shutdown(waitForJobsToComplete: false);
    }

    /// <summary>
    /// The saved round trip must not be one that was doing something: a continuation waiting on the
    /// one-off still has to be released by the completion that deletes it.
    /// </summary>
    [Test]
    public async Task AContinuationWaitingOnAOneOffIsStillReleasedByTheCompletionThatDeletesIt()
    {
        await using ServiceProvider container = BuildContainer();
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();

        ScheduledOneOffJob parent = await ScheduleOneOff(scheduler, "parent");
        await scheduler.ScheduleJob<RecordingJob, string>(
            "after",
            Continuation.After(parent.TriggerKey),
            new OneOffJobOptions { Name = "after", Group = Group });

        await scheduler.Start();

        await WaitFor(() => RecordingJob.Ran("after"),
            "the continuation to run once the parent's completion had released it");

        RecordingJob.Ran("parent").Should().BeTrue("the parent is what released it");

        await scheduler.Shutdown(waitForJobsToComplete: false);
    }

    /// <summary>
    /// Waits until the one-off's row is gone, which is what its completion does and therefore the
    /// only signal that the completion has run rather than merely the job body.
    /// </summary>
    private async Task WaitForTheFiringToBeComplete()
    {
        await WaitFor(async () => await CountRows("QRTZ_TRIGGERS") == 0, "the completion to delete the one-off's trigger row");

        (await CountRows("QRTZ_FIRED_TRIGGERS")).Should().Be(0,
            "a completed firing leaves no fired-trigger row, whichever statement removed it");
        (await CountRows("QRTZ_SIMPLE_TRIGGERS")).Should().Be(0,
            "the trigger's schedule goes with the trigger");
        (await CountRows("QRTZ_JOB_DETAILS")).Should().Be(1,
            "the durable job of a one-off stays behind, one row per job type whatever the traffic");
    }

    private async Task<ScheduledOneOffJob> ScheduleOneOff(IScheduler scheduler, string name)
    {
        return await scheduler.ScheduleJob<RecordingJob, string>(
            name,
            scheduler.TimeProvider.GetUtcNow() + dueIn,
            new OneOffJobOptions { Name = name, Group = Group });
    }

    private async Task<long> CountRows(string table)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM " + table;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task WaitFor(Func<Task<bool>> condition, string because)
    {
        long started = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(started) < observationDeadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail("Waited " + observationDeadline + " for " + because + ", and it did not happen.");
    }

    private static Task WaitFor(Func<bool> condition, string because)
    {
        return WaitFor(() => Task.FromResult(condition()), because);
    }

    private ServiceProvider BuildContainer()
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = "one-off-statements";
                options.InstanceId = "one";
            });

            q.UsePersistentStore(store =>
            {
                // Before UseSqlite, which registers the delegate it names: the registrations are
                // try-add, so the first one in wins and this subclass would otherwise never be built.
                store.UseDriverDelegate<CountingSqliteDelegate>();
                store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                store.ProvisionSchema();
            });
        });

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// The shipped SQLite delegate with a tally on the members a one-off firing's round trips are
    /// counted in.
    /// </summary>
    /// <remarks>
    /// Static counters because the container builds the delegate, and the fixture runs one scheduler
    /// at a time.
    /// </remarks>
    public sealed class CountingSqliteDelegate : SQLiteDelegate
    {
        private static int awaitingContinuationScans;

        public static int AwaitingContinuationScans => Volatile.Read(ref awaitingContinuationScans);

        public static void Reset()
        {
            Volatile.Write(ref awaitingContinuationScans, 0);
        }

        public override ValueTask<List<AwaitingContinuation>> SelectAwaitingContinuations(
            ConnectionAndTransactionHolder conn,
            TriggerKey parent,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref awaitingContinuationScans);
            return base.SelectAwaitingContinuations(conn, parent, cancellationToken);
        }
    }

    /// <summary>Records that it ran, so a continuation can be proved to have been released.</summary>
    public sealed class RecordingJob : IJob<string>
    {
        private static readonly HashSet<string> ran = [];

        public static void Reset()
        {
            lock (ran)
            {
                ran.Clear();
            }
        }

        public static bool Ran(string name)
        {
            lock (ran)
            {
                return ran.Contains(name);
            }
        }

        public ValueTask Execute(IJobExecutionContext context, string input, CancellationToken cancellationToken = default)
        {
            lock (ran)
            {
                ran.Add(input);
            }

            return default;
        }
    }
}
