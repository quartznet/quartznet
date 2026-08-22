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

using System.Collections;
using System.Data.Common;

using FakeItEasy;

using Microsoft.Data.SqlClient;

using Quartz.Impl;
using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// The SQL <see cref="StdAdoDelegate" /> generates, asserted against the command text rather than a
/// database: how a <see cref="GroupMatcher{TKey}" /> becomes an '=' or an escaped LIKE, so a group
/// literally named "50%" is not a wildcard, and how a trigger state becomes a filter.
/// </summary>
public class StdAdoDelegateGroupMatcherTest
{
    private StdAdoDelegate adoDelegate;
    private StubCommand command;
    private RecordingParameterCollection parameters;
    private ConnectionAndTransactionHolder conn;

    [SetUp]
    public void SetUp()
    {
        parameters = new RecordingParameterCollection();
        command = A.Fake<StubCommand>();

        A.CallTo(command).Where(x => x.Method.Name == "get_DbParameterCollection")
            .WithReturnType<DbParameterCollection>()
            .Returns(parameters);

        A.CallTo(command).Where(x => x.Method.Name == "CreateDbParameter")
            .WithReturnType<DbParameter>()
            .ReturnsLazily(() => new SqlParameter());

        DbDataReader reader = A.Fake<DbDataReader>();
        A.CallTo(() => reader.ReadAsync(A<CancellationToken>._)).Returns(false);

        A.CallTo(command).Where(x => x.Method.Name == "ExecuteDbDataReaderAsync")
            .WithReturnType<Task<DbDataReader>>()
            .Returns(Task.FromResult(reader));

        adoDelegate = CreateDelegate(bindByName: true, parameterNamePrefix: "@");

        conn = new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);
    }

    /// <summary>
    /// Builds a delegate whose commands are this fixture's stub, so the generated SQL can be inspected.
    /// Providers differ in how they name parameters, which changes how the statement is rewritten.
    /// </summary>
    private StdAdoDelegate CreateDelegate(bool bindByName, string parameterNamePrefix)
    {
        IDbProvider dbProvider = A.Fake<IDbProvider>();
        DbMetadata metadata = new()
        {
            BindByName = bindByName,
            ParameterNamePrefix = parameterNamePrefix
        };
        A.CallTo(() => dbProvider.Metadata).Returns(metadata);
        A.CallTo(() => dbProvider.CreateCommand()).Returns(command);

        StdAdoDelegate result = new();
        result.Initialize(new DriverDelegateContext
        {
            TablePrefix = "QRTZ_",
            SchedulerName = "TESTSCHED",
            InstanceId = "INSTANCE",
            TypeLoader = new SimpleTypeLoader(),
            DbProvider = dbProvider
        });

        return result;
    }

    [TearDown]
    public void TearDown()
    {
        command?.Dispose();
        conn?.Dispose();
    }

    [Test]
    public async Task SelectTriggerGroupNames_WithEqualityMatcher_ShouldCompareWithEquals()
    {
        await adoDelegate.SelectTriggerGroupNames(conn, GroupMatcher<TriggerKey>.GroupEquals("50%"));

        command.CommandText.Should().Contain("TRIGGER_GROUP = @triggerGroup");
        command.CommandText.Should().NotContain("LIKE",
            "an equality matcher must not go through LIKE, where the group's own '%' would act as a wildcard");
        parameters.Value("@triggerGroup").Should().Be("50%", "an equality comparison binds the group name verbatim");
    }

    [Test]
    public async Task SelectTriggerGroupNames_WithStartsWithMatcher_ShouldEscapePercentInTheGroupName()
    {
        await adoDelegate.SelectTriggerGroupNames(conn, GroupMatcher<TriggerKey>.GroupStartsWith("50%"));

        command.CommandText.Should().Contain("TRIGGER_GROUP LIKE @triggerGroup ESCAPE '!'");
        parameters.Value("@triggerGroup").Should().Be("50!%%",
            "the group's own '%' is a literal and only the trailing wildcard this matcher adds stays one");
    }

    [Test]
    public async Task SelectJobsInGroup_WithContainsMatcher_ShouldEscapeUnderscoreInTheGroupName()
    {
        await adoDelegate.SelectJobKeysInGroup(conn, GroupMatcher<JobKey>.GroupContains("a_b"));

        command.CommandText.Should().Contain("JOB_GROUP LIKE @jobGroup ESCAPE '!'");
        parameters.Value("@jobGroup").Should().Be("%a!_b%",
            "'_' is the single-character LIKE wildcard, so a group name containing one has to be escaped");
    }

    [Test]
    public async Task SelectJobsInGroup_WithEndsWithMatcher_ShouldEscapeTheEscapeCharacterItself()
    {
        await adoDelegate.SelectJobKeysInGroup(conn, GroupMatcher<JobKey>.GroupEndsWith("a!b"));

        parameters.Value("@jobGroup").Should().Be("%a!!b",
            "an unescaped escape character would swallow the character after it");
    }

    [Test]
    public async Task SelectJobsInGroup_WithEqualityMatcher_ShouldCompareWithEquals()
    {
        await adoDelegate.SelectJobKeysInGroup(conn, GroupMatcher<JobKey>.GroupEquals("a_b"));

        command.CommandText.Should().Contain("JOB_GROUP = @jobGroup");
        command.CommandText.Should().NotContain("LIKE");
        parameters.Value("@jobGroup").Should().Be("a_b");
    }

    [Test]
    public async Task SelectTriggersInGroup_WithAnyGroup_ShouldMatchEveryGroup()
    {
        await adoDelegate.SelectTriggerKeysInGroup(conn, GroupMatcher<TriggerKey>.AnyGroup());

        command.CommandText.Should().Contain("TRIGGER_GROUP LIKE @triggerGroup ESCAPE '!'");
        parameters.Value("@triggerGroup").Should().Be("%");
    }

    [Test]
    public async Task DeletePausedTriggerGroup_WithEqualityMatcher_ShouldCompareWithEquals()
    {
        await adoDelegate.DeletePausedTriggerGroup(conn, GroupMatcher<TriggerKey>.GroupEquals(AdoConstants.AllGroupsPaused));

        command.CommandText.Should().Contain("TRIGGER_GROUP = @triggerGroup");
        command.CommandText.Should().NotContain("LIKE",
            "the all-groups-paused marker is full of underscores, which LIKE would read as wildcards");
        parameters.Value("@triggerGroup").Should().Be(AdoConstants.AllGroupsPaused);
    }

    [Test]
    public async Task DeletePausedTriggerGroup_WithStartsWithMatcher_ShouldUseLikeWithEscape()
    {
        await adoDelegate.DeletePausedTriggerGroup(conn, GroupMatcher<TriggerKey>.GroupStartsWith("a_"));

        command.CommandText.Should().Contain("TRIGGER_GROUP LIKE @triggerGroup ESCAPE '!'");
        parameters.Value("@triggerGroup").Should().Be("a!_%");
    }

    [Test]
    public async Task UpdateTriggerGroupStateFromOtherState_WithEqualityMatcher_ShouldCompareWithEquals()
    {
        await adoDelegate.UpdateTriggerGroupStateFromOtherState(
            conn,
            GroupMatcher<TriggerKey>.GroupEquals("50%"),
            StoredTriggerState.Paused,
            StoredTriggerState.Waiting);

        command.CommandText.Should().Contain("TRIGGER_GROUP = @triggerGroup");
        command.CommandText.Should().NotContain("LIKE");
        parameters.Value("@triggerGroup").Should().Be("50%");
    }

    [Test]
    public async Task UpdateTriggerGroupStateFromOtherState_WithStartsWithMatcher_ShouldUseLikeWithEscape()
    {
        await adoDelegate.UpdateTriggerGroupStateFromOtherState(
            conn,
            GroupMatcher<TriggerKey>.GroupStartsWith("50%"),
            StoredTriggerState.Paused,
            StoredTriggerState.Waiting);

        command.CommandText.Should().Contain("TRIGGER_GROUP LIKE @triggerGroup ESCAPE '!'");
        parameters.Value("@triggerGroup").Should().Be("50!%%");
    }

    [Test]
    public async Task UpdateTriggerGroupStateFromOtherStates_WithEqualityMatcher_ShouldCompareWithEquals()
    {
        await adoDelegate.UpdateTriggerGroupStateFromOtherStates(
            conn,
            GroupMatcher<TriggerKey>.GroupEquals("50%"),
            StoredTriggerState.Paused,
            [StoredTriggerState.Acquired, StoredTriggerState.Waiting]);

        command.CommandText.Should().Contain("TRIGGER_GROUP = @groupName");
        command.CommandText.Should().NotContain("LIKE");
        parameters.Value("@groupName").Should().Be("50%");
    }

    [Test]
    public async Task UpdateTriggerGroupStateFromOtherStates_WithStartsWithMatcher_ShouldUseLikeWithEscape()
    {
        await adoDelegate.UpdateTriggerGroupStateFromOtherStates(
            conn,
            GroupMatcher<TriggerKey>.GroupStartsWith("50%"),
            StoredTriggerState.Paused,
            [StoredTriggerState.Acquired, StoredTriggerState.Waiting]);

        command.CommandText.Should().Contain("TRIGGER_GROUP LIKE @groupName ESCAPE '!'");
        parameters.Value("@groupName").Should().Be("50!%%");
    }

    /// <summary>
    /// The old-state predicate is generated for the length of the set, so a caller is no longer stuck
    /// with the two or three the statement used to hard-code.
    /// </summary>
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(4)]
    public async Task UpdateTriggerStateFromOtherStates_ShouldBindOneParameterPerState(int stateCount)
    {
        StoredTriggerState[] states = Enum.GetValues<StoredTriggerState>().Take(stateCount).ToArray();

        await adoDelegate.UpdateTriggerStateFromOtherStates(conn, new TriggerKey("t1", "g1"), StoredTriggerState.Paused, states);

        BoundOldStates().Should().BeEquivalentTo(states.Select(x => x.ToStoredValue()));
        command.CommandText.Split(" OR ").Should().HaveCount(stateCount, "the predicate is a disjunction over the set");
    }

    /// <summary>
    /// A disjunction cannot tell a repeated term from a single one, so folding duplicates away only keeps
    /// the number of distinct statement texts — and so of database plans — down.
    /// </summary>
    [Test]
    public async Task UpdateTriggerStateFromOtherStates_ShouldFoldDuplicateStates()
    {
        await adoDelegate.UpdateTriggerStateFromOtherStates(
            conn,
            new TriggerKey("t1", "g1"),
            StoredTriggerState.Paused,
            [StoredTriggerState.Waiting, StoredTriggerState.Acquired, StoredTriggerState.Waiting]);

        BoundOldStates().Should().BeEquivalentTo([AdoConstants.StateWaiting, AdoConstants.StateAcquired]);
    }

    [Test]
    public async Task UpdateTriggerStateFromOtherStates_ShouldRejectAnEmptyStateSet()
    {
        Func<Task> act = async () => await adoDelegate.UpdateTriggerStateFromOtherStates(
            conn, new TriggerKey("t1", "g1"), StoredTriggerState.Paused, []);

        await act.Should().ThrowAsync<ArgumentException>(
            "a statement matching no state at all is a mistake, not a no-op");
    }

    [Test]
    public async Task SelectTriggerHeaders_WithStartsWithMatcher_ShouldUseLikeWithEscape()
    {
        await adoDelegate.SelectTriggerHeaders(conn, new TriggerQuery { Group = GroupMatcher<TriggerKey>.GroupStartsWith("50%") });

        command.CommandText.Should().Contain("TRIGGER_GROUP LIKE @triggerGroup ESCAPE '!'");
        parameters.Value("@triggerGroup").Should().Be("50!%%");
    }

    [Test]
    public async Task SelectTriggerStateWithExecuting_BuildsExpectedSql()
    {
        await adoDelegate.SelectTriggerStateWithExecuting(conn, new TriggerKey("trigger1", "group1"));

        string expectedCommandText = "SELECT TRIGGER_STATE, "
                                     + "CASE WHEN EXISTS ("
                                     + "SELECT 1 FROM QRTZ_FIRED_TRIGGERS FT "
                                     + "WHERE FT.SCHED_NAME = QRTZ_TRIGGERS.SCHED_NAME "
                                     + "AND FT.TRIGGER_NAME = QRTZ_TRIGGERS.TRIGGER_NAME "
                                     + "AND FT.TRIGGER_GROUP = QRTZ_TRIGGERS.TRIGGER_GROUP "
                                     + "AND FT.STATE = 'EXECUTING') "
                                     + "THEN 1 ELSE 0 END "
                                     + "FROM QRTZ_TRIGGERS "
                                     + "WHERE SCHED_NAME = @schedulerName "
                                     + "AND TRIGGER_NAME = @triggerName "
                                     + "AND TRIGGER_GROUP = @triggerGroup";
        command.CommandText.Should().Be(expectedCommandText);
    }

    /// <summary>
    /// Providers that bind positionally have their parameter names rewritten by a plain substring
    /// replace, so a statement mentioning one name twice would end up with more placeholders than bound
    /// parameters. The executing state is embedded as a literal precisely to avoid that.
    /// </summary>
    [Test]
    public async Task SelectTriggerStateWithExecuting_BindsPositionallyWithoutDuplicateParameters()
    {
        StdAdoDelegate positional = CreateDelegate(bindByName: false, parameterNamePrefix: "?");

        await positional.SelectTriggerStateWithExecuting(conn, new TriggerKey("trigger1", "group1"));

        command.CommandText.Should().NotContain("@", "every named parameter must have been rewritten");
        command.CommandText.Count(c => c == '?')
            .Should().Be(3, "exactly the scheduler name, trigger name and trigger group are bound");
        command.CommandText.Should().Contain("'EXECUTING'", "the state is a literal, not a parameter");
    }

    [Test]
    public async Task SelectTriggerHeaders_FilteringByExecuting_RequiresAFiredTriggerRow()
    {
        await adoDelegate.SelectTriggerHeaders(conn, new TriggerQuery { State = TriggerState.Executing });

        // Executing is not a stored state, so the filter has to reach into FIRED_TRIGGERS.
        command.CommandText.Should().Contain("AND EXISTS (SELECT 1 FROM QRTZ_FIRED_TRIGGERS FT");
        command.CommandText.Should().NotContain("AND NOT EXISTS");

        // Executing is also what an unrecognised stored state reports as while it is running, and those
        // values cannot be listed, so the filter excludes the states that outrank executing instead.
        command.CommandText.Should().Contain("AND TRIGGER_STATE NOT IN (");
        BoundStates().Should().BeEquivalentTo([
            AdoConstants.StatePaused,
            AdoConstants.StatePausedBlocked,
            AdoConstants.StateError,
            AdoConstants.StateDeleted
        ]);
    }

    [Test]
    public async Task SelectTriggerHeaders_FilteringByNormal_ExcludesExecutingTriggers()
    {
        await adoDelegate.SelectTriggerHeaders(conn, new TriggerQuery { State = TriggerState.Normal });

        // Otherwise the listing would return a row here and then report it as executing.
        command.CommandText.Should().Contain("AND NOT EXISTS (SELECT 1 FROM QRTZ_FIRED_TRIGGERS FT");

        // Normal is what an unrecognised stored state reports as, and those values cannot be listed, so
        // the filter excludes the states that report as something else instead of listing its own.
        command.CommandText.Should().Contain("AND TRIGGER_STATE NOT IN (");
        BoundStates().Should().BeEquivalentTo([
            AdoConstants.StateComplete,
            AdoConstants.StateBlocked,
            AdoConstants.StatePaused,
            AdoConstants.StatePausedBlocked,
            AdoConstants.StateError,
            AdoConstants.StateDeleted
        ]);
    }

    [Test]
    public async Task SelectTriggerHeaders_FilteringByPaused_IgnoresExecution()
    {
        await adoDelegate.SelectTriggerHeaders(conn, new TriggerQuery { State = TriggerState.Paused });

        // Paused outranks executing, so execution cannot change the answer and needs no predicate.
        command.CommandText.Should().NotContain("AND EXISTS");
        command.CommandText.Should().NotContain("AND NOT EXISTS");
        BoundStates().Should().BeEquivalentTo([AdoConstants.StatePaused, AdoConstants.StatePausedBlocked]);
    }

    /// <summary>
    /// The listing projection and <c>ReadTriggerHeader</c> agree on where the executing flag is: the
    /// reader takes it from a fixed ordinal, so a column added to the SELECT list ahead of it would make
    /// every listing read the wrong value. Only exercised here — the paging tests need a database.
    /// </summary>
    [TestCase(0, TriggerState.Normal)]
    [TestCase(1, TriggerState.Executing)]
    public async Task SelectTriggerHeaders_ReadsTheExecutingFlagFromTheProjection(int executingFlag, TriggerState expected)
    {
        InstallSingleRowReader(AdoConstants.StateWaiting, executingFlag);

        PagedResult<TriggerHeader> result = await adoDelegate.SelectTriggerHeaders(conn, new TriggerQuery());

        TriggerHeader header = result.Items.Should().ContainSingle().Subject;
        header.Key.Should().Be(new TriggerKey("trigger1", "group1"));
        header.State.Should().Be(expected);
    }

    /// <summary>
    /// Fakes one listing row. The values are positional on purpose: this is what pins the projection's
    /// column order to the reader's ordinals.
    /// </summary>
    private void InstallSingleRowReader(string triggerState, int executingFlag)
    {
        var strings = new Dictionary<int, string>
        {
            [0] = "trigger1",
            [1] = "group1",
            [2] = "job1",
            [3] = "jobGroup1",
            [4] = "description",
            [5] = "SIMPLE",
            [6] = triggerState,
            [11] = "calendar",
            [13] = "executionGroup"
        };

        DbDataReader reader = A.Fake<DbDataReader>();
        bool read = false;
        A.CallTo(() => reader.ReadAsync(A<CancellationToken>._)).ReturnsLazily(() =>
        {
            bool first = !read;
            read = true;
            return first;
        });

        A.CallTo(() => reader.GetString(A<int>._)).ReturnsLazily((int i) => strings[i]);
        A.CallTo(() => reader.IsDBNull(A<int>._)).ReturnsLazily((int i) => !strings.ContainsKey(i));

        // Date columns come back as DBNull so the reader's own null handling applies; priority and the
        // executing flag are the two the reader converts.
        A.CallTo(() => reader.GetValue(A<int>._)).ReturnsLazily((int i) => i switch
        {
            12 => 5,
            14 => executingFlag,
            _ => DBNull.Value
        });

        A.CallTo(command)
            .Where(x => x.Method.Name == "ExecuteDbDataReaderAsync")
            .WithReturnType<Task<DbDataReader>>()
            .Returns(Task.FromResult(reader));
    }

    /// <summary>
    /// The stored states the trigger-state filter bound, in no particular order.
    /// </summary>
    private List<object> BoundStates()
    {
        return parameters
            .Cast<DbParameter>()
            .Where(x => x.ParameterName.StartsWith("@state", StringComparison.Ordinal))
            .Select(x => x.Value)
            .ToList();
    }

    /// <summary>
    /// The stored states an old-state predicate bound, in the order the statement names them.
    /// </summary>
    private List<object> BoundOldStates()
    {
        return parameters
            .Cast<DbParameter>()
            .Where(x => x.ParameterName.StartsWith("@oldState", StringComparison.Ordinal))
            .Select(x => x.Value)
            .ToList();
    }

    [Test]
    public async Task SelectJobHeaders_WithStartsWithMatcher_ShouldUseLikeWithEscape()
    {
        await adoDelegate.SelectJobHeaders(conn, new JobQuery { Group = GroupMatcher<JobKey>.GroupStartsWith("50%") });

        command.CommandText.Should().Contain("JOB_GROUP LIKE @jobGroup ESCAPE '!'");
        parameters.Value("@jobGroup").Should().Be("50!%%");
    }

    [Test]
    public async Task SelectJobHeaders_WithNameMatcher_ShouldEscapeTheNameToo()
    {
        await adoDelegate.SelectJobHeaders(conn, new JobQuery
        {
            Group = GroupMatcher<JobKey>.GroupEquals("g"),
            Name = NameMatcher<JobKey>.NameStartsWith("50%")
        });

        command.CommandText.Should().Contain("JOB_GROUP = @jobGroup");
        command.CommandText.Should().Contain("JOB_NAME LIKE @jobName ESCAPE '!'");
        parameters.Value("@jobName").Should().Be("50!%%", "the matcher's own text is a literal, so its wildcards are escaped");
    }

    [Test]
    public async Task SelectTriggerHeaders_WithNameMatcher_ShouldCompareWithEqualsForAnEqualityMatcher()
    {
        await adoDelegate.SelectTriggerHeaders(conn, new TriggerQuery { Name = NameMatcher<TriggerKey>.NameEquals("nightly") });

        command.CommandText.Should().Contain("TRIGGER_NAME = @triggerName");
        command.CommandText.Should().NotContain("TRIGGER_NAME LIKE", "an equality matcher must not fall back to LIKE");
        parameters.Value("@triggerName").Should().Be("nightly");
    }

    [Test]
    public async Task SelectCalendarNames_WithNameMatcher_ShouldUseLikeWithEscape()
    {
        await adoDelegate.SelectCalendarNames(conn, new CalendarQuery { Name = CalendarNameMatcher.NameStartsWith("50%") });

        command.CommandText.Should().Contain("CALENDAR_NAME LIKE @calendarName ESCAPE '!'");
        command.CommandText.Should().Contain("ORDER BY CALENDAR_NAME", "the listing keeps its deterministic order after the filter");
        parameters.Value("@calendarName").Should().Be("50!%%", "the matcher's own text is a literal, so its wildcards are escaped");
    }

    [Test]
    public async Task SelectCalendarNames_WithEqualityMatcher_ShouldCompareWithEquals()
    {
        await adoDelegate.SelectCalendarNames(conn, new CalendarQuery { Name = CalendarNameMatcher.NameEquals("holiday") });

        command.CommandText.Should().Contain("CALENDAR_NAME = @calendarName");
        command.CommandText.Should().NotContain("CALENDAR_NAME LIKE", "an equality matcher must not fall back to LIKE");
        parameters.Value("@calendarName").Should().Be("holiday");
    }

    [Test]
    public async Task SelectCalendarNames_CountOnly_ShouldCarryTheSameFilter()
    {
        A.CallTo(command).Where(x => x.Method.Name == "ExecuteScalarAsync")
            .WithReturnType<Task<object>>()
            .Returns(Task.FromResult<object>(3));

        await adoDelegate.SelectCalendarNames(conn, new CalendarQuery
        {
            Name = CalendarNameMatcher.NameContains("day"),
            Take = 0,
            IncludeTotalCount = true
        });

        command.CommandText.Should().StartWith("SELECT COUNT(*)");
        command.CommandText.Should().Contain("CALENDAR_NAME LIKE @calendarName ESCAPE '!'",
            "a count that ignored the filter would not be the count of the page's result set");
        parameters.Value("@calendarName").Should().Be("%day%");
    }

    [Test]
    public async Task SelectJobGroups_WithName_ShouldFilterInSql()
    {
        await adoDelegate.SelectJobGroups(conn, new JobGroupQuery { Name = "reports" });

        command.CommandText.Should().Contain("JOB_GROUP = @groupName");
        command.CommandText.Should().Contain("ORDER BY JOB_GROUP", "the listing keeps its deterministic order after the filter");
        parameters.Value("@groupName").Should().Be("reports");
    }

    [Test]
    public async Task SelectTriggerGroupNames_WithNameAndPaused_ShouldFilterThePausedGroupsTable()
    {
        await adoDelegate.SelectTriggerGroups(conn, new TriggerGroupQuery { Name = "reports", Paused = true });

        command.CommandText.Should().Contain("PAUSED_TRIGGER_GRPS", "a paused listing reads the paused groups table");
        command.CommandText.Should().Contain("TRIGGER_GROUP = @groupName");
        parameters.Value("@groupName").Should().Be("reports");
    }

    [Test]
    public async Task SelectTriggerGroupNames_WithNameAndNoPausedFilter_ShouldFilterTheAliasedTriggersTable()
    {
        await adoDelegate.SelectTriggerGroups(conn, new TriggerGroupQuery { Name = "reports" });

        command.CommandText.Should().Contain("t.TRIGGER_GROUP = @groupName", "the unfiltered listing reads TRIGGERS under the alias 't'");
        parameters.Value("@groupName").Should().Be("reports");
    }

    /// <summary>
    /// Keeps the parameters the delegate binds, so a test can assert the value and not just the SQL.
    /// </summary>
    private sealed class RecordingParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> parameters = [];

        public object Value(string parameterName) => parameters.Find(x => x.ParameterName == parameterName)?.Value;

        public override int Add(object value)
        {
            parameters.Add((DbParameter) value);
            return parameters.Count - 1;
        }

        public override void AddRange(Array values)
        {
            foreach (object value in values)
            {
                Add(value);
            }
        }

        public override void Clear() => parameters.Clear();

        public override bool Contains(object value) => parameters.Contains((DbParameter) value);

        public override bool Contains(string value) => IndexOf(value) >= 0;

        public override void CopyTo(Array array, int index) => ((ICollection) parameters).CopyTo(array, index);

        public override int Count => parameters.Count;

        public override IEnumerator GetEnumerator() => parameters.GetEnumerator();

        public override int IndexOf(object value) => parameters.IndexOf((DbParameter) value);

        public override int IndexOf(string parameterName) => parameters.FindIndex(x => x.ParameterName == parameterName);

        public override void Insert(int index, object value) => parameters.Insert(index, (DbParameter) value);

        public override void Remove(object value) => parameters.Remove((DbParameter) value);

        public override void RemoveAt(int index) => parameters.RemoveAt(index);

        public override void RemoveAt(string parameterName) => parameters.RemoveAt(IndexOf(parameterName));

        public override object SyncRoot => ((ICollection) parameters).SyncRoot;

        protected override DbParameter GetParameter(int index) => parameters[index];

        protected override DbParameter GetParameter(string parameterName) => parameters[IndexOf(parameterName)];

        protected override void SetParameter(int index, DbParameter value) => parameters[index] = value;

        protected override void SetParameter(string parameterName, DbParameter value) => parameters[IndexOf(parameterName)] = value;
    }
}
