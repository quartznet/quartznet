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
/// The statements of a write-behind flush where the connection batches: the rows, and the guarded status
/// updates, each as few round trips as a round's batches allow.
/// </summary>
/// <remarks>
/// SQLite, the one database the unit tests write to, cannot batch, so the batched arm is asserted against
/// the shared stub here, beside the arm that issues one command each.
/// </remarks>
public class StdAdoDelegateHistoryBatchTest
{
    [Test]
    public async Task AFlushsRowsGoOutInOneBatch()
    {
        StubBatchingConnection connection = new();
        List<StubDbCommand> issued = [];
        StdAdoDelegate del = Shipped(issued);

        await del.InsertExecutionHistories(new ConnectionAndTransactionHolder(connection, null), [Entry("e1"), Entry("e2"), Entry("e3")]);

        connection.Batches.Should().ContainSingle("a flush's rows have no reason to take a round trip each");
        connection.Batches[0].Commands.Should().HaveCount(3).And.OnlyContain(
            command => command.CommandText.StartsWith("INSERT INTO QRTZ_EXECUTION_HISTORY", StringComparison.Ordinal));
        issued.Should().BeEmpty("nothing is issued as a standalone command");

        await del.InsertExecutionHistory(new ConnectionAndTransactionHolder(new StubBatchingConnection(), null), "e1", Entry("e1"));
        connection.Batches[0].Commands[0].CommandText.Should().Be(issued.Single().CommandText,
            "a row in a batch and a row written alone come from the one description");
        connection.Batches[0].Commands[0].Parameters.Count.Should().Be(issued.Single().Parameters.Count);
    }

    [Test]
    public async Task RowsTooLargeToShareABatchGoOutInSeveral()
    {
        StubBatchingConnection connection = new();
        StdAdoDelegate del = Shipped([]);
        string log = new('x', 8 * 1024);

        await del.InsertExecutionHistories(
            new ConnectionAndTransactionHolder(connection, null),
            [Entry("e1") with { Log = log }, Entry("e2") with { Log = log }, Entry("e3") with { Log = log }]);

        connection.Batches.Should().HaveCount(3, "each row is over the size a round's batch is kept under")
            .And.OnlyContain(batch => batch.Commands.Count == 1);
    }

    [Test]
    public async Task AProviderThatCannotBatchInsertsTheRowsOneAtATime()
    {
        StubBatchingConnection connection = new() { SupportsBatching = false };
        List<StubDbCommand> issued = [];
        StdAdoDelegate del = Shipped(issued);

        await del.InsertExecutionHistories(new ConnectionAndTransactionHolder(connection, null), [Entry("e1"), Entry("e2")]);

        connection.Batches.Should().BeEmpty();
        issued.Should().HaveCount(2).And.OnlyContain(
            command => command.CommandText.StartsWith("INSERT INTO QRTZ_EXECUTION_HISTORY", StringComparison.Ordinal));
    }

    [Test]
    public async Task TheGuardedStatusUpdatesGoOutInOneBatchAndAnswerHowManyMatched()
    {
        StubBatchingConnection connection = new() { RecordsAffected = static index => index == 0 ? 1 : 0 };
        StdAdoDelegate del = Shipped([]);

        int matched = await del.UpdateJobRunStatusesIfUnchanged(
            new ConnectionAndTransactionHolder(connection, null),
            [(Status("busy", runCount: 12), 10), (Status("quiet", runCount: 3), 2)]);

        matched.Should().Be(1, "the second row's RUN_COUNT had moved, so its update matched nothing");
        StubBatch batch = connection.Batches.Should().ContainSingle().Subject;
        batch.Commands.Should().HaveCount(2).And.OnlyContain(command =>
            command.CommandText.StartsWith("UPDATE QRTZ_JOB_STATUS SET", StringComparison.Ordinal)
            && command.CommandText.EndsWith("RUN_COUNT = @statusReadRunCount", StringComparison.Ordinal));

        DbParameter[] parameters = batch.Commands[0].Parameters.Cast<DbParameter>().ToArray();
        parameters[^1].ParameterName.Should().Be("@statusReadRunCount", "the guard is the statement's last placeholder");
        parameters[^1].Value.Should().Be(10L, "the guard binds the run count the flush read, not the one it writes");
        parameters.Single(parameter => parameter.ParameterName == "@statusRunCount").Value.Should().Be(12L);
    }

    [Test]
    public async Task OneGuardedStatusUpdateIsIssuedAsACommand()
    {
        StubBatchingConnection connection = new();
        List<StubDbCommand> issued = [];
        StdAdoDelegate del = Shipped(issued);

        await del.UpdateJobRunStatusesIfUnchanged(new ConnectionAndTransactionHolder(connection, null), [(Status("busy", runCount: 12), 10)]);

        connection.Batches.Should().BeEmpty("a batch of one is one round trip either way, and costs more to assemble");
        issued.Should().ContainSingle().Which.CommandText.Should().StartWith("UPDATE QRTZ_JOB_STATUS SET");
    }

    private static ExecutionHistoryEntry Entry(string entryId) => new(
        SchedulerName: "INSTANCE",
        SchedulerInstanceId: "node-a",
        JobGroup: "reports",
        JobName: "nightly",
        TriggerGroup: "reports",
        TriggerName: "nightly",
        FiredAtUtc: new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero),
        Duration: TimeSpan.FromMilliseconds(5),
        Succeeded: true,
        ExceptionMessage: null)
    {
        EntryId = entryId,
        Result = JobRunResult.Succeeded
    };

    private static JobRunStatus Status(string jobName, long runCount) =>
        new("INSTANCE", new JobKey(jobName, "reports"), new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero), JobRunResult.Succeeded)
        {
            FirstFiredAtUtc = new DateTimeOffset(2026, 10, 18, 11, 0, 0, TimeSpan.Zero),
            LastSchedulerInstanceId = "node-a",
            RunCount = runCount
        };

    private static StdAdoDelegate Shipped(List<StubDbCommand> issued)
    {
        IDbProvider dbProvider = A.Fake<IDbProvider>();
        A.CallTo(() => dbProvider.Metadata).Returns(new DbMetadata { ParameterNamePrefix = "@", BindByName = true });
        A.CallTo(() => dbProvider.CreateCommand()).ReturnsLazily(() =>
        {
            StubDbCommand command = new();
            issued.Add(command);
            return command;
        });

        StdAdoDelegate del = new();
        del.Initialize(new DriverDelegateContext
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

        return del;
    }
}
