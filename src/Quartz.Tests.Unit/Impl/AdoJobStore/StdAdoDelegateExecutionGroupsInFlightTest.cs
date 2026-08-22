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

using Microsoft.Data.SqlClient;

using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// The cluster-wide in-flight count a <see cref="ExecutionLimitScope.Cluster" /> execution limit is
/// enforced against: the statement that reads it, how its rows are turned into
/// <see cref="ExecutionGroupInFlight" />, and that the result reaches the acquisition filter.
/// </summary>
/// <remarks>
/// Asserted against a faked reader rather than a database, for the same reason
/// <see cref="StdAdoDelegateGroupMatcherTest" /> is: the SQL has no dialect variants, so what is worth
/// pinning is the statement text and the reading, and both are the same on every provider. The
/// end-to-end behaviour against a real database is <c>JobStoreContractTest</c>'s.
/// </remarks>
public class StdAdoDelegateExecutionGroupsInFlightTest
{
    private StdAdoDelegate adoDelegate;
    private StubCommand command;
    private ConnectionAndTransactionHolder conn;

    [SetUp]
    public void SetUp()
    {
        command = A.Fake<StubCommand>();

        A.CallTo(command).Where(x => x.Method.Name == "get_DbParameterCollection")
            .WithReturnType<DbParameterCollection>()
            .Returns(new StubParameterCollection());

        A.CallTo(command).Where(x => x.Method.Name == "CreateDbParameter")
            .WithReturnType<DbParameter>()
            .ReturnsLazily(() => new SqlParameter());

        IDbProvider dbProvider = A.Fake<IDbProvider>();
        A.CallTo(() => dbProvider.Metadata).Returns(new DbMetadata { BindByName = true, ParameterNamePrefix = "@" });
        A.CallTo(() => dbProvider.CreateCommand()).Returns(command);

        adoDelegate = new StdAdoDelegate();
        adoDelegate.Initialize(new DriverDelegateContext
        {
            TablePrefix = "QRTZ_",
            SchedulerName = "TESTSCHED",
            InstanceId = "INSTANCE",
            TypeLoader = new SimpleTypeLoader(),
            DbProvider = dbProvider
        });

        conn = new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);
    }

    [TearDown]
    public void TearDown()
    {
        command?.Dispose();
        conn?.Dispose();
    }

    [Test]
    public async Task SelectExecutionGroupsInFlight_AggregatesTheFiredTriggersTableByBothGroups()
    {
        InstallRows([("tenant-acme", "nightly", 2)]);

        await adoDelegate.SelectExecutionGroupsInFlight(conn);

        command.CommandText.Should().Be(
            "SELECT EXECUTION_GROUP, TRIGGER_GROUP, COUNT(*) FROM QRTZ_FIRED_TRIGGERS WHERE SCHED_NAME = @schedulerName GROUP BY EXECUTION_GROUP, TRIGGER_GROUP",
            "the count comes from the reservation ledger the store already keeps, scoped to this scheduler, and grouped by both names because the limits derive their key from the pair");
    }

    [Test]
    public async Task SelectExecutionGroupsInFlight_ReturnsOneEntryPerPairInFlight()
    {
        InstallRows(
        [
            ("tenant-acme", "nightly", 2),
            ("tenant-acme", "hourly", 1),
            ("batch", "nightly", 5),
        ]);

        List<ExecutionGroupInFlight> result = await adoDelegate.SelectExecutionGroupsInFlight(conn);

        result.Should().BeEquivalentTo(new[]
        {
            new ExecutionGroupInFlight("tenant-acme", "nightly", 2),
            new ExecutionGroupInFlight("tenant-acme", "hourly", 1),
            new ExecutionGroupInFlight("batch", "nightly", 5),
        }, "folding the pairs down to one limit key is the caller's job, so the rows come back as the database grouped them");
    }

    [Test]
    public async Task SelectExecutionGroupsInFlight_ReadsARowThatCarriesNoExecutionGroup()
    {
        InstallRows([(null, "nightly", 3)]);

        List<ExecutionGroupInFlight> result = await adoDelegate.SelectExecutionGroupsInFlight(conn);

        result.Should().ContainSingle().Which.ExecutionGroup.Should().BeNull(
            "a firing with no execution group is a NULL in the column, and the ungrouped bucket can be limited too");
    }

    /// <summary>
    /// <c>COUNT(*)</c> is <c>Int32</c> on SQL Server and <c>Int64</c> on PostgreSQL, MySQL and SQLite,
    /// so the reader converts rather than casting. Reading it as either one directly would work on half
    /// the dialects and throw on the other half.
    /// </summary>
    [Test]
    public async Task SelectExecutionGroupsInFlight_ConvertsACountWhateverWidthTheProviderReturns()
    {
        InstallRows([("tenant-acme", "nightly", 2L), ("batch", "nightly", 7)]);

        List<ExecutionGroupInFlight> result = await adoDelegate.SelectExecutionGroupsInFlight(conn);

        result.Select(x => x.Count).Should().Equal([2, 7]);
    }

    [Test]
    public async Task SelectExecutionGroupsInFlight_IsEmptyWhenTheClusterIsIdle()
    {
        InstallRows([]);

        List<ExecutionGroupInFlight> result = await adoDelegate.SelectExecutionGroupsInFlight(conn);

        result.Should().BeEmpty();
    }

    /// <summary>
    /// The other half of the round trip: what the aggregate returned has to reach the filter that skips
    /// candidates, or the count would be read and then ignored.
    /// </summary>
    [Test]
    public async Task SelectTriggersToAcquire_SkipsACandidateWhoseGroupIsAtItsClusterCeiling()
    {
        InstallAcquisitionRows([("t1", "nightly", "acme"), ("t2", "nightly", "batch")]);

        List<TriggerAcquireResult> results = await adoDelegate.SelectTriggersToAcquire(conn, new TriggerAcquisitionCriteria
        {
            NoLaterThan = DateTimeOffset.UtcNow.AddMinutes(1),
            NoEarlierThan = DateTimeOffset.UtcNow.AddMinutes(-1),
            MaxCount = 5,
            LiveNodeCutoff = DateTimeOffset.UtcNow.AddMinutes(-1),
            ExecutionLimits = ExecutionLimitsBuilder.Create()
                .ForGroup("acme", 1, ExecutionLimitScope.Cluster)
                .Build(),
            ClusterInFlight = [new ExecutionGroupInFlight("acme", "nightly", 1)],
        });

        results.Should().ContainSingle("acme's one cluster-wide slot is already held, so only the other candidate may be taken")
            .Which.ExecutionGroup.Should().Be("batch");
    }

    /// <summary>
    /// Fakes the aggregate's result set. The projection is read positionally, so the values are
    /// positional here too — that is what pins the column order to the ordinals the reader uses.
    /// </summary>
    private void InstallRows((string ExecutionGroup, string TriggerGroup, object Count)[] rows)
    {
        DbDataReader reader = A.Fake<DbDataReader>();
        int index = -1;

        A.CallTo(() => reader.ReadAsync(A<CancellationToken>._)).ReturnsLazily(() =>
        {
            index++;
            return index < rows.Length;
        });

        A.CallTo(() => reader.IsDBNull(A<int>._)).ReturnsLazily((int i) => i == 0 && rows[index].ExecutionGroup is null);
        A.CallTo(() => reader.GetString(A<int>._)).ReturnsLazily((int i) => i == 0 ? rows[index].ExecutionGroup : rows[index].TriggerGroup);
        A.CallTo(() => reader.GetValue(A<int>._)).ReturnsLazily((int _) => rows[index].Count);

        InstallReader(reader);
    }

    /// <summary>
    /// Fakes the candidate result set the acquisition select produces, which is read by ordinal after
    /// asking for each column by name.
    /// </summary>
    private void InstallAcquisitionRows((string TriggerName, string TriggerGroup, string ExecutionGroup)[] rows)
    {
        DbDataReader reader = A.Fake<DbDataReader>();
        int index = -1;

        A.CallTo(() => reader.ReadAsync(A<CancellationToken>._)).ReturnsLazily(() =>
        {
            index++;
            return index < rows.Length;
        });

        A.CallTo(() => reader.GetOrdinal(A<string>._)).ReturnsLazily((string name) => name switch
        {
            AdoConstants.ColumnTriggerName => 0,
            AdoConstants.ColumnTriggerGroup => 1,
            AdoConstants.ColumnJobClass => 2,
            AdoConstants.ColumnExecutionGroup => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "unexpected column")
        });

        A.CallTo(() => reader.IsDBNull(A<int>._)).ReturnsLazily((int i) => i == 3 && rows[index].ExecutionGroup is null);
        A.CallTo(() => reader.GetString(A<int>._)).ReturnsLazily((int i) => i switch
        {
            0 => rows[index].TriggerName,
            1 => rows[index].TriggerGroup,
            2 => typeof(NoOpJob).AssemblyQualifiedName,
            _ => rows[index].ExecutionGroup
        });

        InstallReader(reader);
    }

    private void InstallReader(DbDataReader reader)
    {
        A.CallTo(command)
            .Where(x => x.Method.Name == "ExecuteDbDataReaderAsync")
            .WithReturnType<Task<DbDataReader>>()
            .Returns(Task.FromResult(reader));
    }

    private sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
