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

using Quartz.Jobs;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// Settling a continuation against a real database: the statements, the state column and the
/// transaction they run in.
/// </summary>
/// <remarks>
/// <para>
/// <c>JobStoreContractTest</c> makes these claims of every store on every dialect leg, but those legs
/// need Docker. SQLite is a file, so a whole persistent store is a temporary path — and the path the
/// assertions run through here is the ADO store's own completion path, with the row in
/// <c>QRTZ_TRIGGERS</c> read back to prove it is the column that changed rather than something in
/// memory.
/// </para>
/// <para>
/// The scheduler drives it, so the run shell classifies the firing and the outcome reaches the store
/// the way it does in an application — rather than the test naming the outcome itself, which would
/// prove nothing about the two halves agreeing.
/// </para>
/// </remarks>
public sealed class AdoContinuationSqliteTest
{
    private const string Group = "continuations";

    private static readonly TimeSpan observationDeadline = TimeSpan.FromSeconds(30);

    private SqliteTestDatabase database = null!;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        database = new SqliteTestDatabase("continuations");
        OutcomeJob.Reset();
    }

    [TearDown]
    public void DeleteDatabase()
    {
        database.Dispose();
    }

    [Test]
    public async Task AContinuationIsStoredAwaitingWithItsParentInTheRow()
    {
        await using ServiceProvider container = BuildContainer();
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();

        TriggerKey parent = new TriggerKey("parent", Group);
        TriggerKey continuation = new TriggerKey("waiting", Group);

        await ScheduleParked(scheduler, parent);
        await ScheduleContinuation(scheduler, continuation, parent, ContinuationCondition.OnSuccess);

        (await scheduler.GetTriggerState(continuation)).Should().Be(TriggerState.Awaiting,
            "the state is what keeps the trigger out of every acquisition, and it is read back from the column");

        (await ReadColumn("TRIGGER_STATE", continuation)).Should().Be("AWAITING");
        (await ReadColumn("CONTINUES_TRIGGER_NAME", continuation)).Should().Be("parent");
        (await ReadColumn("CONTINUES_TRIGGER_GROUP", continuation)).Should().Be(Group);
        Convert.ToInt32(await ReadColumn("CONTINUATION_CONDITION", continuation))
            .Should().Be((int) ContinuationCondition.OnSuccess,
                "the condition is persisted as the integer of the flags, which is what the settlement statement reads");

        await scheduler.Shutdown(waitForJobsToComplete: false);
    }

    [Test]
    public async Task AParentThatSucceedsReleasesTheContinuationAndItRuns()
    {
        await using ServiceProvider container = BuildContainer();
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();

        TriggerKey parent = new TriggerKey("parent", Group);
        TriggerKey continuation = new TriggerKey("after-success", Group);

        // Both stored before the scheduler is started, so the continuation is waiting by the time the
        // parent becomes due. TriggerJob would not do: it fires a volatile trigger of its own, and a
        // continuation waits for a trigger rather than for a job.
        await ScheduleParked(scheduler, parent, dueIn: TimeSpan.FromSeconds(1));
        await ScheduleContinuation(scheduler, continuation, parent, ContinuationCondition.OnSuccess);

        await scheduler.Start();

        await WaitFor(() => OutcomeJob.Ran("after-success"),
            "the continuation to run once the parent's completion had released it");

        OutcomeJob.Ran("parent").Should().BeTrue("the parent is what released it");

        await scheduler.Shutdown(waitForJobsToComplete: false);
    }

    [Test]
    public async Task AParentThatFailsDiscardsAContinuationWaitingForSuccess()
    {
        await using ServiceProvider container = BuildContainer();
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();

        TriggerKey parent = new TriggerKey("failing-parent", Group);
        TriggerKey discarded = new TriggerKey("never-runs", Group);
        TriggerKey released = new TriggerKey("cleanup", Group);

        await ScheduleParked(scheduler, parent, fail: true, dueIn: TimeSpan.FromSeconds(1));
        await ScheduleContinuation(scheduler, discarded, parent, ContinuationCondition.OnSuccess);
        await ScheduleContinuation(scheduler, released, parent, ContinuationCondition.OnFailure);

        await scheduler.Start();

        await WaitFor(() => OutcomeJob.Ran("cleanup"),
            "the continuation waiting on a failure to run, since the parent failed");

        (await ReadColumn("TRIGGER_STATE", discarded)).Should().BeNull(
            "a continuation whose condition the outcome did not name is deleted — the row is gone, not parked");

        (await scheduler.Exists(discarded)).Should().BeFalse();
        OutcomeJob.Ran("never-runs").Should().BeFalse("the job it would have fired must not have run");

        await scheduler.Shutdown(waitForJobsToComplete: false);
    }

    /// <summary>
    /// Deleting the parent is the one settlement with no outcome to match, and the store has to write
    /// the two answers it has rather than leaving the rows waiting for a firing that cannot come.
    /// </summary>
    [Test]
    public async Task DeletingTheParentParksWhatCaredAndReleasesWhatDidNot()
    {
        await using ServiceProvider container = BuildContainer();
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();

        TriggerKey parent = new TriggerKey("doomed-parent", Group);
        TriggerKey onSuccess = new TriggerKey("orphaned", Group);
        TriggerKey onAny = new TriggerKey("indifferent", Group);

        await ScheduleParked(scheduler, parent);
        await ScheduleContinuation(scheduler, onSuccess, parent, ContinuationCondition.OnSuccess);
        await ScheduleContinuation(scheduler, onAny, parent, ContinuationCondition.OnAnyOutcome);

        await scheduler.UnscheduleJob(parent);

        (await ReadColumn("TRIGGER_STATE", onSuccess)).Should().Be("ERROR",
            "the question it asked has no answer now, which is an operator's to see");
        (await ReadColumn("TRIGGER_STATE", onAny)).Should().Be("WAITING",
            "'however it ends' did not care how it ended, and it has ended");

        (await scheduler.ResetTriggerFromErrorState(onSuccess)).Should().BeTrue();

        (await ReadColumn("TRIGGER_STATE", onSuccess)).Should().Be("WAITING");
        long nextFireTime = Convert.ToInt64(await ReadColumn("NEXT_FIRE_TIME", onSuccess));
        nextFireTime.Should().BeGreaterThan(0,
            "resetting a parked continuation means running it, so the reset gives it the fire time a "
            + "release would have — the one it had while waiting was never a time the schedule chose");

        await scheduler.Shutdown(waitForJobsToComplete: false);
    }

    /// <summary>
    /// The parent trigger. By default far enough out that nothing fires it at all; give
    /// <paramref name="dueIn" /> to have it fire shortly after the scheduler is started.
    /// </summary>
    private static async Task ScheduleParked(
        IScheduler scheduler,
        TriggerKey key,
        bool fail = false,
        TimeSpan? dueIn = null)
    {
        IJobDetail job = JobBuilder.Create<OutcomeJob>()
            .WithIdentity(key.Name, key.Group)
            .UsingJobData(OutcomeJob.FailKey, fail)
            .Build();

        await scheduler.ScheduleJob(job, TriggerBuilder.Create()
            .WithIdentity(key)
            .ForJob(job)
            .StartAt(DateTimeOffset.UtcNow + (dueIn ?? TimeSpan.FromDays(1)))
            .Build());
    }

    private static async Task ScheduleContinuation(
        IScheduler scheduler,
        TriggerKey key,
        TriggerKey parent,
        ContinuationCondition condition)
    {
        IJobDetail job = JobBuilder.Create<OutcomeJob>()
            .WithIdentity(key.Name, key.Group)
            .Build();

        await scheduler.ScheduleJob(job, TriggerBuilder.Create()
            .WithIdentity(key)
            .ForJob(job)
            // In the past, so the release's max(now, START_TIME) is "now" and the trigger is due the
            // instant it stops awaiting.
            .StartAt(DateTimeOffset.UtcNow.AddMinutes(-1))
            .StartAfter(parent, condition)
            .Build());
    }

    /// <summary>
    /// One column of one trigger's row, or <see langword="null" /> when there is no such row — which
    /// is how a discarded continuation is told from a parked one.
    /// </summary>
    private async Task<object?> ReadColumn(string column, TriggerKey key)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            $"SELECT {column} FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = @name AND TRIGGER_GROUP = @group";
        command.Parameters.AddWithValue("@name", key.Name);
        command.Parameters.AddWithValue("@group", key.Group);

        object? value = await command.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }

    private static async Task WaitFor(Func<bool> condition, string what)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + observationDeadline;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail($"Timed out after {observationDeadline.TotalSeconds:F0} s waiting for {what}.");
    }

    private ServiceProvider BuildContainer()
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = "continuations";
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
    /// Records that it ran, and fails on demand so that a test can choose the outcome the store is
    /// told about without naming it.
    /// </summary>
    public sealed class OutcomeJob : IJob
    {
        public const string FailKey = "fail";

        private static readonly HashSet<string> ran = [];

        public static void Reset()
        {
            lock (ran)
            {
                ran.Clear();
            }
        }

        public static bool Ran(string jobName)
        {
            lock (ran)
            {
                return ran.Contains(jobName);
            }
        }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            lock (ran)
            {
                ran.Add(context.JobDetail.Key.Name);
            }

            if (context.MergedJobDataMap.ContainsKey(FailKey) && context.MergedJobDataMap.GetBoolean(FailKey))
            {
                throw new InvalidOperationException("the parent was asked to fail");
            }

            return default;
        }
    }
}
