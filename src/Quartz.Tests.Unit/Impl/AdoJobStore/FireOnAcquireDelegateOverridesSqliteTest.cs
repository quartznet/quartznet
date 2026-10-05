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

using System.Data.Common;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// A delegate of somebody's own keeps every member it overrides called when the store fires what is due
/// as it acquires it (#3864). The round is one transaction, but a delegate that does not say it supports
/// the round's own members gets the single-trigger ones, exactly as acquiring and then firing called them:
/// each trigger reserved through <see cref="IDriverDelegate.InsertFiredTrigger" />, its header and job read
/// on their own, and its fire written by <see cref="IDriverDelegate.ApplyTriggerFired" /> as an update of
/// that reservation.
/// </summary>
/// <remarks>
/// SQLite, which acquires within the lock, so every round of more than one trigger takes the combined
/// round; each test builds its node around the delegate it is about.
/// </remarks>
[NonParallelizable]
public sealed class FireOnAcquireDelegateOverridesSqliteTest
{
    private const string Group = "fire-on-acquire-overrides";
    private static readonly JobKey jobKey = new("job", Group);

    private SqliteTestDatabase database = null!;
    private ServiceProvider? node;

    [SetUp]
    public void CreateDatabase()
    {
        database = new SqliteTestDatabase("fire-on-acquire-overrides");
        OwnFireWriteDelegate.Reset();
        OwnFiredRowDelegate.Reset();
    }

    [TearDown]
    public async Task DisposeNode()
    {
        if (node is not null)
        {
            await node.DisposeAsync();
        }

        database.Dispose();
    }

    /// <summary>
    /// A delegate whose <see cref="IDriverDelegate.ApplyTriggerFired" /> writes the fire itself, without
    /// the base implementation, updates the fired-trigger row its reservation wrote. Given a fire that
    /// expects to insert that row instead, its update would find nothing, and the job would run with no row
    /// to say it is running.
    /// </summary>
    [Test]
    public async Task AnApplyTriggerFiredOverrideThatWritesTheFireItselfFindsTheReservationItUpdates()
    {
        IJobStore store = await BuildNode<OwnFireWriteDelegate>();
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        await Schedule(store, "due-1", now);
        await Schedule(store, "due-2", now);

        // Storing a trigger reads its job; only the round's reads are counted.
        OwnFireWriteDelegate.Reset();

        TriggerAcquisitionResult round = await store.AcquireNextTriggersAndFireDue(RequestFor(maxCount: 2));

        round.Due.Select(x => x.Key.Name).Should().BeEquivalentTo(["due-1", "due-2"]);
        round.Fired.Should().OnlyContain(x => x.TriggerFiredBundle != null);
        OwnFireWriteDelegate.FireWrites.Should().Be(2, "the override writes every fire");
        (await FiredRows()).Should().BeEquivalentTo(
            [("due-1", "EXECUTING", "job"), ("due-2", "EXECUTING", "job")],
            "each running firing has its row, which the override's update moved from the reservation acquisition wrote");
        OwnFireWriteDelegate.HeaderReads.Should().Be(2, "each fire reads its own header through the member the delegate overrides");
        OwnFireWriteDelegate.JobReads.Should().Be(2, "and its own job");
    }

    /// <summary>
    /// A delegate whose <see cref="IDriverDelegate.InsertFiredTrigger" /> writes something of its own beside
    /// the row has it called for every trigger the round reserves, the ones it fires included.
    /// </summary>
    [Test]
    public async Task AnInsertFiredTriggerOverrideIsCalledForEveryTriggerTheRoundTakes()
    {
        IJobStore store = await BuildNode<OwnFiredRowDelegate>();
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        await Schedule(store, "due-1", now);
        await Schedule(store, "due-2", now);

        TriggerAcquisitionResult round = await store.AcquireNextTriggersAndFireDue(RequestFor(maxCount: 2));

        round.Fired.Should().HaveCount(2).And.OnlyContain(x => x.TriggerFiredBundle != null);
        OwnFiredRowDelegate.Inserted.Should().BeEquivalentTo(["due-1", "due-2"], "every fired-trigger row goes through the override");
        (await FiredRows()).Should().BeEquivalentTo([("due-1", "EXECUTING", "job"), ("due-2", "EXECUTING", "job")]);
    }

    private async Task<IJobStore> BuildNode<TDelegate>() where TDelegate : class, IDriverDelegate
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = "fire-on-acquire-overrides";
                options.InstanceId = "node";
                options.MaxBatchSize = 5;
            });

            q.UsePersistentStore(persistent =>
            {
                // Before UseSqlite, which registers the delegate it names: the registrations are try-add.
                persistent.UseDriverDelegate<TDelegate>();
                persistent.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                persistent.ProvisionSchema();
            });
        });

        node = services.BuildServiceProvider();
        IScheduler scheduler = await node.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.AddJob(JobBuilder.Create<NoOpJob>().WithIdentity(jobKey).StoreDurably().Build());
        return node.GetRequiredService<IJobStore>();
    }

    private static async Task Schedule(IJobStore store, string name, DateTimeOffset at)
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(name, Group)
            .ForJob(jobKey)
            .StartAt(at)
            .Build();
        trigger.ComputeFirstFireTimeUtc(null);
        await store.AddTrigger(trigger);
    }

    private static TriggerAcquisitionRequest RequestFor(int maxCount)
    {
        return new TriggerAcquisitionRequest
        {
            NoLaterThan = TimeProvider.System.GetUtcNow().AddMinutes(5),
            MaxCount = maxCount,
            TimeWindow = TimeSpan.FromSeconds(5),
        };
    }

    private async Task<List<(string Trigger, string State, string? Job)>> FiredRows()
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT TRIGGER_NAME, STATE, JOB_NAME FROM QRTZ_FIRED_TRIGGERS";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        List<(string, string, string?)> rows = [];
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        return rows;
    }

    /// <summary>
    /// A delegate that writes a fire itself: the fired-trigger row moved to <c>EXECUTING</c> naming the job,
    /// as 4.3's statement moved it, and the trigger row through <see cref="StdAdoDelegate.UpdateTrigger" />.
    /// It never calls the base implementation, so it knows nothing of a row the fire is to insert.
    /// </summary>
    public sealed class OwnFireWriteDelegate : SQLiteDelegate
    {
        private static int fireWrites;
        private static int headerReads;
        private static int jobReads;

        public static int FireWrites => Volatile.Read(ref fireWrites);

        public static int HeaderReads => Volatile.Read(ref headerReads);

        public static int JobReads => Volatile.Read(ref jobReads);

        public static void Reset()
        {
            Volatile.Write(ref fireWrites, 0);
            Volatile.Write(ref headerReads, 0);
            Volatile.Write(ref jobReads, 0);
        }

        public override async ValueTask ApplyTriggerFired(ConnectionAndTransactionHolder conn, TriggerFiredUpdate update, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref fireWrites);

            using DbCommand command = conn.Connection.CreateCommand();
            conn.Attach(command);
            command.CommandText = "UPDATE QRTZ_FIRED_TRIGGERS SET STATE = 'EXECUTING', JOB_NAME = @jobName, JOB_GROUP = @jobGroup WHERE ENTRY_ID = @entryId";
            AddParameter(command, "@jobName", update.JobDetail.Key.Name);
            AddParameter(command, "@jobGroup", update.JobDetail.Key.Group);
            AddParameter(command, "@entryId", update.Trigger.FireInstanceId!);
            await command.ExecuteNonQueryAsync(cancellationToken);

            await UpdateTrigger(conn, update.Trigger, update.NewState, update.JobDetail, cancellationToken);
        }

        public override ValueTask<StoredTriggerHeader?> SelectTriggerHeader(ConnectionAndTransactionHolder conn, TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref headerReads);
            return base.SelectTriggerHeader(conn, triggerKey, cancellationToken);
        }

        public override ValueTask<IJobDetail?> SelectJobDetail(ConnectionAndTransactionHolder conn, JobKey jobKey, ITypeLoader typeLoader, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref jobReads);
            return base.SelectJobDetail(conn, jobKey, typeLoader, cancellationToken);
        }

        private static void AddParameter(DbCommand command, string name, object value)
        {
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }
    }

    /// <summary>
    /// A delegate that records every fired-trigger row it is asked to insert, and inserts it as the base does.
    /// </summary>
    public sealed class OwnFiredRowDelegate : SQLiteDelegate
    {
        private static readonly List<string> inserted = [];

        public static List<string> Inserted
        {
            get
            {
                lock (inserted)
                {
                    return [.. inserted];
                }
            }
        }

        public static void Reset()
        {
            lock (inserted)
            {
                inserted.Clear();
            }
        }

        public override ValueTask<int> InsertFiredTrigger(ConnectionAndTransactionHolder conn, IOperableTrigger trigger, StoredTriggerState state, IJobDetail? job, CancellationToken cancellationToken = default)
        {
            lock (inserted)
            {
                inserted.Add(trigger.Key.Name);
            }

            return base.InsertFiredTrigger(conn, trigger, state, job, cancellationToken);
        }
    }

    public sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
