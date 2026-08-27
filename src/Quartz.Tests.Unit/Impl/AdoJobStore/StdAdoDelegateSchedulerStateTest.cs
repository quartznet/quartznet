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
