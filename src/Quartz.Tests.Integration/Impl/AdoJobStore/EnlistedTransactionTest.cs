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

using System.Collections.Specialized;
using System.Data.Common;
using System.Transactions;

using Microsoft.Data.SqlClient;

using Npgsql;

using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Jobs;
using Quartz.Util;

using IsolationLevel = System.Transactions.IsolationLevel;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// Verifies that scheduling can take part in a transaction the application owns: the schedule is
/// committed together with the application's own work, and is discarded together with it when that
/// work is rolled back. Reported as https://github.com/quartznet/quartznet/issues/2038.
/// </summary>
[NonParallelizable]
public class EnlistedTransactionTest
{
    [Test]
    [Category("db-postgres")]
    public Task PostgresRollingBackTheApplicationTransactionDiscardsTheSchedule()
    {
        return RollingBackTheApplicationTransactionDiscardsTheSchedule(Postgres(), "EnlistedRollbackPg");
    }

    [Test]
    [Category("db-postgres")]
    public Task PostgresCommittingTheApplicationTransactionKeepsTheSchedule()
    {
        return CommittingTheApplicationTransactionKeepsTheSchedule(Postgres(), "EnlistedCommitPg");
    }

    [Test]
    [Category("db-postgres")]
    public Task PostgresIncompleteAmbientScopeDiscardsTheSchedule()
    {
        return IncompleteAmbientScopeDiscardsTheSchedule(Postgres(), "AmbientScopePg");
    }

    [Test]
    [Category("db-postgres")]
    public Task PostgresRunningSchedulerFiresTheJobRightAfterTheApplicationCommits()
    {
        return RunningSchedulerFiresTheJobRightAfterTheApplicationCommits(Postgres(), "EnlistedRunningPg");
    }

    [Test]
    [Category("db-postgres")]
    public Task PostgresRunningSchedulerFiresTheJobAfterAnAmbientScopeCompletes()
    {
        return RunningSchedulerFiresTheJobAfterAnAmbientScopeCompletes(Postgres(), "AmbientRunningPg");
    }

    [Test]
    [Category("db-postgres")]
    public Task PostgresEnlistingIsRefusedWhenTheStoreDoesNotAcceptIt()
    {
        return EnlistingIsRefusedWhenTheStoreDoesNotAcceptIt(Postgres(), "EnlistedRefusedPg");
    }

    [Test]
    [Category("db-sqlserver")]
    public Task SqlServerRollingBackTheApplicationTransactionDiscardsTheSchedule()
    {
        return RollingBackTheApplicationTransactionDiscardsTheSchedule(SqlServer(), "EnlistedRollbackMssql");
    }

    [Test]
    [Category("db-sqlserver")]
    public Task SqlServerCommittingTheApplicationTransactionKeepsTheSchedule()
    {
        return CommittingTheApplicationTransactionKeepsTheSchedule(SqlServer(), "EnlistedCommitMssql");
    }

    [Test]
    [Category("db-sqlserver")]
    public Task SqlServerIncompleteAmbientScopeDiscardsTheSchedule()
    {
        return IncompleteAmbientScopeDiscardsTheSchedule(SqlServer(), "AmbientScopeMssql");
    }

    private static async Task RollingBackTheApplicationTransactionDiscardsTheSchedule(
        TestProvider provider,
        string schedulerName)
    {
        IScheduler scheduler = await CreateScheduler(provider, schedulerName);
        JobKey jobKey = new JobKey("enlisted", schedulerName);

        try
        {
            await scheduler.Clear();

            using (DbConnection connection = provider.CreateConnection())
            {
                await connection.OpenAsync();
                using (DbTransaction transaction = connection.BeginTransaction())
                {
                    using (scheduler.EnlistTransaction(transaction))
                    {
                        await scheduler.ScheduleJob(CreateJob(jobKey), CreateTrigger(jobKey));

                        bool visibleInsideTheTransaction = await scheduler.CheckExists(jobKey);
                        visibleInsideTheTransaction.Should().BeTrue("the job store wrote through the enlisted transaction");
                    }

                    transaction.Rollback();
                }
            }

            bool survivedTheRollback = await scheduler.CheckExists(jobKey);
            survivedTheRollback.Should().BeFalse("the schedule belongs to the transaction the application rolled back");
        }
        finally
        {
            await scheduler.Shutdown(false);
        }
    }

    private static async Task CommittingTheApplicationTransactionKeepsTheSchedule(
        TestProvider provider,
        string schedulerName)
    {
        IScheduler scheduler = await CreateScheduler(provider, schedulerName);
        JobKey jobKey = new JobKey("enlisted", schedulerName);

        try
        {
            await scheduler.Clear();

            using (DbConnection connection = provider.CreateConnection())
            {
                await connection.OpenAsync();
                using (DbTransaction transaction = connection.BeginTransaction())
                {
                    using (scheduler.EnlistTransaction(transaction))
                    {
                        await scheduler.ScheduleJob(CreateJob(jobKey), CreateTrigger(jobKey));
                        transaction.Commit();
                    }
                }
            }

            IJobDetail persisted = await scheduler.GetJobDetail(jobKey);
            persisted.Should().NotBeNull("the application committed the transaction the schedule was written in");
            (await scheduler.GetTriggersOfJob(jobKey)).Should().HaveCount(1);

            await scheduler.Clear();
        }
        finally
        {
            await scheduler.Shutdown(false);
        }
    }

    private static async Task IncompleteAmbientScopeDiscardsTheSchedule(
        TestProvider provider,
        string schedulerName)
    {
        IScheduler scheduler = await CreateScheduler(provider, schedulerName);
        JobKey jobKey = new JobKey("ambient", schedulerName);

        try
        {
            await scheduler.Clear();

            TransactionOptions options = new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted };
            using (new TransactionScope(TransactionScopeOption.RequiresNew, options, TransactionScopeAsyncFlowOption.Enabled))
            {
                // Sharing the one connection is what keeps the scope from being promoted to a
                // distributed transaction, which Npgsql does not support at all.
                using (DbConnection connection = provider.CreateConnection())
                {
                    await connection.OpenAsync();

                    using (scheduler.EnlistConnection(connection))
                    {
                        await scheduler.ScheduleJob(CreateJob(jobKey), CreateTrigger(jobKey));
                    }
                }

                // scope is disposed without Complete, so the ambient transaction rolls back
            }

            bool survivedTheRollback = await scheduler.CheckExists(jobKey);
            survivedTheRollback.Should().BeFalse("the ambient transaction was never completed");
        }
        finally
        {
            await scheduler.Shutdown(false);
        }
    }

    /// <summary>
    /// The scheduler runs for real here, which is what exercises the parts the other tests cannot:
    /// the scheduler thread contending for the locks the application transaction holds, and the
    /// scheduling change signal that has to wait for the commit. A job scheduled to fire immediately
    /// must run promptly after the application commits - not an idle interval later.
    /// </summary>
    private static async Task RunningSchedulerFiresTheJobRightAfterTheApplicationCommits(
        TestProvider provider,
        string schedulerName)
    {
        IScheduler scheduler = await CreateScheduler(provider, schedulerName, idleWaitTime: TimeSpan.FromMinutes(2));
        JobKey jobKey = new JobKey("running", schedulerName);

        try
        {
            await scheduler.Clear();
            SignallingJob.Reset();
            await scheduler.Start();

            using (DbConnection connection = provider.CreateConnection())
            {
                await connection.OpenAsync();
                using (DbTransaction transaction = connection.BeginTransaction())
                {
                    IJobDetail job = JobBuilder.Create<SignallingJob>()
                        .WithIdentity(jobKey)
                        .StoreDurably()
                        .Build();

                    ITrigger trigger = TriggerBuilder.Create()
                        .WithIdentity(jobKey.Name, jobKey.Group)
                        .ForJob(jobKey)
                        .StartNow()
                        .Build();

                    using (scheduler.EnlistTransaction(transaction))
                    {
                        await scheduler.ScheduleJob(job, trigger);
                        transaction.Commit();
                    }
                }
            }

            // Comfortably below the two minute idle wait, so passing means the post-commit signal
            // woke the scheduler rather than the idle timer expiring.
            bool fired = SignallingJob.Fired.Wait(TimeSpan.FromSeconds(30));
            fired.Should().BeTrue("the scheduler must be signalled once the application's commit makes the trigger visible");

            await scheduler.Clear();
        }
        finally
        {
            await scheduler.Shutdown(true);
        }
    }

    /// <summary>
    /// The ambient half of the deferred signal: with a <see cref="TransactionScope" /> in play the
    /// enlistment scope closes first and the transaction itself reports the outcome, so the signal has
    /// to come from its completion rather than from disposing the enlistment.
    /// </summary>
    private static async Task RunningSchedulerFiresTheJobAfterAnAmbientScopeCompletes(
        TestProvider provider,
        string schedulerName)
    {
        IScheduler scheduler = await CreateScheduler(provider, schedulerName, idleWaitTime: TimeSpan.FromMinutes(2));
        JobKey jobKey = new JobKey("ambient-running", schedulerName);

        try
        {
            await scheduler.Clear();
            SignallingJob.Reset();
            await scheduler.Start();

            TransactionOptions options = new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted };
            using (TransactionScope scope = new TransactionScope(TransactionScopeOption.RequiresNew, options, TransactionScopeAsyncFlowOption.Enabled))
            {
                using (DbConnection connection = provider.CreateConnection())
                {
                    await connection.OpenAsync();

                    IJobDetail job = JobBuilder.Create<SignallingJob>()
                        .WithIdentity(jobKey)
                        .StoreDurably()
                        .Build();

                    ITrigger trigger = TriggerBuilder.Create()
                        .WithIdentity(jobKey.Name, jobKey.Group)
                        .ForJob(jobKey)
                        .StartNow()
                        .Build();

                    using (scheduler.EnlistConnection(connection))
                    {
                        await scheduler.ScheduleJob(job, trigger);
                    }

                    // The enlistment is already gone here, so only the scope completing can raise the signal.
                    SignallingJob.Fired.IsSet.Should().BeFalse("nothing is committed until the scope completes");
                }

                scope.Complete();
            }

            bool fired = SignallingJob.Fired.Wait(TimeSpan.FromSeconds(30));
            fired.Should().BeTrue("completing the ambient transaction must signal the scheduler");

            await scheduler.Clear();
        }
        finally
        {
            await scheduler.Shutdown(true);
        }
    }

    private static async Task EnlistingIsRefusedWhenTheStoreDoesNotAcceptIt(
        TestProvider provider,
        string schedulerName)
    {
        IScheduler scheduler = await CreateScheduler(provider, schedulerName, acceptEnlistedTransactions: false);

        try
        {
            using (DbConnection connection = provider.CreateConnection())
            {
                await connection.OpenAsync();
                using DbTransaction transaction = connection.BeginTransaction();

                Action enlist = () => scheduler.EnlistTransaction(transaction);

                enlist.Should().Throw<InvalidOperationException>()
                    .WithMessage("*acceptEnlistedTransactions*",
                        "silently ignoring the enlistment would let scheduling commit outside the application transaction");
            }
        }
        finally
        {
            await scheduler.Shutdown(false);
        }
    }

    private static IJobDetail CreateJob(JobKey jobKey)
    {
        return JobBuilder.Create<NoOpJob>()
            .WithIdentity(jobKey)
            .StoreDurably()
            .Build();
    }

    private static ITrigger CreateTrigger(JobKey jobKey)
    {
        return TriggerBuilder.Create()
            .WithIdentity(jobKey.Name, jobKey.Group)
            .ForJob(jobKey)
            .StartAt(DateTimeOffset.UtcNow.AddHours(1))
            .Build();
    }

    private static async Task<IScheduler> CreateScheduler(
        TestProvider provider,
        string schedulerName,
        bool acceptEnlistedTransactions = true,
        TimeSpan? idleWaitTime = null)
    {
        NameValueCollection properties = new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = schedulerName,
            ["quartz.scheduler.instanceId"] = "AUTO",
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType,
            ["quartz.jobStore.type"] = typeof(LocalTransactionJobStore).AssemblyQualifiedNameWithoutVersion(),
            ["quartz.jobStore.useProperties"] = "true",
            ["quartz.jobStore.dataSource"] = "default",
            ["quartz.jobStore.tablePrefix"] = "QRTZ_",
            ["quartz.jobStore.acceptEnlistedTransactions"] = acceptEnlistedTransactions.ToString().ToLowerInvariant(),
            ["quartz.jobStore.driverDelegateType"] = provider.DriverDelegateType.AssemblyQualifiedNameWithoutVersion(),
            ["quartz.dataSource.default.connectionString"] = provider.ConnectionString,
            ["quartz.dataSource.default.provider"] = provider.ProviderName,
            ["quartz.threadPool.maxConcurrency"] = "2"
        };

        if (idleWaitTime != null)
        {
            properties["quartz.scheduler.idleWaitTime"] = ((int) idleWaitTime.Value.TotalMilliseconds).ToString();
        }

        // Most of these tests never start the scheduler - they are about what reaches the database,
        // and a running scheduler thread would contend for the locks the application transaction
        // holds. RunningSchedulerFiresTheJobRightAfterTheApplicationCommits is the one that does.
        return await QuartzSchedulerBuilder.Create().UseProperties(properties).BuildScheduler();
    }

    private static TestProvider Postgres()
    {
        return new TestProvider(
            TestConstants.PostgresProvider,
            TestConstants.PostgresConnectionString,
            typeof(PostgreSQLDelegate),
            () => new NpgsqlConnection(TestConstants.PostgresConnectionString));
    }

    private static TestProvider SqlServer()
    {
        return new TestProvider(
            TestConstants.DefaultSqlServerProvider,
            TestConstants.SqlServerConnectionString,
            typeof(SqlServerDelegate),
            () => new SqlConnection(TestConstants.SqlServerConnectionString));
    }

    [DisallowConcurrentExecution]
    public sealed class SignallingJob : IJob
    {
        internal static readonly ManualResetEventSlim Fired = new ManualResetEventSlim(false);

        internal static void Reset() => Fired.Reset();

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Fired.Set();
            return default;
        }
    }

    private sealed class TestProvider
    {
        private readonly Func<DbConnection> connectionFactory;

        internal TestProvider(
            string providerName,
            string connectionString,
            Type driverDelegateType,
            Func<DbConnection> connectionFactory)
        {
            ProviderName = providerName;
            ConnectionString = connectionString;
            DriverDelegateType = driverDelegateType;
            this.connectionFactory = connectionFactory;
        }

        internal string ProviderName { get; }

        internal string ConnectionString { get; }

        internal Type DriverDelegateType { get; }

        internal DbConnection CreateConnection() => connectionFactory();
    }
}
