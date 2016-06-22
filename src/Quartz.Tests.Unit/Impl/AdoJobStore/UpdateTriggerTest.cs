using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
#if FAKE_IT_EASY
using FakeItEasy;
#endif
using NUnit.Framework;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Impl.Triggers;
using Quartz.Logging;
using Quartz.Simpl;

namespace Quartz.Tests.Unit.Impl.AdoJobStore
{
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
#if FAKE_IT_EASY
        [Test]
        public async Task CronTrigger_AfterTriggerUpdate_Retains_Cron_Type()
        {
            //Arrange
            var cronTriggerImpl = new CronTriggerImpl("Trigger", "Trigger.Group", "JobName", "JobGroup", "0 15 23 * * ?");
            cronTriggerImpl.CalendarName = "calName";
            cronTriggerImpl.MisfireInstruction = 1;
            cronTriggerImpl.Description = "Description";
            cronTriggerImpl.SetPreviousFireTimeUtc(new DateTimeOffset(new DateTime(2010, 1, 1)));
            cronTriggerImpl.SetNextFireTimeUtc(new DateTimeOffset(new DateTime(2010, 2, 1)));
            cronTriggerImpl.JobKey = new JobKey("JobKey", "JobKeyGroup");
            cronTriggerImpl.Priority = 1;

            var dbProvider = A.Fake<IDbProvider>();
            var dbCommand = A.Fake<DbCommand>();
            var dataParameterCollection = A.Fake<DbParameterCollection>();
            A.CallTo(() => dbProvider.CreateCommand()).Returns(dbCommand);
            Func<DbParameter> dataParam = () => new SqlParameter();
            A.CallTo(dbProvider)
                .Where(x => x.Method.Name == "CreateDbParameter")
                .WithReturnType<DbParameter>()
                .ReturnsLazily(dataParam);

            A.CallTo(dbCommand)
                .Where(x => x.Method.Name == "CreateDbParameter")
                .WithReturnType<DbParameter>()
                .ReturnsLazily(dataParam);

            var dataParameterCollectionOutputs = new List<object>();

            A.CallTo(() => dataParameterCollection.Add(A<object>._)).Invokes(x=>
            {
                dataParameterCollectionOutputs.Add(x.Arguments.Single());
            });

            A.CallTo(dbCommand)
                .Where(x => x.Method.Name == "get_DbParameterCollection")
                .WithReturnType<DbParameterCollection>()
                .Returns(dataParameterCollection);

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
            await adoDelegate.UpdateTrigger(conn, cronTriggerImpl, "state", jobDetail);

            //Assert
            var resultDataParameters = dataParameterCollectionOutputs.Select(x => x as IDataParameter).Where(x => x.ParameterName == "triggerType").FirstOrDefault();
            Assert.AreEqual("CRON", resultDataParameters.Value);
        }
#endif
    }
}