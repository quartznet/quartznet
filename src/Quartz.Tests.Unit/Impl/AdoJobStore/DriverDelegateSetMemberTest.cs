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

using FakeItEasy;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// The statements <see cref="StdAdoDelegate" />'s set-shaped members send, and how many of them.
/// </summary>
/// <remarks>
/// These are only reachable against a database from the integration tests, which say what the answers
/// are. What this level says is what went over the wire to get them: one statement per chunk rather
/// than one per key, the key-set predicate spliced onto the shared prefix, and the parameters bound in
/// the order the statement names them — which is what a provider binding positionally depends on.
/// </remarks>
public class DriverDelegateSetMemberTest
{
    [Test]
    public async Task TheTriggerKeysOfASetOfJobsAreOneStatement()
    {
        RecordingDelegate del = RecordingDelegate.Create();

        await del.SelectTriggerKeysForJobs(del.Connection, [new JobKey("a", "jobs"), new JobKey("b", "jobs")]);

        del.Statements.Should().ContainSingle("two jobs are one key-set predicate, not two statements");
        del.Statements[0].Should().StartWith("SELECT TRIGGER_NAME, TRIGGER_GROUP FROM QRTZ_TRIGGERS");
        del.Statements[0].Should().Contain("@jkn000").And.Contain("@jkg001");
        del.ParametersOf(0).Should().StartWith(["@schedulerName", "@jkn000", "@jkg000", "@jkn001", "@jkg001"]);
    }

    [Test]
    public async Task AnEmptySetOfJobsIsNoStatementAtAll()
    {
        RecordingDelegate del = RecordingDelegate.Create();

        List<TriggerKey> keys = await del.SelectTriggerKeysForJobs(del.Connection, []);

        keys.Should().BeEmpty();
        del.Statements.Should().BeEmpty("there is nothing to ask about");
    }

    [Test]
    public async Task AKeySetStateTransitionIsOneUpdateNamingBothStates()
    {
        RecordingDelegate del = RecordingDelegate.Create();

        await del.UpdateTriggerStatesFromOtherStates(
            del.Connection,
            [new TriggerKey("a", "g"), new TriggerKey("b", "g")],
            StoredTriggerState.Paused,
            [StoredTriggerState.Waiting, StoredTriggerState.Acquired]);

        del.Statements.Should().ContainSingle();
        del.Statements[0].Should().StartWith("UPDATE QRTZ_TRIGGERS SET TRIGGER_STATE = @newState");
        del.Statements[0].Should().Contain("@oldState00").And.Contain("@oldState01").And.Contain("@tkn001");

        // The statement names them in this order, which is the only thing a provider that binds
        // positionally has to go on.
        del.ParametersOf(0).Should().StartWith(
            ["@newState", "@schedulerName", "@oldState00", "@oldState01", "@tkn000", "@tkg000", "@tkn001", "@tkg001"]);
    }

    /// <summary>
    /// The transition that names no keys at all — every trigger of this scheduler that is in one of the
    /// given states — whose two leading parameters were the pair still bound the wrong way round.
    /// </summary>
    [Test]
    public async Task AStoreWideStateTransitionBindsItsParametersInStatementOrder()
    {
        RecordingDelegate del = RecordingDelegate.Create();

        await del.UpdateTriggerStatesFromOtherStates(
            del.Connection,
            StoredTriggerState.Waiting,
            [StoredTriggerState.Acquired, StoredTriggerState.Blocked]);

        del.Statements.Should().ContainSingle();
        del.Statements[0].Should().StartWith(
            "UPDATE QRTZ_TRIGGERS SET TRIGGER_STATE = @newState WHERE SCHED_NAME = @schedulerName");

        del.ParametersOf(0).Should().Equal(
            ["@newState", "@schedulerName", "@oldState00", "@oldState01"],
            "the SET clause names @newState before the WHERE names @schedulerName, so a provider "
            + "configured to bind by position would otherwise write the scheduler's name into TRIGGER_STATE "
            + "and look for triggers of a scheduler called WAITING");
    }

    [Test]
    public async Task AnEmptyKeySetIsNoUpdateAtAll()
    {
        RecordingDelegate del = RecordingDelegate.Create();

        int updated = await del.UpdateTriggerStatesFromOtherStates(
            del.Connection, [], StoredTriggerState.Paused, [StoredTriggerState.Waiting]);

        updated.Should().Be(0);
        del.Statements.Should().BeEmpty();
    }

    /// <summary>
    /// The paused-job-group question is asked of the whole table and intersected here, so it costs one
    /// statement whatever it is asked about.
    /// </summary>
    [Test]
    public async Task AskingWhichJobGroupsArePausedIsOneStatement()
    {
        RecordingDelegate del = RecordingDelegate.Create();

        await del.SelectPausedJobGroups(del.Connection, ["reports", "billing", "nightly"]);

        del.Statements.Should().ContainSingle();
        del.Statements[0].Should().Be("SELECT JOB_GROUP FROM QRTZ_PAUSED_JOB_GRPS WHERE SCHED_NAME = @schedulerName");
    }

    [Test]
    public async Task PausingSeveralJobGroupsWritesThemInOneBatch()
    {
        RecordingDelegate del = RecordingDelegate.Create();

        await del.InsertPausedJobGroups(del.Connection, ["reports", "billing"]);

        del.Connection.Connection.Should().BeOfType<StubBatchingConnection>();
        ((StubBatchingConnection) del.Connection.Connection).Batches.Should().ContainSingle();
        ((StubBatchingConnection) del.Connection.Connection).Batches[0].Commands.Should().HaveCount(2);
        del.Statements.Should().BeEmpty("the rows travel as batch commands rather than as prepared statements");
    }

    [Test]
    public async Task PausingNoJobGroupsWritesNothing()
    {
        RecordingDelegate del = RecordingDelegate.Create();

        await del.InsertPausedJobGroups(del.Connection, []);

        ((StubBatchingConnection) del.Connection.Connection).Batches.Should().BeEmpty();
        del.Statements.Should().BeEmpty();
    }

    [Test]
    public async Task AnEmptyKeySetIsNoHeaderReadAtAll()
    {
        RecordingDelegate del = RecordingDelegate.Create();

        List<StoredTriggerHeader> headers = await del.SelectStoredTriggerHeaders(del.Connection, []);

        headers.Should().BeEmpty();
        del.Statements.Should().BeEmpty();
    }

    /// <summary>
    /// A delegate that answers each read with an empty reader and remembers what it was asked to
    /// prepare, which is how a statement built inside <see cref="StdAdoDelegate" /> is measured without
    /// a database.
    /// </summary>
    private sealed class RecordingDelegate : StdAdoDelegate
    {
        private readonly List<ReaderStubCommand> commands = [];

        public static RecordingDelegate Create()
        {
            IDbProvider dbProvider = A.Fake<IDbProvider>();
            A.CallTo(() => dbProvider.Metadata).Returns(new DbMetadata { ParameterNamePrefix = "@", BindByName = true });
            A.CallTo(() => dbProvider.CreateCommand()).ReturnsLazily(() => new StubDbCommand());

            RecordingDelegate del = new();
            del.Initialize(new DriverDelegateContext
            {
                TablePrefix = "QRTZ_",
                InstanceId = "TESTSCHED",
                SchedulerName = "INSTANCE",
                TypeLoader = new SimpleTypeLoader(),
                UseProperties = false,
                DbProvider = dbProvider,
                ObjectSerializer = A.Fake<IObjectSerializer>(),
                TimeProvider = TimeProvider.System
            });

            return del;
        }

        public ConnectionAndTransactionHolder Connection { get; } = new(new StubBatchingConnection(), transaction: null);

        public List<string> Statements { get; } = [];

        /// <summary>The parameter names bound to one statement, in the order they were bound.</summary>
        public IReadOnlyList<string> ParametersOf(int statement) =>
            [.. commands[statement].Parameters.Cast<DbParameter>().Select(parameter => parameter.ParameterName)];

        public override DbCommand PrepareCommand(ConnectionAndTransactionHolder cth, string commandText)
        {
            Statements.Add(commandText);

            ReaderStubCommand cmd = new(ProjectionDataReader.Empty) { CommandText = commandText };
            commands.Add(cmd);
            cth.Attach(cmd);
            return cmd;
        }
    }
}
