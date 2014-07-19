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
using System.Data;
using System.Data.Common;
using System.Runtime.Serialization;

using Common.Logging;

using NUnit.Framework;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Simpl;
using Quartz.Spi;

using Rhino.Mocks;

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
            args.Logger = LogManager.GetLogger(GetType());
            args.TablePrefix = "QRTZ_";
            args.InstanceName = "TESTSCHED";
            args.InstanceId = "INSTANCE";
            args.DbProvider = new DbProvider("SqlServer-20", "");
            args.TypeLoadHelper = new SimpleTypeLoadHelper();
            args.ObjectSerializer = new DefaultObjectSerializer();

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
                Assert.Fail();
            }
            catch (SerializationException e)
            {
                Assert.IsTrue(e.Message.IndexOf("key3") >= 0);
            }
        }

        private class NonSerializableTestClass
        {
        }

        [Test]
        public void TestSelectBlobTriggerWithNoBlobContent()
        {
            var dbProvider = MockRepository.GenerateMock<IDbProvider>();
            var connection = MockRepository.GenerateMock<IDbConnection>();
            var transaction = MockRepository.GenerateMock<IDbTransaction>();
            var command = (IDbCommand) MockRepository.GenerateMock<StubCommand>();
            var dbMetadata = new DbMetadata();
            dbProvider.Stub(x => x.Metadata).Repeat.Any().Return(dbMetadata);

            dbProvider.Stub(x => x.CreateCommand()).Return(command);

            var dataReader = MockRepository.GenerateMock<IDataReader>();
            command.Stub(x => x.ExecuteReader()).Return(dataReader);
            command.Stub(x => x.Parameters).Repeat.Any().Return(new StubParameterCollection());
            command.Stub(x => x.CommandText).Return("").Repeat.Any();
            command.Stub(x => x.CreateParameter()).Repeat.Any().Return(new StubDataParameter());

            var adoDelegate = new StdAdoDelegate();

            var delegateInitializationArgs = new DelegateInitializationArgs
                                             {
                                                 TablePrefix = "QRTZ_",
                                                 InstanceId = "TESTSCHED",
                                                 InstanceName = "INSTANCE",
                                                 TypeLoadHelper = new SimpleTypeLoadHelper(),
                                                 UseProperties = false,
                                                 InitString = "",
                                                 Logger = LogManager.GetLogger(GetType()),
                                                 DbProvider = dbProvider
                                             };
            adoDelegate.Initialize(delegateInitializationArgs);

            var conn = new ConnectionAndTransactionHolder(connection, transaction);

            // First result set has results, second has none
            dataReader.Stub(x => x.Read()).Return(true).Repeat.Once();
            dataReader.Stub(x => x.Read()).Return(false);
            dataReader.Stub(x => x[AdoConstants.ColumnTriggerType]).Return(AdoConstants.TriggerTypeBlob);

            IOperableTrigger trigger = adoDelegate.SelectTrigger(conn, new TriggerKey("test"));
            Assert.That(trigger, Is.Null);
        }

        [Test]
        public void TestSelectSimpleTriggerWithExceptionWithExtendedProps()
        {
            var dbProvider = MockRepository.GenerateMock<IDbProvider>();
            var connection = MockRepository.GenerateMock<IDbConnection>();
            var transaction = MockRepository.GenerateMock<IDbTransaction>();
            var command = (IDbCommand)MockRepository.GenerateMock<StubCommand>();
            var dbMetadata = new DbMetadata();
            dbProvider.Stub(x => x.Metadata).Repeat.Any().Return(dbMetadata);

            dbProvider.Stub(x => x.CreateCommand()).Return(command);

            var dataReader = MockRepository.GenerateMock<IDataReader>();
            command.Stub(x => x.ExecuteReader()).Return(dataReader);
            command.Stub(x => x.Parameters).Repeat.Any().Return(new StubParameterCollection());
            command.Stub(x => x.CommandText).Return("").Repeat.Any();
            command.Stub(x => x.CreateParameter()).Repeat.Any().Return(new StubDataParameter());

            var persistenceDelegate = MockRepository.GenerateMock<ITriggerPersistenceDelegate>();
            var exception = new InvalidOperationException();
            persistenceDelegate.Stub(x => x.LoadExtendedTriggerProperties(Arg<ConnectionAndTransactionHolder>.Is.Anything, Arg<TriggerKey>.Is.Anything)).Throw(exception);
            

            StdAdoDelegate adoDelegate = new TestStdAdoDelegate(persistenceDelegate);
            
            var delegateInitializationArgs = new DelegateInitializationArgs
                                             {
                                                 TablePrefix = "QRTZ_",
                                                 InstanceId = "TESTSCHED",
                                                 InstanceName = "INSTANCE",
                                                 TypeLoadHelper = new SimpleTypeLoadHelper(),
                                                 UseProperties = false,
                                                 InitString = "",
                                                 Logger = LogManager.GetLogger(GetType()),
                                                 DbProvider = dbProvider
                                             };
            adoDelegate.Initialize(delegateInitializationArgs);

            // Mock basic trigger data
            dataReader.Stub(x => x.Read()).Return(true).Repeat.Any();
            dataReader.Stub(x => x[AdoConstants.ColumnTriggerType]).Return(AdoConstants.TriggerTypeSimple);

            try
            {
                var conn = new ConnectionAndTransactionHolder(connection, transaction);
                adoDelegate.SelectTrigger(conn, new TriggerKey("test"));
                Assert.Fail("Trigger selection should result in exception");
            }
            catch (InvalidOperationException e)
            {
                Assert.That(e, Is.SameAs(exception));
            }
            
            persistenceDelegate.AssertWasCalled(x => x.LoadExtendedTriggerProperties(Arg<ConnectionAndTransactionHolder>.Is.Anything, Arg<TriggerKey>.Is.Anything));
        }

        [Test]
        public void TestSelectSimpleTriggerWithDeleteBeforeSelectExtendedProps()
        {
            var dbProvider = MockRepository.GenerateMock<IDbProvider>();
            var connection = MockRepository.GenerateMock<IDbConnection>();
            var transaction = MockRepository.GenerateMock<IDbTransaction>();
            var command = (IDbCommand)MockRepository.GenerateMock<StubCommand>();
            var dbMetadata = new DbMetadata();
            dbProvider.Stub(x => x.Metadata).Repeat.Any().Return(dbMetadata);

            dbProvider.Stub(x => x.CreateCommand()).Return(command);

            var dataReader = MockRepository.GenerateMock<IDataReader>();
            command.Stub(x => x.ExecuteReader()).Return(dataReader);
            command.Stub(x => x.Parameters).Repeat.Any().Return(new StubParameterCollection());
            command.Stub(x => x.CommandText).Return("").Repeat.Any();
            command.Stub(x => x.CreateParameter()).Repeat.Any().Return(new StubDataParameter());

            var persistenceDelegate = MockRepository.GenerateMock<ITriggerPersistenceDelegate>();
            var exception = new InvalidOperationException();
            persistenceDelegate.Stub(x => x.LoadExtendedTriggerProperties(Arg<ConnectionAndTransactionHolder>.Is.Anything, Arg<TriggerKey>.Is.Anything)).Throw(exception);


            StdAdoDelegate adoDelegate = new TestStdAdoDelegate(persistenceDelegate);

            var delegateInitializationArgs = new DelegateInitializationArgs
            {
                TablePrefix = "QRTZ_",
                InstanceId = "TESTSCHED",
                InstanceName = "INSTANCE",
                TypeLoadHelper = new SimpleTypeLoadHelper(),
                UseProperties = false,
                InitString = "",
                Logger = LogManager.GetLogger(GetType()),
                DbProvider = dbProvider
            };
            adoDelegate.Initialize(delegateInitializationArgs);

            // First result set has results, second has none
            dataReader.Stub(x => x.Read()).Return(true).Repeat.Once();
            dataReader.Stub(x => x.Read()).Return(false);
            dataReader.Stub(x => x[AdoConstants.ColumnTriggerType]).Return(AdoConstants.TriggerTypeSimple);

            var conn = new ConnectionAndTransactionHolder(connection, transaction);
            IOperableTrigger trigger = adoDelegate.SelectTrigger(conn, new TriggerKey("test"));
            Assert.That(trigger, Is.Null);

            persistenceDelegate.AssertWasCalled(x => x.LoadExtendedTriggerProperties(Arg<ConnectionAndTransactionHolder>.Is.Anything, Arg<TriggerKey>.Is.Anything));
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
                Logger = LogManager.GetLogger(GetType()),
                DbProvider = MockRepository.GenerateMock<IDbProvider>()
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