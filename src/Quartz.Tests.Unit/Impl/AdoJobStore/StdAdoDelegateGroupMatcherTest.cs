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

    /// <summary>
    /// Builds a delegate that hands out a fresh command per statement, so a member that issues several
    /// can be asserted on all of them rather than only on whichever wrote last.
    /// </summary>
    private static StdAdoDelegate CreateDelegateRecordingEveryCommand(List<StubCommand> issued, int scalar = 0)
    {
        IDbProvider dbProvider = A.Fake<IDbProvider>();
        A.CallTo(() => dbProvider.Metadata).Returns(new DbMetadata
        {
            BindByName = true,
            ParameterNamePrefix = "@"
        });

        A.CallTo(() => dbProvider.CreateCommand()).ReturnsLazily(() =>
        {
            StubCommand fresh = A.Fake<StubCommand>();

            A.CallTo(fresh).Where(x => x.Method.Name == "get_DbParameterCollection")
                .WithReturnType<DbParameterCollection>()
                .Returns(new RecordingParameterCollection());

            A.CallTo(fresh).Where(x => x.Method.Name == "CreateDbParameter")
                .WithReturnType<DbParameter>()
                .ReturnsLazily(() => new SqlParameter());

            // A count statement reads its answer with Convert.ToInt32, which a bare fake's dummy
            // object cannot satisfy.
            A.CallTo(fresh).Where(x => x.Method.Name == "ExecuteScalarAsync")
                .WithReturnType<Task<object>>()
                .Returns(Task.FromResult<object>(scalar));

            issued.Add(fresh);
            return fresh;
        });

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

    [Test]
    public async Task SelectFireInstances_BuildsExpectedSql()
    {
        await adoDelegate.SelectFireInstances(conn, new FireInstanceQuery());

        string expectedCommandText = "SELECT ENTRY_ID, TRIGGER_NAME, TRIGGER_GROUP, JOB_NAME, JOB_GROUP, "
                                     + "INSTANCE_NAME, STATE, FIRED_TIME, SCHED_TIME, EXECUTION_GROUP "
                                     + "FROM QRTZ_FIRED_TRIGGERS "
                                     + "WHERE SCHED_NAME = @schedulerName "
                                     + "AND STATE <> @entryState "
                                     + "ORDER BY TRIGGER_GROUP, TRIGGER_NAME, ENTRY_ID "
                                     + "OFFSET @pageSkip ROWS FETCH NEXT @pageTake ROWS ONLY";
        command.CommandText.Should().Be(expectedCommandText);

        // The entry id is in the ORDER BY because trigger group and name do not identify a firing —
        // without it a page boundary between two firings of one trigger is arbitrary.
        parameters.Value("@entryState").Should().Be(AdoConstants.StateAcquired,
            "the default listing is everything that is not merely reserved");
        parameters.Value("@pageTake").Should().Be(PagedQuery.DefaultTake + 1,
            "one row past the page is what HasMore reads");
    }

    [Test]
    public async Task SelectFireInstances_FilteringByAcquired_ComparesAgainstTheSameStoredState()
    {
        await adoDelegate.SelectFireInstances(conn, new FireInstanceQuery { State = FireInstanceState.Acquired });

        command.CommandText.Should().Contain("AND STATE = @entryState");
        command.CommandText.Should().NotContain("STATE <> ");
        parameters.Value("@entryState").Should().Be(AdoConstants.StateAcquired);
    }

    [Test]
    public async Task SelectFireInstances_WithoutAStateFilter_ListsEveryState()
    {
        await adoDelegate.SelectFireInstances(conn, new FireInstanceQuery { State = null });

        command.CommandText.Should().NotContain("@entryState", "a null state filter is no predicate at all");
        parameters.Value("@entryState").Should().BeNull();
    }

    [Test]
    public async Task SelectFireInstances_CarriesEveryFilterItWasGiven()
    {
        await adoDelegate.SelectFireInstances(conn, new FireInstanceQuery
        {
            TriggerGroup = GroupMatcher<TriggerKey>.GroupStartsWith("50%"),
            TriggerName = NameMatcher<TriggerKey>.NameEquals("trigger1"),
            Job = new JobKey("job1", "jobGroup1"),
            SchedulerInstanceId = "node-2"
        });

        command.CommandText.Should().Contain("AND TRIGGER_GROUP LIKE @triggerGroup ESCAPE '!'");
        command.CommandText.Should().Contain("AND TRIGGER_NAME = @triggerName");
        command.CommandText.Should().Contain("AND JOB_NAME = @jobName AND JOB_GROUP = @jobGroup");
        command.CommandText.Should().Contain("AND INSTANCE_NAME = @instanceName");

        parameters.Value("@triggerGroup").Should().Be("50!%%", "a group literally named 50% is not a wildcard");
        parameters.Value("@triggerName").Should().Be("trigger1");
        parameters.Value("@jobName").Should().Be("job1");
        parameters.Value("@instanceName").Should().Be("node-2");
    }

    [Test]
    public async Task SelectFireInstances_CountOnly_ShouldCarryTheSameFilter()
    {
        A.CallTo(command)
            .Where(x => x.Method.Name == "ExecuteScalarAsync")
            .WithReturnType<Task<object>>()
            .Returns(Task.FromResult<object>(3));

        PagedResult<FireInstance> result = await adoDelegate.SelectFireInstances(
            conn,
            new FireInstanceQuery { SchedulerInstanceId = "node-2", Take = 0, IncludeTotalCount = true });

        result.Items.Should().BeEmpty("the count idiom reads no page");
        result.TotalCount.Should().Be(3);

        command.CommandText.Should().StartWith("SELECT COUNT(*) FROM QRTZ_FIRED_TRIGGERS");
        command.CommandText.Should().Contain("AND INSTANCE_NAME = @instanceName",
            "a count that filtered differently from the page would report a total for another question");
        command.CommandText.Should().NotContain("ORDER BY", "nothing is ordered when nothing is returned");
    }

    /// <summary>
    /// The projection and <c>ReadFireInstance</c> agree on where each column is: the reader takes them
    /// from fixed ordinals, so a column added to the SELECT list would silently shift every one after it.
    /// </summary>
    [Test]
    public async Task SelectFireInstances_ReadsEveryColumnFromTheProjection()
    {
        InstallFireInstanceReader(AdoConstants.StateExecuting);

        PagedResult<FireInstance> result = await adoDelegate.SelectFireInstances(conn, new FireInstanceQuery());

        FireInstance instance = result.Items.Should().ContainSingle().Subject;
        instance.FireInstanceId.Should().Be("entry1");
        instance.TriggerKey.Should().Be(new TriggerKey("trigger1", "group1"));
        instance.JobKey.Should().Be(new JobKey("job1", "jobGroup1"));
        instance.SchedulerInstanceId.Should().Be("node-1");
        instance.State.Should().Be(FireInstanceState.Executing);
        instance.ExecutionGroup.Should().Be("executionGroup");
    }

    [Test]
    public async Task SelectFireInstances_ReadsAReservationWithoutItsJob()
    {
        InstallFireInstanceReader(AdoConstants.StateAcquired);

        PagedResult<FireInstance> result = await adoDelegate.SelectFireInstances(conn, new FireInstanceQuery { State = null });

        FireInstance instance = result.Items.Should().ContainSingle().Subject;
        instance.State.Should().Be(FireInstanceState.Acquired);
        instance.JobKey.Should().BeNull("the job columns of a reservation hold nothing yet");
    }

    [Test]
    public async Task InsertFiredTrigger_WritesTheExecutionGroup()
    {
        IOperableTrigger trigger = FireInstanceTrigger("reports");

        await adoDelegate.InsertFiredTrigger(conn, trigger, StoredTriggerState.Acquired, job: null);

        command.CommandText.Should().Contain("EXECUTION_GROUP");
        command.CommandText.Should().Contain("@triggerExecutionGroup");
        parameters.Value("@triggerExecutionGroup").Should().Be("reports",
            "the column was dead schema until the listing needed to read it back");
    }

    [Test]
    public async Task UpdateFiredTrigger_WritesTheExecutionGroup()
    {
        IOperableTrigger trigger = FireInstanceTrigger(executionGroup: null);
        IJobDetail job = JobBuilder.Create<FireInstanceTestJob>().WithIdentity("job1", "jobGroup1").Build();

        await adoDelegate.UpdateFiredTrigger(conn, trigger, StoredTriggerState.Executing, job);

        command.CommandText.Should().Contain("EXECUTION_GROUP = @executionGroup");
        parameters.Value("@executionGroup").Should().Be(DBNull.Value,
            "a trigger with no execution group writes a null rather than leaving the column stale");
    }

    private sealed class FireInstanceTestJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    private static IOperableTrigger FireInstanceTrigger(string executionGroup)
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity("trigger1", "group1")
            .ForJob("job1", "jobGroup1")
            .WithExecutionGroup(executionGroup)
            .StartNow()
            .Build();

        trigger.FireInstanceId = "entry1";
        return trigger;
    }

    /// <summary>
    /// Fakes one fire-instance row. The values are positional on purpose: this is what pins the
    /// projection's column order to the reader's ordinals.
    /// </summary>
    private void InstallFireInstanceReader(string entryState)
    {
        var strings = new Dictionary<int, string>
        {
            [0] = "entry1",
            [1] = "trigger1",
            [2] = "group1",
            [3] = "job1",
            [4] = "jobGroup1",
            [5] = "node-1",
            [6] = entryState,
            [9] = "executionGroup"
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

        // The two timestamps come back as DBNull so the reader's own null handling applies.
        A.CallTo(() => reader.GetValue(A<int>._)).ReturnsLazily((int _) => DBNull.Value);

        A.CallTo(command)
            .Where(x => x.Method.Name == "ExecuteDbDataReaderAsync")
            .WithReturnType<Task<DbDataReader>>()
            .Returns(Task.FromResult(reader));
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

        command.CommandText.Should().Contain("j.JOB_GROUP = @groupName", "the unfiltered listing reads JOB_DETAILS under the alias 'j'");
        command.CommandText.Should().Contain("ORDER BY j.JOB_GROUP", "the listing keeps its deterministic order after the filter");
        parameters.Value("@groupName").Should().Be("reports");
    }

    [Test]
    public async Task SelectJobGroups_WithNameAndPaused_ShouldFilterThePausedGroupsTable()
    {
        await adoDelegate.SelectJobGroups(conn, new JobGroupQuery { Name = "reports", Paused = true });

        command.CommandText.Should().Contain("PAUSED_JOB_GRPS", "a paused listing reads the paused groups table");
        command.CommandText.Should().NotContain("JOB_DETAILS",
            "a group paused while it holds no jobs has no row in JOB_DETAILS, so reading it would lose the group");
        command.CommandText.Should().Contain("JOB_GROUP = @groupName");
        parameters.Value("@groupName").Should().Be("reports");
    }

    [Test]
    public async Task SelectJobGroups_WithoutPausedFilter_ShouldProjectThePausedFlag()
    {
        await adoDelegate.SelectJobGroups(conn, new JobGroupQuery());

        command.CommandText.Should().Contain("CASE WHEN EXISTS", "an unfiltered listing reports each group's paused state");
        command.CommandText.Should().Contain("PAUSED_JOB_GRPS");
        command.CommandText.Should().Contain("pg.SCHED_NAME = j.SCHED_NAME",
            "the subquery correlates on the outer column rather than binding @schedulerName twice, "
            + "because a provider that adapts named parameters positionally would then find one placeholder too many");
    }

    [Test]
    public async Task SelectJobGroups_UnpausedOnly_ShouldExcludeThePausedGroups()
    {
        await adoDelegate.SelectJobGroups(conn, new JobGroupQuery { Paused = false });

        command.CommandText.Should().Contain("NOT EXISTS", "the unpaused listing is the complement of the paused one");
        command.CommandText.Should().Contain("PAUSED_JOB_GRPS");
    }

    [Test]
    public async Task SelectJobGroups_ShouldReadThePausedFlagFromTheSecondColumn()
    {
        GivenOneRow("reports", isPaused: 1);

        PagedResult<JobGroup> result = await adoDelegate.SelectJobGroups(conn, new JobGroupQuery());

        result.Items.Should().ContainSingle().Which.Should().Be(new JobGroup("reports", Paused: true),
            "the unfiltered listing projects the flag beside the name, and reads it back from that position");
    }

    [Test]
    public async Task SelectJobGroups_PausedOnly_ShouldNotNeedTheFlagColumn()
    {
        GivenOneRow("reports", isPaused: null);

        PagedResult<JobGroup> result = await adoDelegate.SelectJobGroups(conn, new JobGroupQuery { Paused = true });

        result.Items.Should().ContainSingle().Which.Should().Be(new JobGroup("reports", Paused: true),
            "every row of the paused-groups table is a paused group, so the statement selects no flag to read");
    }

    [Test]
    public async Task SelectJobGroups_CountOnly_ShouldCountThePausedGroupsTable()
    {
        GivenScalar(3);

        PagedResult<JobGroup> result = await adoDelegate.SelectJobGroups(conn, new JobGroupQuery
        {
            Paused = true,
            Name = "reports",
            Take = 0,
            IncludeTotalCount = true
        });

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(3);
        command.CommandText.Should().StartWith("SELECT COUNT(*)");
        command.CommandText.Should().Contain("PAUSED_JOB_GRPS");
        command.CommandText.Should().Contain("JOB_GROUP = @groupName",
            "a count that ignored the filter would not be the count of the page's result set");
    }

    [Test]
    public async Task SelectJobGroups_WithTotalCount_ShouldCountTheSameSetItListed()
    {
        List<StubCommand> issued = [];
        StdAdoDelegate recording = CreateDelegateRecordingEveryCommand(issued, scalar: 2);

        PagedResult<JobGroup> result = await recording.SelectJobGroups(
            conn, new JobGroupQuery { Paused = false, IncludeTotalCount = true });

        result.TotalCount.Should().Be(2);
        issued.Should().HaveCount(2, "one statement lists the page and a second counts the whole set");
        issued[1].CommandText.Should().StartWith("SELECT COUNT(");
        issued[1].CommandText.Should().Contain("NOT EXISTS",
            "the count carries the same exclusion the listing did, or the two disagree");
    }

    [Test]
    public async Task InsertPausedJobGroup_ShouldWriteTheGroupToThePausedJobGroupsTable()
    {
        await adoDelegate.InsertPausedJobGroup(conn, "reports");

        command.CommandText.Should().Contain("INSERT INTO QRTZ_PAUSED_JOB_GRPS");
        command.CommandText.Should().Contain("SCHED_NAME, JOB_GROUP");
        parameters.Value("@schedulerName").Should().Be("TESTSCHED",
            "the row is scoped to one scheduler, like every other row Quartz writes");
        parameters.Value("@jobGroup").Should().Be("reports");
    }

    [Test]
    public async Task IsJobGroupPaused_ShouldAskThePausedJobGroupsTableForTheOneGroup()
    {
        await adoDelegate.IsJobGroupPaused(conn, "reports");

        command.CommandText.Should().Contain("FROM QRTZ_PAUSED_JOB_GRPS");
        command.CommandText.Should().Contain("JOB_GROUP = @jobGroup",
            "the check that guards the insert asks about one group rather than listing every paused one");
        parameters.Value("@jobGroup").Should().Be("reports");
    }

    [Test]
    public async Task ClearData_ShouldEmptyThePausedJobGroupsTableToo()
    {
        List<StubCommand> issued = [];
        StdAdoDelegate recording = CreateDelegateRecordingEveryCommand(issued);

        await recording.ClearData(conn);

        List<string> statements = issued.ConvertAll(x => x.CommandText);
        statements.Should().Contain(x => x.Contains("DELETE FROM QRTZ_PAUSED_JOB_GRPS"),
            "a clear that left paused job groups behind would pause whatever was scheduled into them next");
        statements.Should().Contain(x => x.Contains("DELETE FROM QRTZ_PAUSED_TRIGGER_GRPS"),
            "and the trigger groups it always cleared are still cleared");
    }

    [Test]
    public async Task ValidateSchema_ShouldRequireThePausedJobGroupsTable()
    {
        List<StubCommand> issued = [];
        StdAdoDelegate recording = CreateDelegateRecordingEveryCommand(issued);

        int tableCount = await recording.ValidateSchema(conn);

        tableCount.Should().Be(AdoConstants.AllTableNames.Length);
        issued.ConvertAll(x => x.CommandText).Should().Contain(x => x.Contains("FROM QRTZ_PAUSED_JOB_GRPS"),
            "4.x has no schema probes, so a database missing the table has to fail at startup rather "
            + "than silently forget every job group pause");
    }

    /// <summary>
    /// Arranges the shared stub to answer one listing row.
    /// </summary>
    /// <param name="isPaused">
    /// The paused flag the row carries, or null for the statements that select no flag column.
    /// </param>
    private void GivenOneRow(string groupName, int? isPaused)
    {
        DbDataReader reader = A.Fake<DbDataReader>();
        bool read = false;
        A.CallTo(() => reader.ReadAsync(A<CancellationToken>._)).ReturnsLazily(() =>
        {
            bool first = !read;
            read = true;
            return first;
        });
        A.CallTo(() => reader.GetString(0)).Returns(groupName);
        if (isPaused is not null)
        {
            A.CallTo(() => reader.GetValue(1)).Returns(isPaused.Value);
        }

        A.CallTo(command).Where(x => x.Method.Name == "ExecuteDbDataReaderAsync")
            .WithReturnType<Task<DbDataReader>>()
            .Returns(Task.FromResult(reader));
    }

    private void GivenScalar(object value)
    {
        A.CallTo(command).Where(x => x.Method.Name == "ExecuteScalarAsync")
            .WithReturnType<Task<object>>()
            .Returns(Task.FromResult(value));
    }

    [Test]
    public async Task DeletePausedJobGroup_WithPrefixMatcher_ShouldUseLikeWithEscape()
    {
        await adoDelegate.DeletePausedJobGroup(conn, GroupMatcher<JobKey>.GroupStartsWith("50%"));

        command.CommandText.Should().Contain("JOB_GROUP LIKE @jobGroup ESCAPE '!'");
        parameters.Value("@jobGroup").Should().Be("50!%%", "the matcher's own text is a literal, so its wildcards are escaped");
    }

    [Test]
    public async Task DeletePausedJobGroup_WithEqualityMatcher_ShouldCompareWithEquals()
    {
        await adoDelegate.DeletePausedJobGroup(conn, GroupMatcher<JobKey>.GroupEquals("reports"));

        command.CommandText.Should().Contain("JOB_GROUP = @jobGroup");
        command.CommandText.Should().NotContain("JOB_GROUP LIKE", "an equality matcher must not fall back to LIKE");
        parameters.Value("@jobGroup").Should().Be("reports");
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
