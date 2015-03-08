using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using Quartz.Logging;
using NUnit.Framework;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Impl.Triggers;
using Quartz.Simpl;
using FakeItEasy;

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

            var dbProvider = A.Fake<IDbProvider>();
            var dbCommand = A.Fake<IDbCommand>();
            var dataParameterCollection = A.Fake<IDataParameterCollection>();
            A.CallTo(() => dbProvider.CreateCommand()).Returns(dbCommand);
            Func<StubDataParameter> dataParam = () => new StubDataParameter();
            A.CallTo(() => dbProvider.CreateParameter()).ReturnsLazily(dataParam);
            A.CallTo(() => dbCommand.CreateParameter()).ReturnsLazily(dataParam);

            var dataParameterCollectionOutputs = new List<object>();

            Func<object, int> dataParameterFunc = x =>
                                                      {
                                                          dataParameterCollectionOutputs.Add(x);
                                                          return 1;
                                                      };

            A.CallTo(() => dataParameterCollection.Add(A<object>.Ignored)).ReturnsLazily(dataParameterFunc);

            A.CallTo(() => dbCommand.Parameters).Returns(dataParameterCollection);
            var metaData = A.Fake<DbMetadata>();
            A.CallTo(() => dbProvider.Metadata).Returns(metaData);
            
            Func<string, string> paramFunc = x => x;
            A.CallTo(() => metaData.GetParameterName(A<string>.Ignored)).ReturnsLazily(paramFunc);

            DelegateInitializationArgs args = new DelegateInitializationArgs();
            args.Logger = LogProvider.GetLogger(GetType());
            args.TablePrefix = "QRTZ_";
            args.InstanceName = "TESTSCHED";
            args.InstanceId = "INSTANCE";
            args.DbProvider = dbProvider;
            args.TypeLoadHelper = new SimpleTypeLoadHelper();

            var adoDelegate = new StdAdoDelegate();
            adoDelegate.Initialize(args);

            var dbConnection = new StubConnection();
            var conn = new ConnectionAndTransactionHolder(dbConnection, null);
            var jobDetail = A.Fake<IJobDetail>();
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