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

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// The statement behind <see cref="IDriverDelegate.SelectTriggerKeysInState" />: one read that says
/// which of a set of triggers are in a state, where the caller used to ask for a state per key.
/// </summary>
/// <remarks>
/// Asserted against a command made of nothing rather than a database, because the statement has no
/// dialect variants: what is worth pinning is its text and the order its parameters are bound in. Both
/// are the same on every provider — and the order only matters on one kind of provider, which is why
/// nothing else would ever notice it being wrong. A provider a caller describes themselves with
/// <c>BindByName</c> off takes its parameters positionally, and this statement binds three kinds of
/// string in a row.
/// </remarks>
public class StdAdoDelegateTriggerKeysInStateTest
{
    private StdAdoDelegate adoDelegate = null!;
    private FakeCommand command = null!;
    private IDbProvider dbProvider = null!;
    private ConnectionAndTransactionHolder conn = null!;

    [SetUp]
    public void SetUp()
    {
        command = new FakeCommand();

        dbProvider = A.Fake<IDbProvider>();
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
    public async Task SelectTriggerKeysInState_AsksForTheKeysOfTheGivenTriggersThatAreInTheState()
    {
        InstallEmptyReader();

        await adoDelegate.SelectTriggerKeysInState(
            conn,
            [new TriggerKey("t-1", "tg"), new TriggerKey("t-2", "tg")],
            StoredTriggerState.Complete);

        command.CommandText.Should().Be(
            "SELECT TRIGGER_NAME, TRIGGER_GROUP FROM QRTZ_TRIGGERS WHERE SCHED_NAME = @schedulerName "
            + "AND TRIGGER_STATE = @state "
            + "AND ((TRIGGER_NAME = @tkn000 AND TRIGGER_GROUP = @tkg000) OR (TRIGGER_NAME = @tkn001 AND TRIGGER_GROUP = @tkg001))",
            "the whole set is one statement: the state the caller is asking about, and the keys as the "
            + "disjunction every key-set predicate in this delegate is built as");
    }

    [Test]
    public async Task SelectTriggerKeysInState_BindsItsParametersInTheOrderTheStatementNamesThem()
    {
        InstallEmptyReader();

        await adoDelegate.SelectTriggerKeysInState(
            conn,
            [new TriggerKey("t-1", "tg"), new TriggerKey("t-2", "tg"), new TriggerKey("t-3", "other")],
            StoredTriggerState.Complete);

        BoundValues().Should().Equal(
            ["TESTSCHED", "COMPLETE", "t-1", "tg", "t-2", "tg", "t-3", "other", "t-3", "other"],
            "the statement names the scheduler, then the state, then the keys, and a provider binding "
            + "positionally would otherwise look for a trigger in a state called 'TESTSCHED' - and the "
            + "three keys are padded to the bucket of four by repeating the last of them, which a "
            + "disjunction cannot be changed by");
    }

    [Test]
    public async Task SelectTriggerKeysInState_AsksNothingWhenThereAreNoKeysToAskAbout()
    {
        List<TriggerKey> keys = await adoDelegate.SelectTriggerKeysInState(conn, [], StoredTriggerState.Complete);

        keys.Should().BeEmpty();
        A.CallTo(() => dbProvider.CreateCommand()).MustNotHaveHappened();
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
