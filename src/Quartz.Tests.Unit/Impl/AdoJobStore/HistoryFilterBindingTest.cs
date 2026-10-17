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
using System.Text.RegularExpressions;

using FakeItEasy;

using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// The statements a filtered history page, misfire page and chart are read with: which predicates they
/// carry, and the order their parameters are bound in.
/// </summary>
/// <remarks>
/// A provider with <c>BindByName</c> off takes its parameters by position, so every predicate appends its
/// text and its values in step. A predicate whose values were bound out of order is silent on every
/// driver Quartz describes, which all bind by name, and wrong on the one a caller describes themselves.
/// </remarks>
public sealed partial class HistoryFilterBindingTest
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

        DbDataReader reader = A.Fake<DbDataReader>();
        A.CallTo(() => reader.ReadAsync(A<CancellationToken>._)).Returns(false);
        command.Reader = reader;
    }

    [TearDown]
    public void TearDown()
    {
        command.Dispose();
        conn.Dispose();
    }

    [Test]
    public async Task ATriggerAndAFiringAreMatchedByEquality()
    {
        await Read(new ExecutionHistoryQuery
        {
            SchedulerName = "TESTSCHED",
            Trigger = new TriggerKey("at-midnight", "nightly"),
            FireInstanceId = "node-a-17"
        });

        command.CommandText.Should()
            .Contain($" AND {AdoConstants.ColumnTriggerGroup} = @{SqlParameters.TriggerGroup} AND {AdoConstants.ColumnTriggerName} = @{SqlParameters.TriggerName}")
            .And.Contain($" AND {AdoConstants.ColumnFireInstanceId} = @{SqlParameters.HistoryFireInstanceId}")
            .And.NotContain(" LIKE ", "an exact filter is an equality the database can seek on, never a pattern");
        Bound().Should().Contain([
            (SqlParameters.TriggerGroup, "nightly"),
            (SqlParameters.TriggerName, "at-midnight"),
            (SqlParameters.HistoryFireInstanceId, "node-a-17")
        ]);
    }

    [Test]
    public async Task EveryFilterIsBoundInTheOrderTheStatementNamesIt()
    {
        await Read(new ExecutionHistoryQuery
        {
            SchedulerName = "TESTSCHED",
            SchedulerInstanceId = "node-a",
            JobContains = "night",
            TriggerContains = "mid",
            FailedFinally = true,
            Job = new JobKey("nightly-report", "reports"),
            Trigger = new TriggerKey("at-midnight", "nightly"),
            FireInstanceId = "node-a-17",
            FiredFrom = DateTimeOffset.UnixEpoch,
            FiredBefore = DateTimeOffset.UnixEpoch.AddDays(1),
            Results = [JobRunResult.Failed, JobRunResult.Cancelled]
        });

        AssertBoundInStatementOrder(SqlParameters.TriggerGroup, SqlParameters.TriggerName, SqlParameters.HistoryFireInstanceId);
    }

    [Test]
    public async Task TheMisfireListingBindsItsTriggerInStatementOrder()
    {
        await adoDelegate.SelectMisfireHistory(conn, new MisfireHistoryQuery
        {
            SchedulerName = "TESTSCHED",
            SchedulerInstanceId = "node-a",
            TriggerContains = "mid",
            Job = new JobKey("nightly-report", "reports"),
            Trigger = new TriggerKey("at-midnight", "nightly"),
            Reasons = [MisfireReason.Missed, MisfireReason.Vetoed]
        }, DateTimeOffset.UnixEpoch);

        command.CommandText.Should().Contain(StdAdoConstants.SqlHistoryTriggerPredicate);
        AssertBoundInStatementOrder(SqlParameters.TriggerGroup, SqlParameters.TriggerName);
    }

    [Test]
    public async Task TheRunStatisticsBindTheTriggerAndTheFiringInStatementOrder()
    {
        await adoDelegate.SelectExecutionStatistics(conn, new ExecutionStatisticsQuery
        {
            SchedulerName = "TESTSCHED",
            Job = new JobKey("nightly-report", "reports"),
            Trigger = new TriggerKey("at-midnight", "nightly"),
            FireInstanceId = "node-a-17",
            JobGroup = "reports",
            Results = [JobRunResult.Failed]
        }, DateTimeOffset.UnixEpoch);

        command.CommandText.Should().Contain(StdAdoConstants.SqlHistoryTriggerPredicate)
            .And.Contain(StdAdoConstants.SqlExecutionHistoryFireInstancePredicate);
        AssertBoundInStatementOrder(SqlParameters.TriggerGroup, SqlParameters.TriggerName, SqlParameters.HistoryFireInstanceId);
    }

    /// <summary>
    /// The bound parameters carry <paramref name="expected" />, and are bound one per placeholder in the
    /// order the statement names them.
    /// </summary>
    private void AssertBoundInStatementOrder(params string[] expected)
    {
        List<string> bound = Bound().Select(parameter => parameter.Name).ToList();

        bound.Should().Contain(expected, "the trigger and the firing are filters of their own");
        bound.Should().Equal(Placeholder().Matches(command.CommandText).Select(match => match.Groups[1].Value),
            "a provider that binds by position takes one value per placeholder, in the statement's order");
    }

    private ValueTask<PagedResult<ExecutionHistoryEntry>> Read(ExecutionHistoryQuery query)
    {
        return adoDelegate.SelectExecutionHistory(conn, query, DateTimeOffset.UnixEpoch);
    }

    private List<(string Name, object? Value)> Bound()
    {
        List<(string Name, object? Value)> bound = [];
        foreach (DbParameter parameter in command.Parameters)
        {
            bound.Add((parameter.ParameterName.TrimStart('@'), parameter.Value));
        }

        return bound;
    }

    [GeneratedRegex("@([A-Za-z0-9_]+)")]
    private static partial Regex Placeholder();
}
