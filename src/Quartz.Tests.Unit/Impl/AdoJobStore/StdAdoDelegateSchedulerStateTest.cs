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
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

using FakeItEasy;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Simpl;

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
    private StdAdoDelegate adoDelegate;
    private RecordingCommand command;
    private ConnectionAndTransactionHolder conn;

    [SetUp]
    public void SetUp()
    {
        command = new RecordingCommand();

        IDbProvider dbProvider = A.Fake<IDbProvider>();
        A.CallTo(() => dbProvider.Metadata).Returns(new DbMetadata());
        A.CallTo(() => dbProvider.CreateCommand()).Returns(command);

        adoDelegate = new StdAdoDelegate();
        adoDelegate.Initialize(new DelegateInitializationArgs
        {
            TablePrefix = "QRTZ_",
            InstanceName = "TESTSCHED",
            InstanceId = "node-1",
            TypeLoadHelper = new SimpleTypeLoadHelper(),
            DbProvider = dbProvider,
            ObjectSerializer = new SystemTextJsonObjectSerializer(),
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

        BoundValues().Should().Equal(new object[] { "TESTSCHED", "node-1" },
            "SqlSelectSchedulerState names @schedulerName before @instanceName, so a provider that binds "
            + "positionally would otherwise look for a scheduler called 'node-1' on a node called 'TESTSCHED'");
    }

    [Test]
    public async Task SelectSchedulerStateRecords_BindsOnlyTheSchedulerWhenEveryNodeIsAskedFor()
    {
        InstallEmptyReader();

        await adoDelegate.SelectSchedulerStateRecords(conn, null);

        BoundValues().Should().Equal(new object[] { "TESTSCHED" },
            "SqlSelectSchedulerStates has no instance predicate, so binding one would leave the command a parameter over");
    }

    private List<object> BoundValues()
    {
        List<object> values = new List<object>();
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

    /// <summary>
    /// A command that keeps its parameters in the order they were added, which is all this fixture
    /// looks at.
    /// </summary>
    private sealed class RecordingCommand : DbCommand
    {
        /// <summary>
        /// The result set the next execution hands back. Left unset, executing throws — a command made
        /// of nothing has nothing to return.
        /// </summary>
        public DbDataReader Reader { get; set; }

        public override string CommandText { get; set; } = "";

        public override int CommandTimeout { get; set; }

        public override CommandType CommandType { get; set; }

        public override bool DesignTimeVisible { get; set; }

        public override UpdateRowSource UpdatedRowSource { get; set; }

        protected override DbConnection DbConnection { get; set; }

        protected override DbParameterCollection DbParameterCollection { get; } = new RecordingParameterCollection();

        protected override DbTransaction DbTransaction { get; set; }

        public override void Cancel()
        {
        }

        public override int ExecuteNonQuery() => throw new NotSupportedException();

        public override object ExecuteScalar() => throw new NotSupportedException();

        public override void Prepare()
        {
        }

        protected override DbParameter CreateDbParameter() => new RecordingParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            => Reader ?? throw new NotSupportedException();
    }

    private sealed class RecordingParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> parameters = new List<DbParameter>();

        public override int Count => parameters.Count;

        public override object SyncRoot { get; } = new object();

#if NETFRAMEWORK
        public override bool IsFixedSize => false;

        public override bool IsReadOnly => false;

        public override bool IsSynchronized => false;
#endif

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

        public override IEnumerator GetEnumerator() => parameters.GetEnumerator();

        public override int IndexOf(object value) => parameters.IndexOf((DbParameter) value);

        public override int IndexOf(string parameterName) => parameters.FindIndex(p => p.ParameterName == parameterName);

        public override void Insert(int index, object value) => parameters.Insert(index, (DbParameter) value);

        public override void Remove(object value) => parameters.Remove((DbParameter) value);

        public override void RemoveAt(int index) => parameters.RemoveAt(index);

        public override void RemoveAt(string parameterName) => RemoveAt(IndexOf(parameterName));

        protected override DbParameter GetParameter(int index) => parameters[index];

        protected override DbParameter GetParameter(string parameterName) => GetParameter(IndexOf(parameterName));

        protected override void SetParameter(int index, DbParameter value) => parameters[index] = value;

        protected override void SetParameter(string parameterName, DbParameter value) => SetParameter(IndexOf(parameterName), value);
    }

    private sealed class RecordingParameter : DbParameter
    {
        public override DbType DbType { get; set; }

        public override ParameterDirection Direction { get; set; }

        public override bool IsNullable { get; set; }

        public override string ParameterName { get; set; } = "";

        public override int Size { get; set; }

        public override string SourceColumn { get; set; } = "";

        public override bool SourceColumnNullMapping { get; set; }

        public override object Value { get; set; }

#if NETFRAMEWORK
        public override DataRowVersion SourceVersion { get; set; }
#endif

        public override void ResetDbType()
        {
        }
    }
}
