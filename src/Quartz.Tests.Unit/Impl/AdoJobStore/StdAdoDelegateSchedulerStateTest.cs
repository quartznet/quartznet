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

using FakeItEasy;

using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// The check-in table's reads, and specifically the order their parameters are bound in.
/// </summary>
/// <remarks>
/// Every driver Quartz ships a description for binds by name, so binding order is invisible to all of
/// them. It is not invisible to a provider a caller describes themselves: a command with
/// <c>BindByName</c> off takes its parameters positionally, and two adjacent string parameters bound the
/// wrong way round produce no error at all — just a query for a scheduler named after a node.
/// </remarks>
public class StdAdoDelegateSchedulerStateTest
{
    private StdAdoDelegate adoDelegate = null!;
    private FakeCommand command = null!;
    private ConnectionAndTransactionHolder conn = null!;

    [SetUp]
    public void SetUp()
    {
        command = new FakeCommand();

        IDbProvider dbProvider = A.Fake<IDbProvider>();
        A.CallTo(() => dbProvider.Metadata).Returns(new DbMetadata { BindByName = true, ParameterNamePrefix = "@" });
        A.CallTo(() => dbProvider.CreateCommand()).Returns(command);

        adoDelegate = new StdAdoDelegate();
        adoDelegate.Initialize(new DriverDelegateContext
        {
            TablePrefix = "QRTZ_",
            SchedulerName = "TESTSCHED",
            InstanceId = "node-1",
            TypeLoader = new SimpleTypeLoader(),
            DbProvider = dbProvider,
        });

        conn = new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);
    }

    [TearDown]
    public void TearDown()
    {
        command.Dispose();
        conn.Dispose();
    }

    [Test]
    public async Task SelectSchedulerStateRecords_BindsOneNodesParametersInTheOrderTheStatementNamesThem()
    {
        InstallEmptyReader();

        await adoDelegate.SelectSchedulerStateRecords(conn, "node-1");

        BoundValues().Should().Equal(["TESTSCHED", "node-1"],
            "SqlSelectSchedulerState names @schedulerName before @instanceName, so a provider that binds "
            + "positionally would otherwise look for a scheduler called 'node-1' on a node called 'TESTSCHED'");
    }

    [Test]
    public async Task SelectSchedulerStateRecords_BindsOnlyTheSchedulerWhenEveryNodeIsAskedFor()
    {
        InstallEmptyReader();

        await adoDelegate.SelectSchedulerStateRecords(conn, instanceId: null);

        BoundValues().Should().Equal(["TESTSCHED"],
            "SqlSelectSchedulerStates has no instance predicate, so binding one would leave the command a parameter over");
    }

    [Test]
    public async Task SelectSchedulerStateRecords_ReadsTheVersionEachNodeStamped()
    {
        DateTimeOffset checkedIn = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
        InstallReader(
            ("node-46", checkedIn, "4.6.0"),
            ("node-45", checkedIn, null));

        List<SchedulerStateRecord> records = await adoDelegate.SelectSchedulerStateRecords(conn, instanceId: null);

        records.Should().HaveCount(2);
        records[0].QuartzVersion.Should().Be("4.6.0", "a 4.6 node stamps its row at every check-in");
        records[1].QuartzVersion.Should().BeNull("a row a node older than 4.6 wrote has no stamp, which is how a peer tells it is older (#4067)");
        records[0].CheckinTimestamp.Should().Be(checkedIn);
    }

    [Test]
    public async Task InsertSchedulerState_BindsTheNodesVersionLast()
    {
        command.RowsAffected = 1;

        await adoDelegate.InsertSchedulerState(conn, "node-1", new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero), TimeSpan.FromSeconds(7.5));

        BoundValues().Should().HaveCount(5).And.StartWith(["TESTSCHED", "node-1"]);
        BoundValues()[4].Should().Be(SchedulerNodeVersion.Current,
            "the row says which Quartz the node runs, in statement order after the interval, for providers that bind positionally");
    }

    [Test]
    public async Task UpdateSchedulerState_RestampsTheVersionWithEveryCheckIn()
    {
        command.RowsAffected = 1;

        await adoDelegate.UpdateSchedulerState(conn, "node-1", new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));

        BoundValues().Should().HaveCount(4);
        BoundValues()[1].Should().Be(SchedulerNodeVersion.Current,
            "written on every check-in, not only the first: a row an older node left under this instance id would otherwise keep saying nothing");
        BoundValues().Skip(2).Should().Equal(["TESTSCHED", "node-1"], "the key follows the two SET values, in the order the statement names them");
    }

    private void InstallReader(params (string InstanceId, DateTimeOffset CheckedIn, string? Version)[] rows)
    {
        DbDataReader reader = A.Fake<DbDataReader>();
        int index = -1;
        A.CallTo(() => reader.ReadAsync(A<CancellationToken>._)).ReturnsLazily(() => Task.FromResult(++index < rows.Length));
        A.CallTo(() => reader[AdoConstants.ColumnInstanceName]).ReturnsLazily(() => rows[index].InstanceId);
        A.CallTo(() => reader[AdoConstants.ColumnLastCheckinTime]).ReturnsLazily(() => (object) rows[index].CheckedIn.UtcTicks);
        A.CallTo(() => reader[AdoConstants.ColumnCheckinInterval]).ReturnsLazily(() => (object) 7500L);
        A.CallTo(() => reader[AdoConstants.ColumnQuartzVersion]).ReturnsLazily(() => (object?) rows[index].Version ?? DBNull.Value);
        command.Reader = reader;
    }

    private List<object?> BoundValues()
    {
        List<object?> values = [];
        foreach (DbParameter parameter in command.Parameters)
        {
            values.Add(parameter.Value);
        }

        return values;
    }

    private void InstallEmptyReader()
    {
        DbDataReader reader = A.Fake<DbDataReader>();
        A.CallTo(() => reader.ReadAsync(A<CancellationToken>._)).Returns(false);
        command.Reader = reader;
    }
}
