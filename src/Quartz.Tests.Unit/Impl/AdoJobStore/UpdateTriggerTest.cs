using System.Data;
using System.Data.Common;

using FakeItEasy;

using Microsoft.Data.SqlClient;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Impl.Triggers;
using Quartz.Simpl;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

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
        get => throw new NotImplementedException();
        set => throw new NotImplementedException();
    }

    public override string Database => throw new NotImplementedException();

    public override ConnectionState State => throw new NotImplementedException();

    public override string DataSource => throw new NotImplementedException();

    public override string ServerVersion => throw new NotImplementedException();

    protected override DbCommand CreateDbCommand()
    {
        throw new NotImplementedException();
    }
}

[TestFixture]
public class UpdateTriggerTest
{
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

        // Support getting the existing trigger type.
        var selectTypeReader = A.Fake<DbDataReader>();
        A.CallTo(() => selectTypeReader.ReadAsync(CancellationToken.None))
            .Returns(true);
        A.CallTo(() => selectTypeReader[AdoConstants.ColumnTriggerType])
            .Returns(AdoConstants.TriggerTypeCron);

        var selectTypeCommand = A.Fake<DbCommand>();
        A.CallTo(selectTypeCommand)
            .Where(x => x.Method.Name == "ExecuteDbDataReaderAsync")
            .WithReturnType<Task<DbDataReader>>()
            .Returns(selectTypeReader);

        var dbProvider = A.Fake<IDbProvider>();
        var dbCommand = A.Fake<DbCommand>();
        A.CallTo(() => dbProvider.CreateCommand()).ReturnsNextFromSequence(selectTypeCommand, dbCommand);

        var dataParameterCollection = A.Fake<DbParameterCollection>();

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

        A.CallTo(() => dataParameterCollection.Add(A<object>._)).Invokes(x =>
        {
            dataParameterCollectionOutputs.Add(x.Arguments.Single());
        });

        A.CallTo(dbCommand)
            .Where(x => x.Method.Name == "get_DbParameterCollection")
            .WithReturnType<DbParameterCollection>()
            .Returns(dataParameterCollection);

        A.CallTo(() => dbProvider.Metadata).Returns(new DbMetadata());

        DelegateInitializationArgs args = new DelegateInitializationArgs();
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
        Assert.That(resultDataParameters.Value, Is.EqualTo("CRON"));
    }
}