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
using Microsoft.Extensions.Time.Testing;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Impl.Triggers;
using Quartz.Impl;
using Quartz.Extensibility;
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
    }

    [Test]
    public void TestSerializeJobData()
    {
        var args = new DriverDelegateContext
        {
            TablePrefix = "QRTZ_",
            SchedulerName = "TESTSCHED",
            InstanceId = "INSTANCE",
            DbProvider = new DbProvider(TestConstants.DefaultSqlServerProvider, ""),
            TypeLoader = new SimpleTypeLoader(),
            ObjectSerializer = serializer
        };

        var del = new StdAdoDelegate();
        del.Initialize(args);

        var jdm = new JobDataMap();
        del.SerializeJobData(jdm);

        jdm.Clear();
        jdm["key"] = "value";
        jdm["key2"] = null;
        del.SerializeJobData(jdm);

        jdm.Clear();
        jdm["key1"] = "value";
        jdm["key2"] = null;
        jdm["key3"] = new NonSerializableTestClass();

        Action serializeApplicationType = () => del.SerializeJobData(jdm);

        if (serializer is SystemTextJsonObjectSerializer)
        {
            // The read side has no polymorphic object deserialization, so a class of the application's
            // own could never come back as itself. Writing it would put a blob in JOB_DATA that the next
            // load of this job fails on, which is why the refusal happens here instead.
            serializeApplicationType.Should().Throw<JsonSerializationException>(
                    "System.Text.Json refuses at write time a value it could not read back")
                .Which.Message.Should().Contain("AddTypeInfoResolver",
                    "the failure has to name the way an application declares a type of its own");
        }
        else
        {
            serializeApplicationType.Should().NotThrow(
                "with binary serialization out of the picture, a private type is no obstacle to Newtonsoft");
        }
    }

    private sealed class NonSerializableTestClass;

    [Test]
    public async Task SelectTriggersToAcquire_ShouldBindExcludedJobTypesInSqlOrder()
    {
        IDbProvider dbProvider = A.Fake<IDbProvider>();
        DbConnection connection = A.Fake<DbConnection>();
        DbTransaction transaction = A.Fake<DbTransaction>();
        DbCommand command = A.Fake<StubCommand>();
        DbDataReader dataReader = A.Fake<DbDataReader>();
        DbParameterCollection parameterCollection = A.Fake<DbParameterCollection>();
        List<DbParameter> boundParameters = [];

        A.CallTo(() => dbProvider.Metadata).Returns(new DbMetadata
        {
            BindByName = true,
            ParameterNamePrefix = "@"
        });
        A.CallTo(() => dbProvider.CreateCommand()).Returns(command);
        A.CallTo(command).Where(x => x.Method.Name == "get_DbParameterCollection")
            .WithReturnType<DbParameterCollection>()
            .Returns(parameterCollection);
        A.CallTo(command).Where(x => x.Method.Name == "CreateDbParameter")
            .WithReturnType<DbParameter>()
            .ReturnsLazily(() => new SqlParameter());
        A.CallTo(command).Where(x => x.Method.Name == "ExecuteDbDataReaderAsync")
            .WithReturnType<Task<DbDataReader>>()
            .Returns(Task.FromResult(dataReader));
        A.CallTo(() => dataReader.ReadAsync(CancellationToken.None)).Returns(false);
        A.CallTo(() => parameterCollection.Add(A<object>._)).ReturnsLazily((object value) =>
        {
            boundParameters.Add((DbParameter) value);
            return boundParameters.Count - 1;
        });

        StdAdoDelegate adoDelegate = new();
        adoDelegate.Initialize(new DriverDelegateContext
        {
            TablePrefix = "QRTZ_",
            InstanceId = "TESTSCHED",
            SchedulerName = "INSTANCE",
            TypeLoader = new SimpleTypeLoader(),
            UseProperties = false,
            DbProvider = dbProvider,
            ObjectSerializer = serializer
        });

        ConnectionAndTransactionHolder conn = new(connection, transaction);
        await adoDelegate.SelectTriggersToAcquire(conn, new TriggerAcquisitionCriteria
        {
            NoLaterThan = DateTimeOffset.UtcNow.AddMinutes(1),
            NoEarlierThan = DateTimeOffset.UtcNow.AddMinutes(-1),
            MaxCount = 5,
            LiveNodeCutoff = DateTimeOffset.UtcNow.AddMinutes(-2),
            ExcludedJobTypeNames = ["First.Job", "Second.Job", "Third.Job"]
        });

        boundParameters.Select(x => x.ParameterName).Should().Equal(
            "@schedulerName",
            "@state",
            "@noLaterThan",
            "@noEarlierThan",
            "@instanceId",
            "@autoPinSentinel",
            "@liveNodeCutoff",
            "@excludedJobType0000",
            "@excludedJobType0001",
            "@excludedJobType0002",
            "@excludedJobType0003");
        boundParameters.Skip(7).Select(x => x.Value).Should().Equal(
            "First.Job",
            "Second.Job",
            "Third.Job",
            "Third.Job");
    }

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

        var dataReader = FakeReader();
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

        var driverDelegateContext = new DriverDelegateContext
        {
            TablePrefix = "QRTZ_",
            InstanceId = "TESTSCHED",
            SchedulerName = "INSTANCE",
            TypeLoader = new SimpleTypeLoader(),
            UseProperties = false,
            DbProvider = dbProvider
        };
        adoDelegate.Initialize(driverDelegateContext);

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

        var dataReader = FakeReader();

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

        // Preferred node auto-claim column reads as absent (distinct ordinal, no collision)
        A.CallTo(() => dataReader.GetOrdinal(AdoConstants.ColumnPreferredNodeAuto)).Returns(20);
        A.CallTo(() => dataReader.IsDBNull(20)).Returns(true);

        var persistenceDelegate = A.Fake<ITriggerPersistenceDelegate>();
        var exception = new InvalidOperationException();
        A.CallTo(() => persistenceDelegate.LoadExtendedTriggerProperties(A<ConnectionAndTransactionHolder>.Ignored, A<TriggerKey>.Ignored, CancellationToken.None)).Throws(exception);

        StdAdoDelegate adoDelegate = new TestStdAdoDelegate(persistenceDelegate);

        var driverDelegateContext = new DriverDelegateContext
        {
            TablePrefix = "QRTZ_",
            InstanceId = "TESTSCHED",
            SchedulerName = "INSTANCE",
            TypeLoader = new SimpleTypeLoader(),
            UseProperties = false,
            DbProvider = dbProvider
        };
        adoDelegate.Initialize(driverDelegateContext);

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

        var dataReader = FakeReader();
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
        A.CallTo(() => dbProvider.Metadata)
            .Returns(dbMetadata);

        var driverDelegateContext = new DriverDelegateContext
        {
            TablePrefix = "QRTZ_",
            InstanceId = "TESTSCHED",
            SchedulerName = "INSTANCE",
            TypeLoader = new SimpleTypeLoader(),
            UseProperties = false,
            DbProvider = dbProvider
        };

        var adoDelegate = new StdAdoDelegate();
        adoDelegate.Initialize(driverDelegateContext);

        var jobKey = new JobKey(jobName, jobGroup);

        var jobDetail = await adoDelegate.SelectJobDetail(
            conn,
            jobKey,
            new SimpleTypeLoader(), // resolves the stored JOB_CLASS_NAME
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

    /// <summary>
    /// A table carried over from 2.x or 3.x names job types the way those versions spelled them, and
    /// only the type loader knows what such a spelling means today. Resolving the stored name
    /// without it leaves the job loading and listing perfectly well - the type is resolved lazily -
    /// and then failing at its first fire.
    /// </summary>
    [Test]
    public async Task SelectJobDetailResolvesAPre40JobClassNameThroughTheTypeLoader()
    {
        const string StoredJobClassName = "Quartz.Jobs.NoOpJob, Quartz";

        var connection = A.Fake<DbConnection>();
        var transaction = A.Fake<DbTransaction>();
        var conn = new ConnectionAndTransactionHolder(connection, transaction);

        var dataReader = FakeReader();
        A.CallTo(() => dataReader.ReadAsync(CancellationToken.None))
            .Returns(true)
            .Once();

        A.CallTo(() => dataReader[AdoConstants.ColumnJobName]).Returns("job");
        A.CallTo(() => dataReader[AdoConstants.ColumnJobGroup]).Returns("group");
        A.CallTo(() => dataReader[AdoConstants.ColumnDescription]).Returns("description");
        A.CallTo(() => dataReader[AdoConstants.ColumnJobClass]).Returns(StoredJobClassName);
        A.CallTo(() => dataReader[AdoConstants.ColumnRequestsRecovery]).Returns(false);
        A.CallTo(() => dataReader[AdoConstants.ColumnIsDurable]).Returns(true);
        A.CallTo(() => dataReader[AdoConstants.ColumnIsNonConcurrent]).Returns(false);
        A.CallTo(() => dataReader[AdoConstants.ColumnIsUpdateData]).Returns(false);

        var command = A.Fake<StubCommand>();
        A.CallTo(command)
            .Where(x => x.Method.Name == "ExecuteDbDataReaderAsync")
            .WithReturnType<Task<DbDataReader>>()
            .Returns(Task.FromResult(dataReader));

        var dbMetadata = new DbMetadata
        {
            BindByName = true,
            ParameterNamePrefix = "@"
        };

        var dbProvider = A.Fake<IDbProvider>();
        A.CallTo(() => dbProvider.CreateCommand()).Returns(command);
        A.CallTo(() => dbProvider.Metadata).Returns(dbMetadata);

        var adoDelegate = new StdAdoDelegate();
        adoDelegate.Initialize(new DriverDelegateContext
        {
            TablePrefix = "QRTZ_",
            InstanceId = "TESTSCHED",
            SchedulerName = "INSTANCE",
            TypeLoader = new SimpleTypeLoader(),
            UseProperties = false,
            DbProvider = dbProvider,
            ObjectSerializer = serializer
        });

        IJobDetail jobDetail = await adoDelegate.SelectJobDetail(
            conn,
            new JobKey("job", "group"),
            new SimpleTypeLoader(),
            CancellationToken.None);

        jobDetail.Should().NotBeNull();
        jobDetail.JobType.FullName.Should().Be(StoredJobClassName,
            "reading a job must not rewrite the JOB_CLASS_NAME that is persisted for it");
        jobDetail.JobType.Type.Should().Be<global::Quartz.Jobs.NoOpJob>(
            "a job class name stored before the jobs moved to their own assembly has to resolve through the type loader");
    }

    /// <summary>
    /// An ADO store keeps a job as the columns of QRTZ_JOB_DETAILS and rebuilds every detail it reads
    /// through <see cref="JobBuilder" />, so an implementation of <see cref="IJobDetail" /> other than
    /// Quartz's own does not survive the round trip the way it does through <c>RAMJobStore</c> (#1143).
    /// That is the promise <see cref="IJobDetail" />'s documentation makes to anyone implementing it,
    /// and this is where it is decided.
    /// </summary>
    [Test]
    public async Task SelectJobDetailRebuildsTheDetailAsQuartzsOwnImplementation()
    {
        var connection = A.Fake<DbConnection>();
        var transaction = A.Fake<DbTransaction>();
        var conn = new ConnectionAndTransactionHolder(connection, transaction);

        var dataReader = FakeReader();
        A.CallTo(() => dataReader.ReadAsync(CancellationToken.None))
            .Returns(true)
            .Once();

        A.CallTo(() => dataReader[AdoConstants.ColumnJobName]).Returns("job");
        A.CallTo(() => dataReader[AdoConstants.ColumnJobGroup]).Returns("group");
        A.CallTo(() => dataReader[AdoConstants.ColumnDescription]).Returns("description");
        A.CallTo(() => dataReader[AdoConstants.ColumnJobClass]).Returns(typeof(TestJob).AssemblyQualifiedNameWithoutVersion());
        A.CallTo(() => dataReader[AdoConstants.ColumnRequestsRecovery]).Returns(false);
        A.CallTo(() => dataReader[AdoConstants.ColumnIsDurable]).Returns(true);
        A.CallTo(() => dataReader[AdoConstants.ColumnIsNonConcurrent]).Returns(false);
        A.CallTo(() => dataReader[AdoConstants.ColumnIsUpdateData]).Returns(true);

        var command = A.Fake<StubCommand>();
        A.CallTo(command)
            .Where(x => x.Method.Name == "ExecuteDbDataReaderAsync")
            .WithReturnType<Task<DbDataReader>>()
            .Returns(Task.FromResult(dataReader));

        var dbProvider = A.Fake<IDbProvider>();
        A.CallTo(() => dbProvider.CreateCommand()).Returns(command);
        A.CallTo(() => dbProvider.Metadata).Returns(new DbMetadata { BindByName = true, ParameterNamePrefix = "@" });

        var adoDelegate = new StdAdoDelegate();
        adoDelegate.Initialize(new DriverDelegateContext
        {
            TablePrefix = "QRTZ_",
            InstanceId = "TESTSCHED",
            SchedulerName = "INSTANCE",
            TypeLoader = new SimpleTypeLoader(),
            UseProperties = false,
            DbProvider = dbProvider,
            ObjectSerializer = serializer
        });

        IJobDetail jobDetail = await adoDelegate.SelectJobDetail(
            conn,
            new JobKey("job", "group"),
            new SimpleTypeLoader(),
            CancellationToken.None);

        jobDetail.Should().BeOfType<JobDetailImpl>(
            "the row says what the job is, not what type described it when it was stored");
    }

    private sealed class TestJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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

        var dataReader = FakeReader();

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

        // Preferred node auto-claim column reads as absent (distinct ordinal, no collision)
        A.CallTo(() => dataReader.GetOrdinal(AdoConstants.ColumnPreferredNodeAuto)).Returns(20);
        A.CallTo(() => dataReader.IsDBNull(20)).Returns(true);

        var persistenceDelegate = A.Fake<ITriggerPersistenceDelegate>();
        var exception = new InvalidOperationException();
        A.CallTo(() => persistenceDelegate.LoadExtendedTriggerProperties(A<ConnectionAndTransactionHolder>.Ignored, A<TriggerKey>.Ignored, CancellationToken.None)).Throws(exception);

        StdAdoDelegate adoDelegate = new TestStdAdoDelegate(persistenceDelegate);

        var driverDelegateContext = new DriverDelegateContext
        {
            TablePrefix = "QRTZ_",
            InstanceId = "TESTSCHED",
            SchedulerName = "INSTANCE",
            TypeLoader = new SimpleTypeLoader(),
            UseProperties = false,
            DbProvider = dbProvider
        };
        adoDelegate.Initialize(driverDelegateContext);

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
    public async Task TestSelectBlobTriggerPopulatesFireTimesFromDb()
    {
        var nextFireTime = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var prevFireTime = new DateTimeOffset(2023, 12, 31, 12, 0, 0, TimeSpan.Zero);

        var dbProvider = A.Fake<IDbProvider>();
        var connection = A.Fake<DbConnection>();
        var transaction = A.Fake<DbTransaction>();
        var command = (DbCommand) A.Fake<StubCommand>();
        var dbMetadata = new DbMetadata();
        A.CallTo(() => dbProvider.Metadata).Returns(dbMetadata);
        A.CallTo(() => dbProvider.CreateCommand()).Returns(command);

        var dataReader = FakeReader();
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

        // Both reads succeed (first for TRIGGERS table, second for BLOB_TRIGGERS table)
        A.CallTo(() => dataReader.ReadAsync(CancellationToken.None)).Returns(true);

        A.CallTo(() => dataReader[AdoConstants.ColumnTriggerType]).Returns(AdoConstants.TriggerTypeBlob);
        A.CallTo(() => dataReader[AdoConstants.ColumnJobName]).Returns("testJob");
        A.CallTo(() => dataReader[AdoConstants.ColumnJobGroup]).Returns("DEFAULT");
        A.CallTo(() => dataReader[AdoConstants.ColumnDescription]).Returns(DBNull.Value);
        A.CallTo(() => dataReader[AdoConstants.ColumnCalendarName]).Returns(DBNull.Value);
        A.CallTo(() => dataReader[AdoConstants.ColumnMisfireInstruction]).Returns(2);
        A.CallTo(() => dataReader[AdoConstants.ColumnPriority]).Returns(5);
        A.CallTo(() => dataReader[AdoConstants.ColumnNextFireTime]).Returns(nextFireTime.UtcTicks);
        A.CallTo(() => dataReader[AdoConstants.ColumnPreviousFireTime]).Returns(prevFireTime.UtcTicks);
        A.CallTo(() => dataReader[AdoConstants.ColumnStartTime]).Returns(DateTimeOffset.UtcNow.UtcTicks);
        A.CallTo(() => dataReader[AdoConstants.ColumnEndTime]).Returns(DBNull.Value);
        A.CallTo(() => dataReader[AdoConstants.ColumnMisfireOriginalFireTime]).Returns(DBNull.Value);
        // Return true for IsDBNull on job data map column so that the map is read as null
        A.CallTo(() => dataReader.IsDBNull(11)).Returns(true);
        // Preferred node auto-claim column reads as absent (distinct ordinal, no collision)
        A.CallTo(() => dataReader.GetOrdinal(AdoConstants.ColumnPreferredNodeAuto)).Returns(20);
        A.CallTo(() => dataReader.IsDBNull(20)).Returns(true);

        // Create a blob trigger with no fire times set (simulating a freshly deserialized custom trigger)
        var blobTrigger = new SimpleTriggerImpl
        {
            Key = new TriggerKey("test", "DEFAULT"),
            JobKey = new JobKey("testJob", "DEFAULT"),
        };

        var adoDelegate = new BlobTriggerOverrideDelegate(blobTrigger);
        adoDelegate.Initialize(new DriverDelegateContext
        {
            TablePrefix = "QRTZ_",
            InstanceId = "TESTSCHED",
            SchedulerName = "INSTANCE",
            TypeLoader = new SimpleTypeLoader(),
            UseProperties = false,
            DbProvider = dbProvider
        });

        var conn = new ConnectionAndTransactionHolder(connection, transaction);
        IOperableTrigger trigger = await adoDelegate.SelectTrigger(conn, new TriggerKey("test"));

        Assert.That(trigger, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(trigger.NextFireTimeUtc, Is.EqualTo(nextFireTime));
            Assert.That(trigger.PreviousFireTimeUtc, Is.EqualTo(prevFireTime));
            Assert.That(trigger.MisfireInstructionCode, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task TestSelectBlobTriggerPopulatesMisfiredFromFireTimeUtcFromDb()
    {
        var nextFireTime = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var misfireOrigFireTime = new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.Zero);

        var dbProvider = A.Fake<IDbProvider>();
        var connection = A.Fake<DbConnection>();
        var transaction = A.Fake<DbTransaction>();
        var command = (DbCommand) A.Fake<StubCommand>();
        var dbMetadata = new DbMetadata();
        A.CallTo(() => dbProvider.Metadata).Returns(dbMetadata);
        A.CallTo(() => dbProvider.CreateCommand()).Returns(command);

        var dataReader = FakeReader();
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

        A.CallTo(() => dataReader.ReadAsync(CancellationToken.None)).Returns(true);

        A.CallTo(() => dataReader[AdoConstants.ColumnTriggerType]).Returns(AdoConstants.TriggerTypeBlob);
        A.CallTo(() => dataReader[AdoConstants.ColumnJobName]).Returns("testJob");
        A.CallTo(() => dataReader[AdoConstants.ColumnJobGroup]).Returns("DEFAULT");
        A.CallTo(() => dataReader[AdoConstants.ColumnDescription]).Returns(DBNull.Value);
        A.CallTo(() => dataReader[AdoConstants.ColumnCalendarName]).Returns(DBNull.Value);
        A.CallTo(() => dataReader[AdoConstants.ColumnMisfireInstruction]).Returns(1);
        A.CallTo(() => dataReader[AdoConstants.ColumnPriority]).Returns(5);
        A.CallTo(() => dataReader[AdoConstants.ColumnNextFireTime]).Returns(nextFireTime.UtcTicks);
        A.CallTo(() => dataReader[AdoConstants.ColumnPreviousFireTime]).Returns(DBNull.Value);
        A.CallTo(() => dataReader[AdoConstants.ColumnStartTime]).Returns(DateTimeOffset.UtcNow.UtcTicks);
        A.CallTo(() => dataReader[AdoConstants.ColumnEndTime]).Returns(DBNull.Value);
        A.CallTo(() => dataReader[AdoConstants.ColumnMisfireOriginalFireTime]).Returns(misfireOrigFireTime.UtcTicks);
        A.CallTo(() => dataReader.IsDBNull(11)).Returns(true);
        // Preferred node auto-claim column reads as absent (distinct ordinal, no collision)
        A.CallTo(() => dataReader.GetOrdinal(AdoConstants.ColumnPreferredNodeAuto)).Returns(20);
        A.CallTo(() => dataReader.IsDBNull(20)).Returns(true);

        var blobTrigger = new SimpleTriggerImpl
        {
            Key = new TriggerKey("test", "DEFAULT"),
            JobKey = new JobKey("testJob", "DEFAULT"),
        };

        var adoDelegate = new BlobTriggerOverrideDelegate(blobTrigger);
        adoDelegate.Initialize(new DriverDelegateContext
        {
            TablePrefix = "QRTZ_",
            InstanceId = "TESTSCHED",
            SchedulerName = "INSTANCE",
            TypeLoader = new SimpleTypeLoader(),
            UseProperties = false,
            DbProvider = dbProvider
        });

        var conn = new ConnectionAndTransactionHolder(connection, transaction);
        IOperableTrigger trigger = await adoDelegate.SelectTrigger(conn, new TriggerKey("test"));

        Assert.That(trigger, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(trigger.NextFireTimeUtc, Is.EqualTo(nextFireTime));
            Assert.That(trigger.MisfireInstructionCode, Is.EqualTo(1));
            Assert.That(((TriggerBase) trigger).MisfiredFromFireTimeUtc, Is.EqualTo(misfireOrigFireTime));
        });
    }

    [Test]
    public void ShouldAddTriggerPersistenceDelegatesFromInitializationArgs()
    {
        StdAdoDelegate adoDelegate = new TestStdAdoDelegate(new SimpleTriggerPersistenceDelegate());

        var driverDelegateContext = new DriverDelegateContext
        {
            TablePrefix = "QRTZ_",
            InstanceId = "TESTSCHED",
            SchedulerName = "INSTANCE",
            TypeLoader = new SimpleTypeLoader(),
            UseProperties = false,
            TriggerPersistenceDelegates = [new TestTriggerPersistenceDelegate(), new TestTriggerPersistenceDelegate()],
            DbProvider = A.Fake<IDbProvider>()
        };

        var act = () => adoDelegate.Initialize(driverDelegateContext);

        act.Should().NotThrow("the registered set arrives typed, the same delegate twice included");
    }

    /// <summary>
    /// A trigger materialized from its rows computes its misfires against the clock the store runs on,
    /// whatever type of trigger it turns out to be. The store selects a misfired trigger by its own
    /// <see cref="TimeProvider" />; if the trigger it hands back reads a different one, the recovery is
    /// arithmetic on two clocks.
    /// </summary>
    [TestCaseSource(nameof(EveryTriggerTypeAStoreReadsBack))]
    public async Task ATriggerReadBackCarriesTheStoresClock(string discriminator, IScheduleBuilder schedule)
    {
        FakeTimeProvider clock = new FakeTimeProvider(new DateTimeOffset(2024, 3, 31, 1, 15, 0, TimeSpan.Zero));

        ITriggerPersistenceDelegate persistenceDelegate = A.Fake<ITriggerPersistenceDelegate>();
        A.CallTo(() => persistenceDelegate.ReadTriggerPropertyBundle(A<DbDataReader>._))
            .Returns(new TriggerPropertyBundle(schedule));
        A.CallTo(() => persistenceDelegate.LoadExtendedTriggerProperties(A<ConnectionAndTransactionHolder>._, A<TriggerKey>._, A<CancellationToken>._))
            .Returns(new ValueTask<TriggerPropertyBundle>(new TriggerPropertyBundle(schedule)));

        StdAdoDelegate adoDelegate = new TestStdAdoDelegate(persistenceDelegate);
        DbDataReader dataReader = InitializeForTriggerRead(adoDelegate, clock, out ConnectionAndTransactionHolder conn);
        DescribeTriggerRow(dataReader, discriminator);

        IOperableTrigger trigger = await adoDelegate.SelectTrigger(conn, new TriggerKey("read-back", "DEFAULT"));

        trigger.Should().BeAssignableTo<TriggerBase>()
            .Which.TimeProvider.Should().BeSameAs(clock,
                "the store built this {0} trigger out of rows, so the only clock it can have is the store's",
                discriminator);
    }

    /// <summary>
    /// And a trigger that came out of BLOB_TRIGGERS, which is the one case where the store cannot have
    /// built it: the clock does not serialize, so a deserialized trigger arrives on the system clock
    /// and the store has to say otherwise.
    /// </summary>
    [Test]
    public async Task ABlobTriggerReadBackCarriesTheStoresClock()
    {
        FakeTimeProvider clock = new FakeTimeProvider(new DateTimeOffset(2024, 3, 31, 1, 15, 0, TimeSpan.Zero));

        // No clock, exactly as deserialization leaves one.
        SimpleTriggerImpl deserialized = new SimpleTriggerImpl
        {
            Key = new TriggerKey("read-back", "DEFAULT"),
            JobKey = new JobKey("testJob", "DEFAULT")
        };

        deserialized.TimeProvider.Should().BeSameAs(TimeProvider.System,
            "the premise: a trigger nobody handed a clock reads the machine's");

        StdAdoDelegate adoDelegate = new BlobTriggerOverrideDelegate(deserialized);
        DbDataReader dataReader = InitializeForTriggerRead(adoDelegate, clock, out ConnectionAndTransactionHolder conn);
        DescribeTriggerRow(dataReader, AdoConstants.TriggerTypeBlob);

        IOperableTrigger trigger = await adoDelegate.SelectTrigger(conn, new TriggerKey("read-back", "DEFAULT"));

        trigger.Should().BeSameAs(deserialized, "the blob path hands back the object it deserialized");
        ((TriggerBase) trigger).TimeProvider.Should().BeSameAs(clock,
            "the store is the only thing that can give a deserialized trigger a clock, and it has to");
    }

    /// <summary>
    /// One row per trigger type a shipped persistence delegate handles, each with the schedule builder
    /// that delegate returns for it — which is what decides the trigger implementation
    /// <c>BuildTrigger</c> ends up with.
    /// </summary>
    private static IEnumerable<TestCaseData> EveryTriggerTypeAStoreReadsBack()
    {
        yield return new TestCaseData(AdoConstants.TriggerTypeCron, CronScheduleBuilder.Create("0 30 * * * ?"));
        yield return new TestCaseData(AdoConstants.TriggerTypeSimple, SimpleScheduleBuilder.Create().WithInterval(TimeSpan.FromHours(1)).RepeatForever());
        yield return new TestCaseData(AdoConstants.TriggerTypeCalendarInterval, CalendarIntervalScheduleBuilder.Create().WithInterval(1, IntervalUnit.Day));
        yield return new TestCaseData(AdoConstants.TriggerTypeDailyTimeInterval, DailyTimeIntervalScheduleBuilder.Create().WithInterval(15, IntervalUnit.Minute));
        yield return new TestCaseData(AdoConstants.TriggerTypeRecurrence, RecurrenceScheduleBuilder.Create("FREQ=DAILY"));
    }

    /// <summary>
    /// Puts a delegate on a faked provider and initializes it with the given clock, handing back the
    /// reader its statements will read from.
    /// </summary>
    private DbDataReader InitializeForTriggerRead(StdAdoDelegate adoDelegate, TimeProvider timeProvider, out ConnectionAndTransactionHolder conn)
    {
        IDbProvider dbProvider = A.Fake<IDbProvider>();
        DbCommand command = (DbCommand) A.Fake<StubCommand>();
        A.CallTo(() => dbProvider.Metadata).Returns(new DbMetadata());
        A.CallTo(() => dbProvider.CreateCommand()).Returns(command);

        DbDataReader dataReader = FakeReader();
        A.CallTo(command).Where(x => x.Method.Name == "ExecuteDbDataReaderAsync")
            .WithReturnType<Task<DbDataReader>>()
            .Returns(Task.FromResult(dataReader));
        A.CallTo(command).Where(x => x.Method.Name == "get_DbParameterCollection")
            .WithReturnType<DbParameterCollection>()
            .Returns(new StubParameterCollection());
        A.CallTo(command).Where(x => x.Method.Name == "CreateDbParameter")
            .WithReturnType<DbParameter>()
            .Returns(new SqlParameter());
        A.CallTo(() => command.CommandText).Returns("");

        adoDelegate.Initialize(new DriverDelegateContext
        {
            TablePrefix = "QRTZ_",
            InstanceId = "TESTSCHED",
            SchedulerName = "INSTANCE",
            TypeLoader = new SimpleTypeLoader(),
            UseProperties = false,
            DbProvider = dbProvider,
            ObjectSerializer = serializer,
            TimeProvider = timeProvider
        });

        conn = new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), A.Fake<DbTransaction>());
        return dataReader;
    }

    /// <summary>
    /// One TRIGGERS row of the given type, with nothing on it that the clock question depends on.
    /// </summary>
    private static void DescribeTriggerRow(DbDataReader dataReader, string discriminator)
    {
        A.CallTo(() => dataReader.ReadAsync(CancellationToken.None)).Returns(true);

        A.CallTo(() => dataReader[AdoConstants.ColumnTriggerType]).Returns(discriminator);
        A.CallTo(() => dataReader[AdoConstants.ColumnJobName]).Returns("testJob");
        A.CallTo(() => dataReader[AdoConstants.ColumnJobGroup]).Returns("DEFAULT");
        A.CallTo(() => dataReader[AdoConstants.ColumnDescription]).Returns(DBNull.Value);
        A.CallTo(() => dataReader[AdoConstants.ColumnCalendarName]).Returns(DBNull.Value);
        A.CallTo(() => dataReader[AdoConstants.ColumnMisfireInstruction]).Returns(MisfireInstruction.SmartPolicy);
        A.CallTo(() => dataReader[AdoConstants.ColumnPriority]).Returns(5);
        A.CallTo(() => dataReader[AdoConstants.ColumnNextFireTime]).Returns(new DateTimeOffset(2024, 3, 31, 0, 30, 0, TimeSpan.Zero).UtcTicks);
        A.CallTo(() => dataReader[AdoConstants.ColumnPreviousFireTime]).Returns(DBNull.Value);
        A.CallTo(() => dataReader[AdoConstants.ColumnStartTime]).Returns(new DateTimeOffset(2024, 3, 30, 0, 30, 0, TimeSpan.Zero).UtcTicks);
        A.CallTo(() => dataReader[AdoConstants.ColumnEndTime]).Returns(DBNull.Value);
        A.CallTo(() => dataReader[AdoConstants.ColumnMisfireOriginalFireTime]).Returns(DBNull.Value);

        // The job data map column reads as absent, and so does the preferred-node auto-claim flag;
        // both are read by literal ordinal, well below the ones FakeReader hands out.
        A.CallTo(() => dataReader.IsDBNull(11)).Returns(true);
        A.CallTo(() => dataReader.GetOrdinal(AdoConstants.ColumnPreferredNodeAuto)).Returns(20);
        A.CallTo(() => dataReader.IsDBNull(20)).Returns(true);
    }

    /// <summary>
    /// A faked reader whose ordinal-taking reads resolve back to the columns a test stubs by name.
    /// </summary>
    /// <remarks>
    /// The row readers ask for a column's position once and then read by position, which a fake stubbed
    /// only by name cannot answer. Positions are handed out on first sight of a name and start well
    /// above the handful of literal ordinals the tests below stub for themselves, so the two cannot
    /// collide.
    /// </remarks>
    private static DbDataReader FakeReader()
    {
        const int firstOrdinal = 100;

        DbDataReader reader = A.Fake<DbDataReader>();
        List<string> columns = [];
        Dictionary<string, int> ordinals = new(StringComparer.Ordinal);

        int OrdinalOf(string name)
        {
            if (!ordinals.TryGetValue(name, out int ordinal))
            {
                ordinal = firstOrdinal + columns.Count;
                columns.Add(name);
                ordinals[name] = ordinal;
            }

            return ordinal;
        }

        object ValueAt(int ordinal)
        {
            int index = ordinal - firstOrdinal;
            object value = index >= 0 && index < columns.Count ? reader[columns[index]] : null;

            // A column the test did not describe is one the row does not have. FakeItEasy answers an
            // unconfigured object-returning member with a dummy proxy, which lives in a dynamic
            // assembly and is nothing a reader would ever hand back.
            return value is null || value.GetType().Assembly.IsDynamic ? DBNull.Value : value;
        }

        A.CallTo(() => reader.GetOrdinal(A<string>._)).ReturnsLazily((string name) => OrdinalOf(name));
        A.CallTo(() => reader.GetValue(A<int>._)).ReturnsLazily((int ordinal) => ValueAt(ordinal));
        A.CallTo(() => reader.IsDBNull(A<int>._)).ReturnsLazily((int ordinal) => ValueAt(ordinal) is DBNull);
        A.CallTo(() => reader.GetString(A<int>._)).ReturnsLazily((int ordinal) => ValueAt(ordinal) as string);

        return reader;
    }

    private sealed class TestStdAdoDelegate : StdAdoDelegate
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

    /// <summary>
    /// Test subclass that bypasses actual blob deserialization to return a pre-built trigger,
    /// allowing tests to verify that SelectTrigger sets fire times from DB columns on blob triggers.
    /// </summary>
    private sealed class BlobTriggerOverrideDelegate : StdAdoDelegate
    {
        private readonly IOperableTrigger blobTrigger;

        public BlobTriggerOverrideDelegate(IOperableTrigger blobTrigger)
        {
            this.blobTrigger = blobTrigger;
        }

#pragma warning disable CS8632
        protected override ValueTask<T?> GetObjectFromBlob<T>(DbDataReader rs, int colIndex, CancellationToken cancellationToken = default) where T : class
        {
            if (typeof(T) == typeof(IOperableTrigger))
            {
                return new ValueTask<T?>((T?) (object) blobTrigger);
            }
            return base.GetObjectFromBlob<T>(rs, colIndex, cancellationToken);
        }
#pragma warning restore CS8632
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

/// <summary>
/// A user-authored persistence delegate, only ever named by assembly-qualified name and never
/// invoked — the built-in delegates are sealed, so the shape a real extension would take is a
/// subclass of the public support base.
/// </summary>
internal sealed class TestTriggerPersistenceDelegate : SimplePropertiesTriggerPersistenceDelegateBase
{
    public override bool CanHandleTriggerType(IOperableTrigger trigger) => false;

    public override string GetHandledTriggerTypeDiscriminator() => "TEST";

    protected override SimplePropertiesTriggerProperties GetTriggerProperties(IOperableTrigger trigger)
    {
        throw new NotSupportedException();
    }

    protected override TriggerPropertyBundle GetTriggerPropertyBundle(SimplePropertiesTriggerProperties properties)
    {
        throw new NotSupportedException();
    }
}
