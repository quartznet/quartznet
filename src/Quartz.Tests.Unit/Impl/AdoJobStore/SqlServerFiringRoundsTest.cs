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

using System.Data;
using System.Data.Common;

using FakeItEasy;

using Microsoft.Extensions.Logging;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;
using Quartz.Tests.Unit.Plugin.History;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// On SQL Server a firing round may take no lock only while the database has <c>READ_COMMITTED_SNAPSHOT</c>
/// on (#4117): the store asks once at startup, and a database that does not — or cannot be asked — keeps
/// 4.5's rounds and says so once, naming the fix. A SQL Server round that takes no lock fires apart.
/// </summary>
/// <remarks>
/// Without the snapshot SQL Server's read committed takes shared locks: a round's read waits on, and
/// deadlocks with, another node's claim.
/// </remarks>
public sealed class SqlServerFiringRoundsTest
{
    /// <summary>Log event <c>AdoJobStoreLog.LockFreeFiringRounds</c>.</summary>
    private const int LockFreeFiringRoundsEvent = 3056;

    [Test]
    public async Task ADatabaseWithReadCommittedSnapshotLeavesTheRoundsToDecide()
    {
        ProbingStore store = Store(new ScriptedSqlServerDelegate(() => true));

        await store.ProbeReadCommittedSnapshot();

        store.StaticRoundLockReason.Should().BeNull("with the snapshot on, a round's read takes no shared lock");
    }

    [Test]
    public async Task ADatabaseWithoutItLocksEveryRoundAndSaysWhy()
    {
        ProbingStore store = Store(new ScriptedSqlServerDelegate(() => false));

        await store.ProbeReadCommittedSnapshot();

        store.StaticRoundLockReason.Should().Be(RoundLockReason.ReadCommittedSnapshotOff);
        RoundLockReason.ReadCommittedSnapshotOff.ToTag().Should().Be("read_committed_snapshot_off",
            "the reason tag on the acquisition instruments is how an operator sees why the rounds are locked");
        RoundLockReason.ReadCommittedSnapshotOff.Describe().Should().Contain("SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE",
            "the startup line names the fix, not only the reason");
    }

    [Test]
    public async Task AProbeThatFailsCountsAsOffAndTheStartupLineSaysWhy()
    {
        using RecordingLoggerProvider logs = new();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        ProbingStore store = Store(new ScriptedSqlServerDelegate(() => throw new InvalidOperationException("VIEW DEFINITION permission denied")), loggerFactory);

        await store.ProbeReadCommittedSnapshot();
        store.LogLockFreeFiringRounds();

        store.StaticRoundLockReason.Should().Be(RoundLockReason.ReadCommittedSnapshotOff,
            "a database the store cannot ask is one it cannot show allows a round without the lock");
        logs.Entries.Should().ContainSingle(x => x.EventId.Id == LockFreeFiringRoundsEvent)
            .Which.Should().Match<LogEntry>(x => x.Level == LogLevel.Information
                                                 && x.Message.Contains("off")
                                                 && x.Message.Contains("READ_COMMITTED_SNAPSHOT ON")
                                                 && x.Message.Contains("permission denied"),
                "one line at startup, naming the reason, the fix and why the database could not be asked");
    }

    [Test]
    public void ADatabaseNotAskedYetKeepsEveryRoundLocked()
    {
        ProbingStore store = Store(new ScriptedSqlServerDelegate(() => true));

        store.StaticRoundLockReason.Should().Be(RoundLockReason.ReadCommittedSnapshotOff,
            "until the startup probe has answered, nothing says a round may go without the lock");
    }

    [TestCase("option_off")]
    [TestCase("isolation_level")]
    [TestCase("delegate")]
    public async Task WhatLocksEveryRoundAnywayIsNotBehindAProbe(string reason)
    {
        int asked = 0;
        bool supportsFireOnAcquire = reason != "delegate";
        ProbingStore store = Store(new ScriptedSqlServerDelegate(() =>
        {
            asked++;
            return true;
        }, supportsFireOnAcquire), configure: options =>
        {
            if (reason == "option_off")
            {
                options.LockFreeFiringRounds = false;
            }
            else if (reason == "isolation_level")
            {
                options.TransactionIsolationLevel = IsolationLevel.Serializable;
            }
        });

        await store.ProbeReadCommittedSnapshot();

        asked.Should().Be(0, "the answer could change nothing");
        store.StaticRoundLockReason!.Value.ToTag().Should().Be(reason, "the configured reason comes first in the rule");
    }

    [Test]
    public async Task ADialectThatReadsCommittedVersionsIsNotAsked()
    {
        ProbingStore store = Store(new PostgreSQLDelegate());

        await store.ProbeReadCommittedSnapshot();

        store.StaticRoundLockReason.Should().BeNull("PostgreSQL's read never takes a shared lock");
    }

    [TestCase(true, true)]
    [TestCase(false, false)]
    [TestCase(null, false)]
    public async Task TheDelegateReadsTheDatabasesSetting(bool? column, bool expected)
    {
        RecordingSqlServerDelegate sqlServer = new(column);

        bool readsCommittedSnapshot = await sqlServer.SelectReadsCommittedSnapshot(new ConnectionAndTransactionHolder(new StubBatchingConnection(), transaction: null));

        readsCommittedSnapshot.Should().Be(expected, "only a database that says it is on is on");
        sqlServer.Statements.Should().Equal(["SELECT is_read_committed_snapshot_on FROM sys.databases WHERE database_id = DB_ID()"]);
    }

    [Test]
    public async Task TheStartupLineSaysARoundWithoutTheLockFiresApart()
    {
        using RecordingLoggerProvider logs = new();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        ProbingStore store = Store(new ScriptedSqlServerDelegate(() => true), loggerFactory);

        await store.ProbeReadCommittedSnapshot();
        store.LogLockFreeFiringRounds();

        logs.Entries.Should().ContainSingle(x => x.EventId.Id == LockFreeFiringRoundsEvent)
            .Which.Message.Should().Contain("on").And.Contain("fires in a second",
                "an operator reading the line sees that SQL Server's rounds are two transactions");
    }

    [Test]
    public void OnlySqlServerFiresLockFreeRoundsApart()
    {
        Store(new SqlServerDelegate()).FiresLockFreeRoundsApart.Should().BeTrue();

        foreach (StdAdoDelegate other in (StdAdoDelegate[]) [new PostgreSQLDelegate(), new MySQLDelegate(), new OracleDelegate(), new FirebirdDelegate(), new SQLiteDelegate(), new StdAdoDelegate()])
        {
            Store(other).FiresLockFreeRoundsApart.Should().BeFalse("{0} keeps the folded round, which PostgreSQL measured fast", other.GetType().Name);
        }
    }

    [Test]
    public void SqlServerNeedsFourSevenPeersAndTheOthersFourSix()
    {
        new SqlServerDelegate().LockFreeRoundsSince.Should().Be(new Version(4, 7), "a 4.6 node locks every SQL Server round");
        new PostgreSQLDelegate().LockFreeRoundsSince.Should().Be(new Version(4, 6));
    }

    /// <summary>
    /// A round of one on a database without the snapshot is 4.5's even when it counts against a
    /// cluster-scoped limit, as with the option off; any other reason keeps such a round folded, so the limit
    /// stays exact.
    /// </summary>
    [TestCase("read_committed_snapshot_off", true)]
    [TestCase("option_off", true)]
    [TestCase("older_peer", false)]
    [TestCase("cluster_limit", false)]
    public void ARoundOfOneCountingAgainstAClusterScopedLimitIs45sOnlyWhenTheRoundsAre(string tag, bool asIn45)
    {
        RoundLockReason reason = Enum.GetValues<RoundLockReason>().Single(x => x.ToTag() == tag);
        AdoJobStoreBaseTest.TestAdoJobStoreBase store = new() { DirectDelegate = new SqlServerDelegate() };
        TriggerAcquisitionRequest request = new()
        {
            NoLaterThan = DateTimeOffset.UtcNow,
            MaxCount = 1,
            ExecutionLimits = ExecutionLimitsBuilder.Create().ForGroup("batch", 2, ExecutionLimitScope.Cluster).Build(),
        };

        store.FiresRoundOfOneAsIn45(request, reason).Should().Be(asIn45);
    }

    [Test]
    public void ARoundOfMoreThanOneStaysFolded()
    {
        AdoJobStoreBaseTest.TestAdoJobStoreBase store = new() { DirectDelegate = new SqlServerDelegate() };

        store.FiresRoundOfOneAsIn45(new TriggerAcquisitionRequest { NoLaterThan = DateTimeOffset.UtcNow, MaxCount = 2 }, RoundLockReason.ReadCommittedSnapshotOff)
            .Should().BeFalse("4.5 fired a batch in one locked transaction too");
    }

    private static ProbingStore Store(StdAdoDelegate driverDelegate, ILoggerFactory loggerFactory = null, Action<AdoJobStoreOptions> configure = null)
    {
        return new ProbingStore(loggerFactory, configure) { DirectDelegate = driverDelegate };
    }

    /// <summary>
    /// The test store, running what it is asked to run without a lock on a connection that goes nowhere.
    /// </summary>
    private sealed class ProbingStore(ILoggerFactory loggerFactory, Action<AdoJobStoreOptions> configure)
        : AdoJobStoreBaseTest.TestAdoJobStoreBase(clustered: false, timeProvider: null, loggerFactory, configure)
    {
        protected override ValueTask<T> ExecuteInLock<T>(
            SchedulerLock? lockKind,
            Func<ConnectionAndTransactionHolder, ValueTask<T>> txCallback,
            CancellationToken cancellationToken = default)
        {
            return txCallback(new ConnectionAndTransactionHolder(new StubBatchingConnection(), transaction: null));
        }
    }

    /// <summary>The SQL Server delegate, answering the probe as the test says.</summary>
    private sealed class ScriptedSqlServerDelegate(Func<bool> answer, bool supportsFireOnAcquire = true) : SqlServerDelegate
    {
        /// <summary>Takes part in the rounds as the shipped delegate does, unless the test says otherwise.</summary>
        public override bool SupportsFireOnAcquire => supportsFireOnAcquire;

        internal override ValueTask<bool> SelectReadsCommittedSnapshot(ConnectionAndTransactionHolder conn, CancellationToken cancellationToken = default)
        {
            return new ValueTask<bool>(answer());
        }
    }

    /// <summary>The shipped SQL Server delegate over a command that answers the column as the test says.</summary>
    private sealed class RecordingSqlServerDelegate(bool? column) : SqlServerDelegate
    {
        public List<string> Statements { get; } = [];

        public override DbCommand PrepareCommand(ConnectionAndTransactionHolder cth, string commandText)
        {
            Statements.Add(commandText);
            DbCommand cmd = A.Fake<DbCommand>();
            A.CallTo(() => cmd.ExecuteScalarAsync(A<CancellationToken>._)).Returns(Task.FromResult<object>(column is { } value ? value : DBNull.Value));
            return cmd;
        }
    }
}
