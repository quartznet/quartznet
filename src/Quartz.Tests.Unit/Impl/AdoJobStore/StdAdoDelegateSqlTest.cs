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

using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using FakeItEasy;

using Microsoft.Data.SqlClient;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Impl.Matchers;
using Quartz.Simpl;
using Quartz.Util;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// Runs delegate members against a recording ADO.NET provider and inspects the statement that was
/// prepared together with the parameters that were bound to it.
/// </summary>
/// <remarks>
/// The parameter-name checks matter because <see cref="AdoUtil" /> binds by name: a bound name that
/// does not occur in the statement leaves the statement's own token unbound, which every provider
/// rejects at execution time. Several of these members have no in-tree caller, so only a test like
/// this exercises them.
/// </remarks>
public class StdAdoDelegateSqlTest
{
    private static readonly DateTimeOffset SomeTime = new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.Zero);

    [Test]
    public async Task SelectMisfiredTriggersShouldBindEveryParameterItsStatementNames()
    {
        var harness = TestHarness.Create();

        await harness.Delegate.SelectMisfiredTriggers(harness.Connection, SomeTime);

        harness.AssertBoundParametersMatchStatement();
    }

    [Test]
    public async Task HasMisfiredTriggersInStateShouldBindEveryParameterItsStatementNames()
    {
        var harness = TestHarness.Create();

        await harness.Delegate.HasMisfiredTriggersInState(harness.Connection, AdoConstants.StateWaiting, SomeTime);

        harness.AssertBoundParametersMatchStatement();
    }

    [Test]
    public async Task SelectMisfiredTriggersInGroupInStateShouldBindEveryParameterItsStatementNames()
    {
        var harness = TestHarness.Create();

        await harness.Delegate.SelectMisfiredTriggersInGroupInState(harness.Connection, "group", AdoConstants.StateWaiting, SomeTime);

        harness.AssertBoundParametersMatchStatement();
    }

    [Test]
    public async Task SelectTriggerForFireTimeShouldBindEveryParameterItsStatementNames()
    {
        var harness = TestHarness.Create();

        await harness.Delegate.SelectTriggerForFireTime(harness.Connection, SomeTime);

        harness.AssertBoundParametersMatchStatement();
    }

    [Test]
    public async Task CountMisfiredTriggersInStateShouldBindEveryParameterItsStatementNames()
    {
        var harness = TestHarness.Create();

        await harness.Delegate.CountMisfiredTriggersInState(harness.Connection, AdoConstants.StateWaiting, SomeTime);

        harness.AssertBoundParametersMatchStatement();
    }

    [Test]
    public async Task HasMisfiredTriggersInStateWithLimitShouldBindEveryParameterItsStatementNames()
    {
        var harness = TestHarness.Create();

        await harness.Delegate.HasMisfiredTriggersInState(harness.Connection, AdoConstants.StateWaiting, SomeTime, 10, new List<TriggerKey>());

        harness.AssertBoundParametersMatchStatement();
    }

    [Test]
    public async Task SelectTriggersInGroupWithEqualityMatcherShouldCompareForEquality()
    {
        var harness = TestHarness.Create();

        await harness.Delegate.SelectTriggersInGroup(harness.Connection, GroupMatcher<TriggerKey>.GroupEquals("50%"));

        harness.Sql.Should().Contain("TRIGGER_GROUP = @triggerGroup");
        harness.Sql.Should().NotContain("LIKE");
        harness.BoundValue("triggerGroup").Should().Be("50%", "an equality matcher names the group literally");
    }

    [Test]
    public async Task SelectTriggersInGroupWithStartsWithMatcherShouldEscapeWildcardsInTheValue()
    {
        var harness = TestHarness.Create();

        await harness.Delegate.SelectTriggersInGroup(harness.Connection, GroupMatcher<TriggerKey>.GroupStartsWith("a_b%c!d"));

        harness.Sql.Should().Contain("TRIGGER_GROUP LIKE @triggerGroup ESCAPE '!'");
        harness.BoundValue("triggerGroup").Should().Be("a!_b!%c!!d%",
            "the matcher's own text is a literal, only the trailing '%' the matcher adds stays a wildcard");
    }

    [Test]
    public async Task SelectJobsInGroupWithEqualityMatcherShouldCompareForEquality()
    {
        var harness = TestHarness.Create();

        await harness.Delegate.SelectJobsInGroup(harness.Connection, GroupMatcher<JobKey>.GroupEquals("a_b"));

        harness.Sql.Should().Contain("JOB_GROUP = @jobGroup");
        harness.Sql.Should().NotContain("LIKE");
        harness.BoundValue("jobGroup").Should().Be("a_b");
    }

    [Test]
    public async Task SelectJobsInGroupWithContainsMatcherShouldEscapeWildcardsInTheValue()
    {
        var harness = TestHarness.Create();

        await harness.Delegate.SelectJobsInGroup(harness.Connection, GroupMatcher<JobKey>.GroupContains("50%"));

        harness.Sql.Should().Contain("JOB_GROUP LIKE @jobGroup ESCAPE '!'");
        harness.BoundValue("jobGroup").Should().Be("%50!%%");
    }

    [Test]
    public async Task SelectTriggerGroupsWithEqualityMatcherShouldCompareForEquality()
    {
        var harness = TestHarness.Create();

        await harness.Delegate.SelectTriggerGroups(harness.Connection, GroupMatcher<TriggerKey>.GroupEquals("a_b"));

        harness.Sql.Should().Contain("TRIGGER_GROUP = @triggerGroup");
        harness.Sql.Should().NotContain("LIKE");
        harness.BoundValue("triggerGroup").Should().Be("a_b");
    }

    [Test]
    public async Task SelectTriggerGroupsWithStartsWithMatcherShouldUseEscapedLike()
    {
        var harness = TestHarness.Create();

        await harness.Delegate.SelectTriggerGroups(harness.Connection, GroupMatcher<TriggerKey>.GroupStartsWith("a_b"));

        harness.Sql.Should().Contain("TRIGGER_GROUP LIKE @triggerGroup ESCAPE '!'");
        harness.BoundValue("triggerGroup").Should().Be("a!_b%");
    }

    [Test]
    public async Task UpdateTriggerGroupStateFromOtherStateWithEqualityMatcherShouldCompareForEquality()
    {
        var harness = TestHarness.Create();

        await harness.Delegate.UpdateTriggerGroupStateFromOtherState(
            harness.Connection,
            GroupMatcher<TriggerKey>.GroupEquals("a_b"),
            AdoConstants.StateWaiting,
            AdoConstants.StatePaused);

        harness.Sql.Should().Contain("TRIGGER_GROUP = @triggerGroup");
        harness.Sql.Should().NotContain("LIKE");
        harness.BoundValue("triggerGroup").Should().Be("a_b");
        harness.AssertBoundParametersMatchStatement();
    }

    [Test]
    public async Task UpdateTriggerGroupStateFromOtherStateWithStartsWithMatcherShouldUseEscapedLike()
    {
        var harness = TestHarness.Create();

        await harness.Delegate.UpdateTriggerGroupStateFromOtherState(
            harness.Connection,
            GroupMatcher<TriggerKey>.GroupStartsWith("a_b"),
            AdoConstants.StateWaiting,
            AdoConstants.StatePaused);

        harness.Sql.Should().Contain("TRIGGER_GROUP LIKE @triggerGroup ESCAPE '!'");
        harness.BoundValue("triggerGroup").Should().Be("a!_b%");
        harness.AssertBoundParametersMatchStatement();
    }

    [Test]
    public async Task UpdateTriggerGroupStateFromOtherStatesWithEqualityMatcherShouldCompareForEquality()
    {
        var harness = TestHarness.Create();

        await harness.Delegate.UpdateTriggerGroupStateFromOtherStates(
            harness.Connection,
            GroupMatcher<TriggerKey>.GroupEquals("a_b"),
            AdoConstants.StateWaiting,
            AdoConstants.StatePaused,
            AdoConstants.StatePausedBlocked,
            AdoConstants.StateBlocked);

        harness.Sql.Should().Contain("TRIGGER_GROUP = @groupName");
        harness.Sql.Should().NotContain("LIKE");
        harness.BoundValue("groupName").Should().Be("a_b");
        harness.AssertBoundParametersMatchStatement();
    }

    [Test]
    public async Task UpdateTriggerGroupStateFromOtherStatesWithStartsWithMatcherShouldUseEscapedLike()
    {
        var harness = TestHarness.Create();

        await harness.Delegate.UpdateTriggerGroupStateFromOtherStates(
            harness.Connection,
            GroupMatcher<TriggerKey>.GroupStartsWith("a_b"),
            AdoConstants.StateWaiting,
            AdoConstants.StatePaused,
            AdoConstants.StatePausedBlocked,
            AdoConstants.StateBlocked);

        harness.Sql.Should().Contain("TRIGGER_GROUP LIKE @groupName ESCAPE '!'");
        harness.BoundValue("groupName").Should().Be("a!_b%");
        harness.AssertBoundParametersMatchStatement();
    }

    [Test]
    public async Task DeletePausedTriggerGroupWithEqualityMatcherShouldCompareForEquality()
    {
        var harness = TestHarness.Create();

        await harness.Delegate.DeletePausedTriggerGroup(harness.Connection, GroupMatcher<TriggerKey>.GroupEquals("a_b"));

        harness.Sql.Should().Contain("TRIGGER_GROUP = @triggerGroup");
        harness.Sql.Should().NotContain("LIKE");
        harness.BoundValue("triggerGroup").Should().Be("a_b");
    }

    [Test]
    public async Task DeletePausedTriggerGroupWithStartsWithMatcherShouldUseEscapedLike()
    {
        var harness = TestHarness.Create();

        await harness.Delegate.DeletePausedTriggerGroup(harness.Connection, GroupMatcher<TriggerKey>.GroupStartsWith("a_b"));

        harness.Sql.Should().Contain("TRIGGER_GROUP LIKE @triggerGroup ESCAPE '!'");
        harness.BoundValue("triggerGroup").Should().Be("a!_b%");
    }

    [Test]
    public async Task DeletePausedTriggerGroupByNameShouldDeleteTheAllGroupsPausedSentinelLiterally()
    {
        var harness = TestHarness.Create();

        await harness.Delegate.DeletePausedTriggerGroup(harness.Connection, AdoConstants.AllGroupsPaused);

        harness.Sql.Should().Contain("TRIGGER_GROUP = @triggerGroup");
        harness.Sql.Should().NotContain("LIKE",
            "the sentinel's underscores would be wildcards in a LIKE, so ResumeAll could delete other groups");
        harness.BoundValue("triggerGroup").Should().Be("_$_ALL_GROUPS_PAUSED_$_");
    }

    [Test]
    public async Task SelectInstancesFiredTriggerRecordsShouldReadPriority()
    {
        var harness = TestHarness.Create();
        var reader = harness.DataReader;

        A.CallTo(() => reader.ReadAsync(A<CancellationToken>._)).Returns(true).Once();
        A.CallTo(() => reader[AdoConstants.ColumnEntryId]).Returns("TESTSCHED_1");
        A.CallTo(() => reader[AdoConstants.ColumnEntryState]).Returns(AdoConstants.StateAcquired);
        A.CallTo(() => reader[AdoConstants.ColumnFiredTime]).Returns(SomeTime.UtcTicks);
        A.CallTo(() => reader[AdoConstants.ColumnScheduledTime]).Returns(SomeTime.UtcTicks);
        A.CallTo(() => reader[AdoConstants.ColumnPriority]).Returns(7);
        A.CallTo(() => reader[AdoConstants.ColumnInstanceName]).Returns("INSTANCE");
        A.CallTo(() => reader[AdoConstants.ColumnTriggerName]).Returns("trigger");
        A.CallTo(() => reader[AdoConstants.ColumnTriggerGroup]).Returns("group");

        var records = await harness.Delegate.SelectInstancesFiredTriggerRecords(harness.Connection, "INSTANCE");

        records.Should().ContainSingle()
            .Which.Priority.Should().Be(7, "ClusterRecover copies this onto the recovery trigger it builds");
    }

    [Test]
    public void AnyGroupMatcherShouldProduceABareWildcard()
    {
        var del = new LikeClauseExposingDelegate();

        del.ToSqlLikeClausePublic(GroupMatcher<TriggerKey>.AnyGroup()).Should().Be("%");
    }

    [TestCase("plain", "plain")]
    [TestCase("a_b", "a!_b")]
    [TestCase("50%", "50!%")]
    [TestCase("wow!", "wow!!")]
    [TestCase("_%!", "!_!%!!")]
    public void EqualityLikeClauseShouldEscapeWildcards(string group, string expected)
    {
        var del = new LikeClauseExposingDelegate();

        del.ToSqlLikeClausePublic(GroupMatcher<TriggerKey>.GroupEquals(group)).Should().Be(expected);
    }

    [Test]
    public void EveryLikeStatementShouldNameTheEscapeCharacter()
    {
        string[] statements =
        {
            StdAdoConstants.SqlDeletePausedTriggerGroup,
            StdAdoConstants.SqlSelectJobsInGroupLike,
            StdAdoConstants.SqlSelectTriggerGroupsFiltered,
            StdAdoConstants.SqlSelectTriggersInGroupLike,
            StdAdoConstants.SqlUpdateTriggerGroupStateFromState,
            StdAdoConstants.SqlUpdateTriggerGroupStateFromStates
        };

        foreach (string statement in statements)
        {
            statement.Should().Contain("LIKE");
            statement.Should().Contain(" ESCAPE '!'",
                "a LIKE fed by ToSqlLikeClause has to name the escape character the clause uses");
        }
    }

    private sealed class LikeClauseExposingDelegate : StdAdoDelegate
    {
        public string ToSqlLikeClausePublic<T>(GroupMatcher<T> matcher) where T : Key<T> => ToSqlLikeClause(matcher);
    }

    /// <summary>
    /// A delegate wired to a provider that records the statement text and the parameters bound to it.
    /// </summary>
    private sealed class TestHarness
    {
        private readonly RecordingParameterCollection parameters;

        private TestHarness(
            StdAdoDelegate adoDelegate,
            ConnectionAndTransactionHolder connection,
            DbCommand command,
            DbDataReader dataReader,
            RecordingParameterCollection parameters)
        {
            Delegate = adoDelegate;
            Connection = connection;
            Command = command;
            DataReader = dataReader;
            this.parameters = parameters;
        }

        public StdAdoDelegate Delegate { get; }

        public ConnectionAndTransactionHolder Connection { get; }

        public DbCommand Command { get; }

        public DbDataReader DataReader { get; }

        public string Sql => Command.CommandText;

        public object BoundValue(string parameterName)
        {
            var parameter = parameters.Recorded.SingleOrDefault(x => x.ParameterName == "@" + parameterName);
            parameter.Should().NotBeNull($"parameter '{parameterName}' should have been bound to: {Sql}");
            return parameter.Value;
        }

        /// <summary>
        /// Asserts that the names bound to the command are exactly the tokens the statement itself
        /// names — a mismatch either way makes the statement fail on every provider.
        /// </summary>
        public void AssertBoundParametersMatchStatement()
        {
            var namesInStatement = Regex.Matches(Sql, "@([A-Za-z0-9_]+)")
                .Cast<Match>()
                .Select(x => x.Groups[1].Value)
                .Distinct()
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();

            var boundNames = parameters.Recorded
                .Select(x => x.ParameterName.TrimStart('@'))
                .Distinct()
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();

            boundNames.Should().Equal(namesInStatement,
                $"AdoUtil binds by name, so an unmatched name leaves a token unbound in: {Sql}");
        }

        public static TestHarness Create()
        {
            var command = A.Fake<StubCommand>();
            var parameters = new RecordingParameterCollection();

            A.CallTo(command).Where(x => x.Method.Name == "get_DbParameterCollection")
                .WithReturnType<DbParameterCollection>()
                .Returns(parameters);

            A.CallTo(command).Where(x => x.Method.Name == "CreateDbParameter")
                .WithReturnType<DbParameter>()
                .ReturnsLazily(() => new SqlParameter());

            var dataReader = A.Fake<DbDataReader>();
            A.CallTo(() => dataReader.ReadAsync(A<CancellationToken>._)).Returns(false);

            A.CallTo(command).Where(x => x.Method.Name == "ExecuteDbDataReaderAsync")
                .WithReturnType<Task<DbDataReader>>()
                .Returns(Task.FromResult((DbDataReader) dataReader));

            A.CallTo(command).Where(x => x.Method.Name == "ExecuteScalarAsync")
                .WithReturnType<Task<object>>()
                .Returns(Task.FromResult<object>(0));

            var dbMetadata = new DbMetadata
            {
                BindByName = true,
                ParameterNamePrefix = "@"
            };
            dbMetadata.Init();

            var dbProvider = A.Fake<IDbProvider>();
            A.CallTo(() => dbProvider.Metadata).Returns(dbMetadata);
            A.CallTo(() => dbProvider.CreateCommand()).Returns(command);

            var adoDelegate = new StdAdoDelegate();
            adoDelegate.Initialize(new DelegateInitializationArgs
            {
                TablePrefix = "QRTZ_",
                InstanceId = "INSTANCE",
                InstanceName = "TESTSCHED",
                TypeLoadHelper = new SimpleTypeLoadHelper(),
                UseProperties = false,
                InitString = "",
                DbProvider = dbProvider
            });

            var connection = new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), A.Fake<DbTransaction>());
            return new TestHarness(adoDelegate, connection, command, dataReader, parameters);
        }
    }

    private sealed class RecordingParameterCollection : StubParameterCollection
    {
        private readonly List<DbParameter> recorded = new List<DbParameter>();

        public IReadOnlyList<DbParameter> Recorded => recorded;

        public override int Add(object value)
        {
            recorded.Add((DbParameter) value);
            return recorded.Count - 1;
        }

        public override int Count => recorded.Count;

        public override IEnumerator GetEnumerator() => recorded.GetEnumerator();

        protected override DbParameter GetParameter(int index) => recorded[index];
    }
}
