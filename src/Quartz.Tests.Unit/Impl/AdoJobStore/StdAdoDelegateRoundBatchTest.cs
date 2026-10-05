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

using System.Data.Common;
using System.Text;

using FakeItEasy;

using Microsoft.Data.Sqlite;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// How a fire-on-acquire round's claims and fire writes reach the database (#3864): one
/// <see cref="DbBatch" /> each, below eight kilobytes, from a delegate Quartz ships on a connection that
/// batches; one statement per trigger from anything else, so that no override is ever bypassed.
/// </summary>
/// <remarks>
/// Against the in-memory provider the batching tests share, because every provider that batches needs a
/// live server, and the branches here — a claim another node took, counts that do not add up, a batch
/// that fails — need one that can be told what to answer.
/// </remarks>
public class StdAdoDelegateRoundBatchTest
{
    private static readonly DateTimeOffset FireTime = new(2031, 6, 17, 10, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Three claims go as one batch of the single-trigger claim's statement, and a total of three says
    /// every one took.
    /// </summary>
    [Test]
    public async Task ClaimsGoAsOneBatchOfTheSingleTriggerStatement()
    {
        PostgreSQLDelegate driverDelegate = Shipped(new PostgreSQLDelegate());
        StubBatchingConnection connection = new();

        List<TriggerKey> moved = await driverDelegate.UpdateTriggerStatesFromOtherStateWithNextFireTime(
            Holder(connection), Claims(3), StoredTriggerState.Acquired, StoredTriggerState.Waiting);

        moved.Select(x => x.Name).Should().Equal(["t0", "t1", "t2"]);
        StubBatch batch = connection.Batches.Should().ContainSingle("three claims fit one batch").Subject;
        batch.Commands.Should().HaveCount(3).And.OnlyContain(x => x.CommandText.StartsWith("UPDATE QRTZ_TRIGGERS SET TRIGGER_STATE", StringComparison.Ordinal),
            "each is the single-trigger claim's own statement");
        batch.Commands[0].Parameters.Count.Should().Be(6);
    }

    /// <summary>
    /// A batch whose total is short of its claims says which took by each command's own count.
    /// </summary>
    [Test]
    public async Task AClaimAnotherNodeTookIsToldApartByItsOwnCount()
    {
        PostgreSQLDelegate driverDelegate = Shipped(new PostgreSQLDelegate());
        StubBatchingConnection connection = new() { RecordsAffected = index => index == 1 ? 0 : 1 };

        List<TriggerKey> moved = await driverDelegate.UpdateTriggerStatesFromOtherStateWithNextFireTime(
            Holder(connection), Claims(3), StoredTriggerState.Acquired, StoredTriggerState.Waiting);

        moved.Select(x => x.Name).Should().Equal(["t0", "t2"], "the claim whose command moved no row lost its trigger to another node");
    }

    /// <summary>
    /// Per-command counts that do not add up to the total are a provider that does not report them; the
    /// delegate throws rather than guess which claims took.
    /// </summary>
    [Test]
    public async Task CountsThatDoNotAddUpAreRefusedRatherThanGuessedAt()
    {
        PostgreSQLDelegate driverDelegate = Shipped(new PostgreSQLDelegate());
        StubBatchingConnection connection = new() { RecordsAffected = index => index == 0 ? 2 : 0 };

        Func<Task> claim = async () => await driverDelegate.UpdateTriggerStatesFromOtherStateWithNextFireTime(
            Holder(connection), Claims(3), StoredTriggerState.Acquired, StoredTriggerState.Waiting);

        await claim.Should().ThrowAsync<ClaimOutcomeUnknownException>();
    }

    /// <summary>
    /// A subclass may have overridden the single-trigger claim, and a batch would bypass it; it is called
    /// for each claim instead, and never batched.
    /// </summary>
    [Test]
    public async Task ASubclassClaimsOneTriggerAtATime()
    {
        CountingDelegate driverDelegate = CountingDelegate.Create();
        StubBatchingConnection connection = new();

        await driverDelegate.UpdateTriggerStatesFromOtherStateWithNextFireTime(
            Holder(connection), Claims(3), StoredTriggerState.Acquired, StoredTriggerState.Waiting);

        connection.Batches.Should().BeEmpty("a subclass's override of the single-trigger claim is never bypassed");
        driverDelegate.PreparedCommands.Should().HaveCount(3);
    }

    /// <summary>
    /// A connection that cannot batch gets the single-trigger claim for each, from a shipped delegate too.
    /// </summary>
    [Test]
    public async Task AConnectionThatCannotBatchClaimsOneTriggerAtATime()
    {
        PostgreSQLDelegate driverDelegate = Shipped(new PostgreSQLDelegate());
        StubBatchingConnection connection = new() { SupportsBatching = false };

        List<TriggerKey> moved = await driverDelegate.UpdateTriggerStatesFromOtherStateWithNextFireTime(
            Holder(connection), Claims(2), StoredTriggerState.Acquired, StoredTriggerState.Waiting);

        connection.Batches.Should().BeEmpty();
        moved.Should().BeEmpty("the stub's standalone commands report no row moved");
    }

    /// <summary>
    /// The fires of a round go as one batch of every statement each fire makes, in order: the fired row
    /// inserted, the trigger's row, its type table.
    /// </summary>
    [Test]
    public async Task FireWritesGoAsOneBatchInTheOrderOfTheFires()
    {
        PostgreSQLDelegate driverDelegate = Shipped(new PostgreSQLDelegate());
        StubBatchingConnection connection = new();

        await driverDelegate.ApplyTriggersFired(Holder(connection), Updates(3));

        StubBatch batch = connection.Batches.Should().ContainSingle("three fires fit one batch").Subject;
        batch.Commands.Should().HaveCount(9, "three statements a fire: the fired row, the trigger row, the simple trigger row");
        batch.Commands[0].CommandText.Should().StartWith("INSERT INTO QRTZ_FIRED_TRIGGERS", "a fire on acquisition inserts its row");
        batch.Commands[1].CommandText.Should().StartWith("UPDATE QRTZ_TRIGGERS");
        batch.Commands[2].CommandText.Should().StartWith("UPDATE QRTZ_SIMPLE_TRIGGERS");
        batch.Commands[3].CommandText.Should().StartWith("INSERT INTO QRTZ_FIRED_TRIGGERS", "then the next fire's");
    }

    /// <summary>
    /// A round larger than eight kilobytes of statements goes as several batches below it: a batch over
    /// the size waited some forty milliseconds on PostgreSQL for the server's delayed acknowledgement.
    /// </summary>
    [Test]
    public async Task ALargeRoundGoesAsSeveralBatchesBelowEightKilobytes()
    {
        PostgreSQLDelegate driverDelegate = Shipped(new PostgreSQLDelegate());
        StubBatchingConnection connection = new();

        await driverDelegate.ApplyTriggersFired(Holder(connection), Updates(10));

        connection.Batches.Should().HaveCountGreaterThan(1);
        connection.Batches.Sum(x => x.Commands.Count).Should().Be(30, "every statement still goes out, once");
        foreach (StubBatch batch in connection.Batches)
        {
            PayloadBytes(batch).Should().BeLessThan(8 * 1024);
        }
    }

    /// <summary>
    /// The size is counted in the bytes the driver sends, not in characters: three claims of triggers
    /// named in a script of three bytes a character fit one batch by their characters, and go as three by
    /// their bytes.
    /// </summary>
    [Test]
    public async Task ARoundIsSizedByTheUtf8BytesItSends()
    {
        PostgreSQLDelegate driverDelegate = Shipped(new PostgreSQLDelegate());
        StubBatchingConnection connection = new();
        string name = new('日', 1_500);
        List<TriggerClaim> claims = [.. Enumerable.Range(0, 3).Select(i => new TriggerClaim { TriggerKey = new TriggerKey(name + i, "g"), NextFireTimeUtc = FireTime })];

        List<TriggerKey> moved = await driverDelegate.UpdateTriggerStatesFromOtherStateWithNextFireTime(
            Holder(connection), claims, StoredTriggerState.Acquired, StoredTriggerState.Waiting);

        moved.Should().HaveCount(3);
        connection.Batches.Should().HaveCount(3,
            "each claim is some 4,700 bytes of UTF-8 though under 1,800 characters, and two of them would pass the size");
        foreach (StubBatch batch in connection.Batches)
        {
            PayloadBytes(batch).Should().BeLessThan(8 * 1024);
        }
    }

    /// <summary>
    /// A batch of fire writes fails as a unit, so which fire failed is not known, and the delegate says so;
    /// the store answers by firing the round one trigger at a time.
    /// </summary>
    [Test]
    public async Task AFailedWriteBatchSaysItCannotTellWhoseFireFailed()
    {
        PostgreSQLDelegate driverDelegate = Shipped(new PostgreSQLDelegate());
        StubBatchingConnection connection = new() { FailBatchExecution = true };

        Func<Task> apply = async () => await driverDelegate.ApplyTriggersFired(Holder(connection), Updates(2));

        (await apply.Should().ThrowAsync<TriggerWriteFailedException>()).Which.Index.Should().Be(-1);
        connection.Batches.Should().ContainSingle().Which.ExecuteCount.Should().Be(1, "a failed batch is not replayed one statement at a time");
    }

    /// <summary>
    /// A type-table write that follows the batch is issued on its own, so when it fails, whose fire it was
    /// is known and said: the store rolls back that fire alone rather than rerunning the round a trigger at
    /// a time to find it.
    /// </summary>
    [Test]
    public async Task AFailedTypeTableWriteAfterTheBatchSaysWhoseFireItWas()
    {
        PostgreSQLDelegate driverDelegate = Shipped(
            new PostgreSQLDelegate(),
            commandFailure: sql => sql.Contains("CRON_TRIGGERS", StringComparison.Ordinal) ? new InvalidOperationException("the old type's row cannot be deleted") : null);
        StubBatchingConnection connection = new();
        List<TriggerFiredUpdate> updates = Updates(3);

        // Stored as a cron trigger and firing as a simple one: its type-table write cannot be described as a
        // statement, so it is issued on its own after the batch.
        updates[1] = updates[1] with { StoredTriggerType = AdoConstants.TriggerTypeCron };

        Func<Task> apply = async () => await driverDelegate.ApplyTriggersFired(Holder(connection), updates);

        TriggerWriteFailedException failure = (await apply.Should().ThrowAsync<TriggerWriteFailedException>()).Which;
        failure.Index.Should().Be(1, "the write that failed is the second fire's own");
        failure.InnerException.Should().BeOfType<InvalidOperationException>();
        connection.Batches.Should().ContainSingle("the batch itself went out and succeeded");
    }

    /// <summary>
    /// A transient failure comes out as itself, for the store's transaction wrapper to retry the round.
    /// </summary>
    [Test]
    public async Task ATransientBatchFailureComesOutAsItself()
    {
        PostgreSQLDelegate driverDelegate = Shipped(new PostgreSQLDelegate());
        StubBatchingConnection connection = new() { BatchFailure = static () => new SqliteException("database is locked", 5) };

        Func<Task> apply = async () => await driverDelegate.ApplyTriggersFired(Holder(connection), Updates(2));

        await apply.Should().ThrowAsync<SqliteException>();
    }

    /// <summary>
    /// A delegate that applies one fire at a time — the interface's default, and a subclass of
    /// <c>StdAdoDelegate</c> — says which fire failed, so that the store rolls back that fire alone.
    /// </summary>
    [Test]
    public async Task OneFireAtATimeSaysWhichFireFailed()
    {
        IDriverDelegate driverDelegate = A.Fake<IDriverDelegate>();
        A.CallTo(() => driverDelegate.ApplyTriggersFired(A<ConnectionAndTransactionHolder>._, A<IReadOnlyList<TriggerFiredUpdate>>._, A<CancellationToken>._))
            .CallsBaseMethod();
        A.CallTo(() => driverDelegate.ApplyTriggerFired(A<ConnectionAndTransactionHolder>._, A<TriggerFiredUpdate>.That.Matches(x => x.Trigger.Key.Name == "t1"), A<CancellationToken>._))
            .Throws(new InvalidOperationException("a column the fire writes is gone"));

        Func<Task> apply = async () => await driverDelegate.ApplyTriggersFired(Holder(new StubBatchingConnection()), Updates(3));

        TriggerWriteFailedException failure = (await apply.Should().ThrowAsync<TriggerWriteFailedException>()).Which;
        failure.Index.Should().Be(1);
        failure.InnerException.Should().BeOfType<InvalidOperationException>();
        A.CallTo(() => driverDelegate.ApplyTriggerFired(A<ConnectionAndTransactionHolder>._, A<TriggerFiredUpdate>.That.Matches(x => x.Trigger.Key.Name == "t2"), A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    /// <summary>
    /// The interface's default claim is the single-trigger claim for each, in order, and answers the ones
    /// that moved a row.
    /// </summary>
    [Test]
    public async Task TheDefaultClaimIsTheSingleTriggerClaimForEach()
    {
        IDriverDelegate driverDelegate = A.Fake<IDriverDelegate>();
        A.CallTo(() => driverDelegate.UpdateTriggerStatesFromOtherStateWithNextFireTime(
                A<ConnectionAndTransactionHolder>._, A<IReadOnlyList<TriggerClaim>>._, A<StoredTriggerState>._, A<StoredTriggerState>._, A<CancellationToken>._))
            .CallsBaseMethod();
        A.CallTo(() => driverDelegate.UpdateTriggerStateFromOtherStateWithNextFireTime(
                A<ConnectionAndTransactionHolder>._, A<TriggerKey>._, StoredTriggerState.Acquired, StoredTriggerState.Waiting, FireTime, A<CancellationToken>._))
            .ReturnsLazily((ConnectionAndTransactionHolder _, TriggerKey key, StoredTriggerState _, StoredTriggerState _, DateTimeOffset _, CancellationToken _) =>
                new ValueTask<int>(key.Name == "t1" ? 0 : 1));

        List<TriggerKey> moved = await driverDelegate.UpdateTriggerStatesFromOtherStateWithNextFireTime(
            Holder(new StubBatchingConnection()), Claims(3), StoredTriggerState.Acquired, StoredTriggerState.Waiting);

        moved.Select(x => x.Name).Should().Equal(["t0", "t2"]);
    }

    private static T Shipped<T>(T driverDelegate, Func<string, Exception> commandFailure = null) where T : StdAdoDelegate
    {
        IDbProvider dbProvider = A.Fake<IDbProvider>();
        A.CallTo(() => dbProvider.Metadata).Returns(new DbMetadata { ParameterNamePrefix = "@", BindByName = true });
        A.CallTo(() => dbProvider.CreateCommand()).ReturnsLazily(() => new StubDbCommand { Failure = commandFailure });

        driverDelegate.Initialize(new DriverDelegateContext
        {
            TablePrefix = "QRTZ_",
            InstanceId = "TESTSCHED",
            SchedulerName = "INSTANCE",
            TypeLoader = new SimpleTypeLoader(),
            UseProperties = false,
            DbProvider = dbProvider,
            ObjectSerializer = A.Fake<IObjectSerializer>(),
            TimeProvider = TimeProvider.System,
        });

        return driverDelegate;
    }

    private static ConnectionAndTransactionHolder Holder(DbConnection connection) => new(connection, null);

    /// <summary>
    /// What a batch carries in UTF-8: its statements' text and its values.
    /// </summary>
    private static int PayloadBytes(StubBatch batch)
    {
        int bytes = 0;
        foreach (StubBatchCommand command in batch.Commands)
        {
            bytes += Encoding.UTF8.GetByteCount(command.CommandText);
            foreach (DbParameter parameter in command.Parameters)
            {
                bytes += parameter.Value switch
                {
                    string text => Encoding.UTF8.GetByteCount(text),
                    byte[] data => data.Length,
                    _ => 8,
                };
            }
        }

        return bytes;
    }

    private static List<TriggerClaim> Claims(int count)
    {
        return [.. Enumerable.Range(0, count).Select(i => new TriggerClaim { TriggerKey = new TriggerKey("t" + i, "g"), NextFireTimeUtc = FireTime })];
    }

    private static List<TriggerFiredUpdate> Updates(int count)
    {
        IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity("job", "g").StoreDurably().Build();

        List<TriggerFiredUpdate> updates = new(count);
        for (int i = 0; i < count; i++)
        {
            IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
                .WithIdentity("t" + i, "g")
                .ForJob(job)
                .StartAt(FireTime)
                .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(1)).RepeatForever())
                .Build();
            trigger.FireInstanceId = "fire-" + i;
            trigger.Triggered(null);

            updates.Add(new TriggerFiredUpdate
            {
                Trigger = trigger,
                JobDetail = job,
                NewState = StoredTriggerState.Waiting,
                StoredTriggerType = AdoConstants.TriggerTypeSimple,
                ScheduledFireTimeUtc = FireTime,
                ClearMisfireOriginalFireTime = false,
                BlockJobTriggers = false,
                FiredOnAcquire = true,
            });
        }

        return updates;
    }

    private sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
