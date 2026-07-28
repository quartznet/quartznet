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
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Impl.Matchers;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// How <see cref="StdAdoDelegate" /> translates a <see cref="GroupMatcher{TKey}" /> into SQL: an
/// equality matcher must compare with '=', and anything that does become a LIKE has to have the
/// matcher's own text escaped, so a group literally named "50%" is not a wildcard.
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
        IDbProvider dbProvider = A.Fake<IDbProvider>();
        DbMetadata metadata = new()
        {
            BindByName = true,
            ParameterNamePrefix = "@"
        };
        metadata.Initialize();
        A.CallTo(() => dbProvider.Metadata).Returns(metadata);

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

        A.CallTo(() => dbProvider.CreateCommand()).Returns(command);

        adoDelegate = new StdAdoDelegate();
        adoDelegate.Initialize(new DelegateInitializationArgs
        {
            TablePrefix = "QRTZ_",
            InstanceName = "TESTSCHED",
            InstanceId = "INSTANCE",
            TypeLoadHelper = new SimpleTypeLoadHelper(),
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
    public async Task SelectTriggerGroups_WithEqualityMatcher_ShouldCompareWithEquals()
    {
        await adoDelegate.SelectTriggerGroups(conn, GroupMatcher<TriggerKey>.GroupEquals("50%"));

        command.CommandText.Should().Contain("TRIGGER_GROUP = @triggerGroup");
        command.CommandText.Should().NotContain("LIKE",
            "an equality matcher must not go through LIKE, where the group's own '%' would act as a wildcard");
        parameters.Value("@triggerGroup").Should().Be("50%", "an equality comparison binds the group name verbatim");
    }

    [Test]
    public async Task SelectTriggerGroups_WithStartsWithMatcher_ShouldEscapePercentInTheGroupName()
    {
        await adoDelegate.SelectTriggerGroups(conn, GroupMatcher<TriggerKey>.GroupStartsWith("50%"));

        command.CommandText.Should().Contain("TRIGGER_GROUP LIKE @triggerGroup ESCAPE '!'");
        parameters.Value("@triggerGroup").Should().Be("50!%%",
            "the group's own '%' is a literal and only the trailing wildcard this matcher adds stays one");
    }

    [Test]
    public async Task SelectJobsInGroup_WithContainsMatcher_ShouldEscapeUnderscoreInTheGroupName()
    {
        await adoDelegate.SelectJobsInGroup(conn, GroupMatcher<JobKey>.GroupContains("a_b"));

        command.CommandText.Should().Contain("JOB_GROUP LIKE @jobGroup ESCAPE '!'");
        parameters.Value("@jobGroup").Should().Be("%a!_b%",
            "'_' is the single-character LIKE wildcard, so a group name containing one has to be escaped");
    }

    [Test]
    public async Task SelectJobsInGroup_WithEndsWithMatcher_ShouldEscapeTheEscapeCharacterItself()
    {
        await adoDelegate.SelectJobsInGroup(conn, GroupMatcher<JobKey>.GroupEndsWith("a!b"));

        parameters.Value("@jobGroup").Should().Be("%a!!b",
            "an unescaped escape character would swallow the character after it");
    }

    [Test]
    public async Task SelectJobsInGroup_WithEqualityMatcher_ShouldCompareWithEquals()
    {
        await adoDelegate.SelectJobsInGroup(conn, GroupMatcher<JobKey>.GroupEquals("a_b"));

        command.CommandText.Should().Contain("JOB_GROUP = @jobGroup");
        command.CommandText.Should().NotContain("LIKE");
        parameters.Value("@jobGroup").Should().Be("a_b");
    }

    [Test]
    public async Task SelectTriggersInGroup_WithAnyGroup_ShouldMatchEveryGroup()
    {
        await adoDelegate.SelectTriggersInGroup(conn, GroupMatcher<TriggerKey>.AnyGroup());

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
            AdoConstants.StatePaused,
            AdoConstants.StateWaiting);

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
            AdoConstants.StatePaused,
            AdoConstants.StateWaiting);

        command.CommandText.Should().Contain("TRIGGER_GROUP LIKE @triggerGroup ESCAPE '!'");
        parameters.Value("@triggerGroup").Should().Be("50!%%");
    }

    [Test]
    public async Task UpdateTriggerGroupStateFromOtherStates_WithEqualityMatcher_ShouldCompareWithEquals()
    {
        await adoDelegate.UpdateTriggerGroupStateFromOtherStates(
            conn,
            GroupMatcher<TriggerKey>.GroupEquals("50%"),
            AdoConstants.StatePaused,
            AdoConstants.StateAcquired,
            AdoConstants.StateWaiting,
            AdoConstants.StateWaiting);

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
            AdoConstants.StatePaused,
            AdoConstants.StateAcquired,
            AdoConstants.StateWaiting,
            AdoConstants.StateWaiting);

        command.CommandText.Should().Contain("TRIGGER_GROUP LIKE @groupName ESCAPE '!'");
        parameters.Value("@groupName").Should().Be("50!%%");
    }

    [Test]
    public async Task SelectTriggerHeaders_WithStartsWithMatcher_ShouldUseLikeWithEscape()
    {
        await adoDelegate.SelectTriggerHeaders(conn, new TriggerQuery { Group = GroupMatcher<TriggerKey>.GroupStartsWith("50%") });

        command.CommandText.Should().Contain("TRIGGER_GROUP LIKE @triggerGroup ESCAPE '!'");
        parameters.Value("@triggerGroup").Should().Be("50!%%");
    }

    [Test]
    public async Task SelectJobHeaders_WithStartsWithMatcher_ShouldUseLikeWithEscape()
    {
        await adoDelegate.SelectJobHeaders(conn, new JobQuery { Group = GroupMatcher<JobKey>.GroupStartsWith("50%") });

        command.CommandText.Should().Contain("JOB_GROUP LIKE @jobGroup ESCAPE '!'");
        parameters.Value("@jobGroup").Should().Be("50!%%");
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
