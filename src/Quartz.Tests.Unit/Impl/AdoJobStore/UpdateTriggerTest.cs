using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using Common.Logging;
using NUnit.Framework;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Impl.Triggers;
using Quartz.Simpl;
using Rhino.Mocks;

namespace Quartz.Tests.Unit.Impl.AdoJobStore
{
    public class StubDataParameter : IDbDataParameter
    {
        public DbType DbType { get; set; }

        public ParameterDirection Direction { get; set; }

        public bool IsNullable { get; set; }

        public string ParameterName { get; set; }

        public string SourceColumn { get; set; }

        public DataRowVersion SourceVersion { get; set; }

        public object Value { get; set; }

        public byte Precision { get; set; }

        public byte Scale { get; set; }

        public int Size { get; set; }
    }

    public class StubConnection : DbConnection
    {
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            throw new NotImplementedException();
        }

        public override void Close()
        {
            throw new NotImplementedException();
        }

        public override void ChangeDatabase(string databaseName)
        {
            throw new NotImplementedException();
        }

        public override void Open()
        {
            throw new NotImplementedException();
        }

        public override string ConnectionString
        {
            get { throw new NotImplementedException(); }
            set { throw new NotImplementedException(); }
        }

        public override string Database
        {
            get { throw new NotImplementedException(); }
        }

        public override ConnectionState State
        {
            get { throw new NotImplementedException(); }
        }

        public override string DataSource
        {
            get { throw new NotImplementedException(); }
        }

        public override string ServerVersion
        {
            get { throw new NotImplementedException(); }
        }

        protected override DbCommand CreateDbCommand()
        {
            throw new NotImplementedException();
        }
    }

    [TestFixture]
    public class UpdateTriggerTest
    {
        [Test]
        public void CronTrigger_AfterTriggerUpdate_Retains_Cron_Type()
        {
            //Arrange
            var cronTriggerImpl = new CronTriggerImpl("Trigger", "Trigger.Group", "JobName", "JobGroup", "0 15 23 * * ?");
            cronTriggerImpl.CalendarName = "calName";
            cronTriggerImpl.MisfireInstruction = 1;
            cronTriggerImpl.Description = "Description";
            cronTriggerImpl.SetPreviousFireTimeUtc(new DateTimeOffset(new DateTime(2010,1,1)));
            cronTriggerImpl.SetNextFireTimeUtc(new DateTimeOffset(new DateTime(2010, 2, 1)));
            cronTriggerImpl.JobKey = new JobKey("JobKey","JobKeyGroup");
            cronTriggerImpl.Priority = 1;

            var dbProvider = MockRepository.GenerateStub<IDbProvider>();
            var dbCommand = MockRepository.GenerateStub<IDbCommand>();
            var dataParameterCollection = MockRepository.GenerateStub<IDataParameterCollection>();
            dbProvider.Stub(d => d.CreateCommand()).Return(dbCommand).Repeat.Any();
            Func<StubDataParameter> dataParam = () => new StubDataParameter();
            dbProvider.Stub(d => d.CreateParameter()).Do(dataParam);
            dbCommand.Stub(c => c.CreateParameter()).Do(dataParam);

            var dataParameterCollectionOutputs = new List<object>();

            Func<object, int> dataParameterFunc = x =>
                                                      {
                                                          dataParameterCollectionOutputs.Add(x);
                                                          return 1;
                                                      };

            dataParameterCollection.Stub(d => d.Add(Arg<object>.Is.Anything)).Do(dataParameterFunc);

            dbCommand.Stub(c => c.Parameters).Return(dataParameterCollection);
            var metaData = MockRepository.GenerateStub<DbMetadata>();
            dbProvider.Stub(d => d.Metadata).Return(metaData);
            
            Func<string, string> paramFunc = x => x;
            metaData.Stub(m => m.GetParameterName(Arg<string>.Is.Anything)).Do(paramFunc);

            DelegateInitializationArgs args = new DelegateInitializationArgs();
            args.Logger = LogManager.GetLogger(GetType());
            args.TablePrefix = "QRTZ_";
            args.InstanceName = "TESTSCHED";
            args.InstanceId = "INSTANCE";
            args.DbProvider = dbProvider;
            args.TypeLoadHelper = new SimpleTypeLoadHelper();

            var adoDelegate = new StdAdoDelegate();
            adoDelegate.Initialize(args);

            var dbConnection = new StubConnection();
            var conn = new ConnectionAndTransactionHolder(dbConnection, null);
            var jobDetail = MockRepository.GenerateMock<IJobDetail>();
            var jobDataMap = new JobDataMap();
            jobDataMap.ClearDirtyFlag();
            cronTriggerImpl.JobDataMap = jobDataMap;
            
            //Act
            adoDelegate.UpdateTrigger(conn, cronTriggerImpl, "state", jobDetail);

            //Assert
            var resultDataParameters = dataParameterCollectionOutputs.Select(x => x as IDataParameter).Where(x => x.ParameterName == "triggerType").FirstOrDefault();
            Assert.AreEqual("CRON",resultDataParameters.Value);
        }
    }
}