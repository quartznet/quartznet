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
using System.Runtime.Serialization;

using FakeItEasy;

using Microsoft.Data.SqlClient;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Simpl;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <author>Marko Lahma (.NET)</author>
[TestFixture(typeof(NewtonsoftJsonObjectSerializer))]
[TestFixture(typeof(SystemTextJsonObjectSerializer))]
public class StdAdoDelegateTest
{
    private readonly IObjectSerializer serializer;

    public StdAdoDelegateTest(Type serializerType)
    {
        serializer = (IObjectSerializer) Activator.CreateInstance(serializerType);
        serializer.Initialize();
    }

    [Test]
    public void TestSerializeJobData()
    {
        var args = new DelegateInitializationArgs();
        args.TablePrefix = "QRTZ_";
        args.InstanceName = "TESTSCHED";
        args.InstanceId = "INSTANCE";
        args.DbProvider = new DbProvider(TestConstants.DefaultSqlServerProvider, "");
        args.TypeLoadHelper = new SimpleTypeLoadHelper();
        args.ObjectSerializer = serializer;

        var del = new StdAdoDelegate();
        del.Initialize(args);

        var jdm = new JobDataMap();
        del.SerializeJobData(jdm);

        jdm.Clear();
        jdm.Put("key", "value");
        jdm.Put("key2", null);
        del.SerializeJobData(jdm);

        jdm.Clear();
        jdm.Put("key1", "value");
        jdm.Put("key2", null);
        jdm.Put("key3", new NonSerializableTestClass());

        try
        {
            del.SerializeJobData(jdm);
        }
        catch (SerializationException e)
        {
            Assert.Fail($"Private types should be serializable when not using binary serialization: {e}");
        }
    }

    private class NonSerializableTestClass;

    [Test]
    public async Task TestSelectBlobTriggerWithNoBlobContent()
    {
        var dbProvider = A.Fake<IDbProvider>();
        var connection = A.Fake<DbConnection>();
        var transaction = A.Fake<DbTransaction>();
        var command = (DbCommand) A.Fake<StubCommand>();
        var dbMetadata = new DbMetadata();
        A.CallTo(() => dbProvider.Metadata).Returns(dbMetadata);

        A.CallTo(() => dbProvider.CreateCommand()).Returns(command);

        var dataReader = A.Fake<DbDataReader>();
        A.CallTo(command).Where(x => x.Method.Name == "ExecuteDbDataReaderAsync")
            .WithReturnType<Task<DbDataReader>>()
            .Returns(dataReader);

        A.CallTo(command).Where(x => x.Method.Name == "get_DbParameterCollection")
            .WithReturnType<DbParameterCollection>()
            .Returns(new StubParameterCollection());

        A.CallTo(() => command.CommandText).Returns("");

        A.CallTo(command).Where(x => x.Method.Name == "CreateDbParameter")
            .WithReturnType<DbParameter>()
            .Returns(new SqlParameter());

        var adoDelegate = new StdAdoDelegate();

        var delegateInitializationArgs = new DelegateInitializationArgs
        {
            TablePrefix = "QRTZ_",
            InstanceId = "TESTSCHED",
            InstanceName = "INSTANCE",
            TypeLoadHelper = new SimpleTypeLoadHelper(),
            UseProperties = false,
            InitString = "",
            DbProvider = dbProvider
        };
        adoDelegate.Initialize(delegateInitializationArgs);

        var conn = new ConnectionAndTransactionHolder(connection, transaction);

        // First result set has results, second has none
        A.CallTo(() => dataReader.ReadAsync(CancellationToken.None)).Returns(true).Once();
        A.CallTo(() => dataReader.ReadAsync(CancellationToken.None)).Returns(false);
        A.CallTo(() => dataReader[AdoConstants.ColumnTriggerType]).Returns(AdoConstants.TriggerTypeBlob);

        IOperableTrigger trigger = await adoDelegate.SelectTrigger(conn, new TriggerKey("test"));
        Assert.That(trigger, Is.Null);
    }

    [Test]
    public async Task TestSelectSimpleTriggerWithExceptionWithExtendedProps()
    {
        var dbProvider = A.Fake<IDbProvider>();
        var connection = A.Fake<DbConnection>();
        var transaction = A.Fake<DbTransaction>();
        var command = (DbCommand) A.Fake<StubCommand>();
        var dbMetadata = new DbMetadata();
        A.CallTo(() => dbProvider.Metadata).Returns(dbMetadata);

        A.CallTo(() => dbProvider.CreateCommand()).Returns(command);

        var dataReader = A.Fake<DbDataReader>();

        A.CallTo(command).Where(x => x.Method.Name == "ExecuteDbDataReaderAsync")
            .WithReturnType<Task<DbDataReader>>()
            .Returns(Task.FromResult(dataReader));

        A.CallTo(command).Where(x => x.Method.Name == "get_DbParameterCollection")
            .WithReturnType<DbParameterCollection>()
            .Returns(new StubParameterCollection());

        A.CallTo(() => command.CommandText).Returns("");

        A.CallTo(command).Where(x => x.Method.Name == "CreateDbParameter")
            .WithReturnType<DbParameter>()
            .Returns(new SqlParameter());

        var persistenceDelegate = A.Fake<ITriggerPersistenceDelegate>();
        var exception = new InvalidOperationException();
        A.CallTo(() => persistenceDelegate.LoadExtendedTriggerProperties(A<ConnectionAndTransactionHolder>.Ignored, A<TriggerKey>.Ignored, CancellationToken.None)).Throws(exception);

        StdAdoDelegate adoDelegate = new TestStdAdoDelegate(persistenceDelegate);

        var delegateInitializationArgs = new DelegateInitializationArgs
        {
            TablePrefix = "QRTZ_",
            InstanceId = "TESTSCHED",
            InstanceName = "INSTANCE",
            TypeLoadHelper = new SimpleTypeLoadHelper(),
            UseProperties = false,
            InitString = "",
            DbProvider = dbProvider
        };
        adoDelegate.Initialize(delegateInitializationArgs);

        // Mock basic trigger data
        A.CallTo(() => dataReader.ReadAsync(CancellationToken.None)).Returns(true);
        A.CallTo(() => dataReader[AdoConstants.ColumnTriggerType]).Returns(AdoConstants.TriggerTypeSimple);
        A.CallTo(() => dataReader[A<string>._]).Returns("1");

        try
        {
            var conn = new ConnectionAndTransactionHolder(connection, transaction);
            await adoDelegate.SelectTrigger(conn, new TriggerKey("test"));
            Assert.Fail("Trigger selection should result in exception");
        }
        catch (InvalidOperationException e)
        {
            Assert.That(e, Is.SameAs(exception));
        }

        A.CallTo(() => persistenceDelegate.LoadExtendedTriggerProperties(A<ConnectionAndTransactionHolder>.Ignored, A<TriggerKey>.Ignored, CancellationToken.None)).MustHaveHappened();
    }

    [Test]
    public async Task TestSelectJobDetail()
    {
        var connection = A.Fake<DbConnection>();
        var transaction = A.Fake<DbTransaction>();
        var conn = new ConnectionAndTransactionHolder(connection, transaction);

        var dataReader = A.Fake<DbDataReader>();
        A.CallTo(() => dataReader.ReadAsync(CancellationToken.None))
            .Returns(true)
            .Once();

        var jobName = $"TestJobName-{Guid.NewGuid()}";
        A.CallTo(() => dataReader[AdoConstants.ColumnJobName])
            .Returns(jobName);
        var jobGroup = $"TestGroup-{Guid.NewGuid()}";
        A.CallTo(() => dataReader[AdoConstants.ColumnJobGroup])
            .Returns(jobGroup);
        var jobDescription = $"TestDescription-{Guid.NewGuid()}";
        A.CallTo(() => dataReader[AdoConstants.ColumnDescription])
            .Returns(jobDescription);
        A.CallTo(() => dataReader[AdoConstants.ColumnJobClass])
            .Returns(typeof(TestJob).AssemblyQualifiedNameWithoutVersion());
        A.CallTo(() => dataReader[AdoConstants.ColumnRequestsRecovery])
            .Returns(true);
        A.CallTo(() => dataReader[AdoConstants.ColumnIsDurable])
            .Returns(true);
        A.CallTo(() => dataReader[AdoConstants.ColumnIsNonConcurrent])
            .Returns(true);
        A.CallTo(() => dataReader[AdoConstants.ColumnIsUpdateData])
            .Returns(true);

        var command = A.Fake<StubCommand>();

        A.CallTo(command)
            .Where(x => x.Method.Name == "ExecuteDbDataReaderAsync")
            .WithReturnType<Task<DbDataReader>>()
            .Returns(Task.FromResult(dataReader));

        var dbProvider = A.Fake<IDbProvider>();
        A.CallTo(() => dbProvider.CreateCommand())
            .Returns(command);

        var dbMetadata = new DbMetadata
        {
            BindByName = true,
            ParameterNamePrefix = "@"
        };
        dbMetadata.Init();
        A.CallTo(() => dbProvider.Metadata)
            .Returns(dbMetadata);

        var delegateInitializationArgs = new DelegateInitializationArgs
        {
            TablePrefix = "QRTZ_",
            InstanceId = "TESTSCHED",
            InstanceName = "INSTANCE",
            TypeLoadHelper = new SimpleTypeLoadHelper(),
            UseProperties = false,
            InitString = "",
            DbProvider = dbProvider
        };

        var adoDelegate = new StdAdoDelegate();
        adoDelegate.Initialize(delegateInitializationArgs);

        var jobKey = new JobKey(jobName, jobGroup);

        var jobDetail = await adoDelegate.SelectJobDetail(
            conn,
            jobKey,
            new SimpleTypeLoadHelper(), // Irrelevant, not used actually by method implementation
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(jobDetail, Is.Not.Null);
            Assert.That(jobDetail.Key.Name, Is.EqualTo(jobName));
            Assert.That(jobDetail.Key.Group, Is.EqualTo(jobGroup));
            Assert.That(jobDetail.Description, Is.EqualTo(jobDescription));
            Assert.That(jobDetail.JobType.Type, Is.EqualTo(typeof(TestJob)));
            Assert.That(jobDetail.RequestsRecovery, Is.True);
            Assert.That(jobDetail.Durable, Is.True);
            Assert.That(jobDetail.ConcurrentExecutionDisallowed, Is.True);
        });

        var expectedCommandText = "SELECT "
                                  + "JOB_NAME,"
                                  + "JOB_GROUP,"
                                  + "DESCRIPTION,"
                                  + "JOB_CLASS_NAME,"
                                  + "IS_DURABLE,"
                                  + "REQUESTS_RECOVERY,"
                                  + "JOB_DATA,"
                                  + "IS_NONCONCURRENT,"
                                  + "IS_UPDATE_DATA "
                                  + "FROM QRTZ_JOB_DETAILS "
                                  + "WHERE SCHED_NAME = @schedulerName "
                                  + "AND JOB_NAME = @jobName "
                                  + "AND JOB_GROUP = @jobGroup";
        Assert.That(command.CommandText, Is.EqualTo(expectedCommandText));
    }

    private class TestJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context) => throw new NotSupportedException();
    }

    [Test]
    public async Task TestSelectSimpleTriggerWithDeleteBeforeSelectExtendedProps()
    {
        var dbProvider = A.Fake<IDbProvider>();
        var connection = A.Fake<DbConnection>();
        var transaction = A.Fake<DbTransaction>();
        var command = (DbCommand) A.Fake<StubCommand>();
        var dbMetadata = new DbMetadata();
        A.CallTo(() => dbProvider.Metadata).Returns(dbMetadata);

        A.CallTo(() => dbProvider.CreateCommand()).Returns(command);

        var dataReader = A.Fake<DbDataReader>();

        A.CallTo(command).Where(x => x.Method.Name == "ExecuteDbDataReaderAsync")
            .WithReturnType<Task<DbDataReader>>()
            .Returns(Task.FromResult(dataReader));

        A.CallTo(command).Where(x => x.Method.Name == "get_DbParameterCollection")
            .WithReturnType<DbParameterCollection>()
            .Returns(new StubParameterCollection());

        A.CallTo(() => command.CommandText).Returns("");

        A.CallTo(command).Where(x => x.Method.Name == "CreateDbParameter")
            .WithReturnType<DbParameter>()
            .Returns(new SqlParameter());

        var persistenceDelegate = A.Fake<ITriggerPersistenceDelegate>();
        var exception = new InvalidOperationException();
        A.CallTo(() => persistenceDelegate.LoadExtendedTriggerProperties(A<ConnectionAndTransactionHolder>.Ignored, A<TriggerKey>.Ignored, CancellationToken.None)).Throws(exception);

        StdAdoDelegate adoDelegate = new TestStdAdoDelegate(persistenceDelegate);

        var delegateInitializationArgs = new DelegateInitializationArgs
        {
            TablePrefix = "QRTZ_",
            InstanceId = "TESTSCHED",
            InstanceName = "INSTANCE",
            TypeLoadHelper = new SimpleTypeLoadHelper(),
            UseProperties = false,
            InitString = "",
            DbProvider = dbProvider
        };
        adoDelegate.Initialize(delegateInitializationArgs);

        // First result set has results, second has none
        A.CallTo(() => dataReader.ReadAsync(CancellationToken.None)).Returns(true).Once();
        A.CallTo(() => dataReader[AdoConstants.ColumnTriggerType]).Returns(AdoConstants.TriggerTypeSimple);
        A.CallTo(() => dataReader[A<string>._]).Returns("1");

        var conn = new ConnectionAndTransactionHolder(connection, transaction);
        IOperableTrigger trigger = await adoDelegate.SelectTrigger(conn, new TriggerKey("test"));
        Assert.That(trigger, Is.Null);

        A.CallTo(() => persistenceDelegate.LoadExtendedTriggerProperties(A<ConnectionAndTransactionHolder>.Ignored, A<TriggerKey>.Ignored, CancellationToken.None)).MustHaveHappened();
    }

    [Test]
    public void ShouldSupportAssemblyQualifiedTriggerPersistenceDelegates()
    {
        StdAdoDelegate adoDelegate = new TestStdAdoDelegate(new SimpleTriggerPersistenceDelegate());

        var delegateInitializationArgs = new DelegateInitializationArgs
        {
            TablePrefix = "QRTZ_",
            InstanceId = "TESTSCHED",
            InstanceName = "INSTANCE",
            TypeLoadHelper = new SimpleTypeLoadHelper(),
            UseProperties = false,
            InitString = "triggerPersistenceDelegateClasses=" + typeof(TestTriggerPersistenceDelegate).AssemblyQualifiedName + ";" + typeof(TestTriggerPersistenceDelegate).AssemblyQualifiedName,
            DbProvider = A.Fake<IDbProvider>()
        };
        adoDelegate.Initialize(delegateInitializationArgs);
    }

    private class TestStdAdoDelegate : StdAdoDelegate
    {
        private readonly ITriggerPersistenceDelegate testDelegate;

        public TestStdAdoDelegate(ITriggerPersistenceDelegate testDelegate)
        {
            this.testDelegate = testDelegate;
        }

        protected override ITriggerPersistenceDelegate FindTriggerPersistenceDelegate(string discriminator)
        {
            return testDelegate;
        }
    }
}

public abstract class StubCommand : DbCommand
{
    protected StubCommand()
    {
        CommandText = "";
    }

    public override string CommandText { get; set; }
}

public class StubParameterCollection : DbParameterCollection
{
    public override int Add(object value)
    {
        return -1;
    }

    public override bool Contains(object value)
    {
        return false;
    }

    public override void Clear()
    {
    }

    public override int IndexOf(object value)
    {
        return -1;
    }

    public override void Insert(int index, object value)
    {
    }

    public override void Remove(object value)
    {
    }

    public override void RemoveAt(int index)
    {
    }

    public override void RemoveAt(string parameterName)
    {
    }

    protected override void SetParameter(int index, DbParameter value)
    {
    }

    protected override void SetParameter(string parameterName, DbParameter value)
    {
    }

    public override int Count => throw new NotImplementedException();

    public override object SyncRoot => throw new NotImplementedException();

    public override int IndexOf(string parameterName)
    {
        throw new NotImplementedException();
    }

    public override IEnumerator GetEnumerator()
    {
        throw new NotImplementedException();
    }

    protected override DbParameter GetParameter(int index)
    {
        throw new NotImplementedException();
    }

    protected override DbParameter GetParameter(string parameterName)
    {
        throw new NotImplementedException();
    }

    public override bool Contains(string value)
    {
        return false;
    }

    public override void CopyTo(Array array, int index)
    {
    }

    public override void AddRange(Array values)
    {
    }
}

internal class TestTriggerPersistenceDelegate : SimpleTriggerPersistenceDelegate;