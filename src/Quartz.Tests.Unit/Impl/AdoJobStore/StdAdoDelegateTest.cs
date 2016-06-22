#region License

/* 
 * All content copyright Terracotta, Inc., unless otherwise indicated. All rights reserved. 
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
using System.Data.Common;
using System.Data.SqlClient;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
#if FAKE_IT_EASY
using FakeItEasy;
#endif
using NUnit.Framework;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Logging;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Tests.Unit.Impl.AdoJobStore
{
    /// <author>Marko Lahma (.NET)</author>
    [TestFixture]
    public class StdAdoDelegateTest
    {
        [Test]
        public void TestSerializeJobData()
        {
            var args = new DelegateInitializationArgs();
            args.Logger = LogProvider.GetLogger(GetType());
            args.TablePrefix = "QRTZ_";
            args.InstanceName = "TESTSCHED";
            args.InstanceId = "INSTANCE";
            args.DbProvider = new DbProvider(TestConstants.DefaultSqlServerProvider, "");
            args.TypeLoadHelper = new SimpleTypeLoadHelper();
#if BINARY_SERIALIZATION
            args.ObjectSerializer = new BinaryObjectSerializer();
#else
            args.ObjectSerializer = new JsonObjectSerializer();
#endif

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
#if BINARY_SERIALIZATION
                Assert.Fail("Private types should not be serializable by binary serialization");
#endif
            }
            catch (SerializationException e)
            {
#if BINARY_SERIALIZATION
                Assert.IsTrue(e.Message.IndexOf("key3") >= 0);
#else
                Assert.Fail($"Private types should be serializable when not using binary serialization: {e.ToString()}");
#endif
            }
        }

        private class NonSerializableTestClass
        {
        }

#if FAKE_IT_EASY
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
                Logger = LogProvider.GetLogger(GetType()),
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
            A.CallTo(() => persistenceDelegate.LoadExtendedTriggerProperties(A<ConnectionAndTransactionHolder>.Ignored, A<TriggerKey>.Ignored)).Throws(exception);

            StdAdoDelegate adoDelegate = new TestStdAdoDelegate(persistenceDelegate);

            var delegateInitializationArgs = new DelegateInitializationArgs
            {
                TablePrefix = "QRTZ_",
                InstanceId = "TESTSCHED",
                InstanceName = "INSTANCE",
                TypeLoadHelper = new SimpleTypeLoadHelper(),
                UseProperties = false,
                InitString = "",
                Logger = LogProvider.GetLogger(GetType()),
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

            A.CallTo(() => persistenceDelegate.LoadExtendedTriggerProperties(A<ConnectionAndTransactionHolder>.Ignored, A<TriggerKey>.Ignored)).MustHaveHappened();
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
            A.CallTo(() => persistenceDelegate.LoadExtendedTriggerProperties(A<ConnectionAndTransactionHolder>.Ignored, A<TriggerKey>.Ignored)).Throws(exception);

            StdAdoDelegate adoDelegate = new TestStdAdoDelegate(persistenceDelegate);

            var delegateInitializationArgs = new DelegateInitializationArgs
            {
                TablePrefix = "QRTZ_",
                InstanceId = "TESTSCHED",
                InstanceName = "INSTANCE",
                TypeLoadHelper = new SimpleTypeLoadHelper(),
                UseProperties = false,
                InitString = "",
                Logger = LogProvider.GetLogger(GetType()),
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

            A.CallTo(() => persistenceDelegate.LoadExtendedTriggerProperties(A<ConnectionAndTransactionHolder>.Ignored, A<TriggerKey>.Ignored)).MustHaveHappened();
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
                Logger = LogProvider.GetLogger(GetType()),
                DbProvider = A.Fake<IDbProvider>()
            };
            adoDelegate.Initialize(delegateInitializationArgs);
        }

#endif

        private class TestStdAdoDelegate : StdAdoDelegate
        {
            private readonly ITriggerPersistenceDelegate testDelegate;

            public TestStdAdoDelegate(ITriggerPersistenceDelegate testDelegate)
            {
                this.testDelegate = testDelegate;
            }

            public override ITriggerPersistenceDelegate FindTriggerPersistenceDelegate(string discriminator)
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

        public override int Count
        {
            get { throw new NotImplementedException(); }
        }

        public override object SyncRoot
        {
            get { throw new NotImplementedException(); }
        }

#if !NETCORE
        public override bool IsFixedSize
        {
            get { throw new NotImplementedException(); }
        }


        public override bool IsReadOnly
        {
            get { throw new NotImplementedException(); }
        }


        public override bool IsSynchronized
        {
            get { throw new NotImplementedException(); }
        }
#endif

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

    public class TestTriggerPersistenceDelegate : SimpleTriggerPersistenceDelegate
    {
    }
}