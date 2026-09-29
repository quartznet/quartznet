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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using FakeItEasy;

using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Simpl;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// One trigger's fire failing on the database undoes that fire alone, and the rest of the batch commits
/// as it is reported (#3931).
/// </summary>
/// <remarks>
/// <para>
/// A fire writes several rows: the fired row, the <c>BLOCKED</c> state of a
/// <see cref="DisallowConcurrentExecutionAttribute" /> job's other triggers, the trigger row. A statement
/// that fails partway used to leave the writes before it in the transaction, and the batch committed
/// them: on SQL Server a job's other triggers stayed <c>BLOCKED</c> with nothing executing to let go of
/// them; on PostgreSQL the failure aborted the transaction, every fire after it failed too, and the
/// commit — a rollback in disguise — took the fires reported <em>before</em> it with it, underneath the
/// jobs the scheduler was about to run.
/// </para>
/// <para>
/// The first test drives one store by hand through a batch whose middle trigger fails, on the engine's
/// own delegate with the failure injected after the fire's writes, and reads the rows. The second runs
/// a scheduler beside a trigger whose every fire fails and watches the rest of its batch keep firing.
/// </para>
/// <para>
/// Uses the assembly-wide database of the dialect a derived fixture names, with
/// <see cref="SchedulerName" /> as the only isolation axis within it.
/// </para>
/// </remarks>
[NonParallelizable]
public abstract class TriggerFireFailureTestBase
{
    private const string SchedulerName = "TriggerFireFailureTest";
    private const string Group = "fireFailure";
    private const string Node = "fire-failure-node";
    private static readonly JobKey SerialJobKey = new JobKey("serial", Group);
    private static readonly JobKey OrdinaryJobKey = new JobKey("ordinary", Group);

    /// <summary>The <c>quartz.dataSource.default.provider</c> of the dialect.</summary>
    protected abstract string Provider { get; }

    /// <summary>The dialect's shipped delegate, failing the fire it is told to: see <see cref="FireFault" />.</summary>
    protected abstract Type FaultingDelegate { get; }

    /// <summary>The assembly-wide database of the dialect.</summary>
    protected abstract string ConnectionString { get; }

    /// <summary>A connection to <see cref="ConnectionString" />, for reading rows and the clean-up.</summary>
    protected abstract DbConnection CreateConnection();

    [SetUp]
    public async Task ResetFault()
    {
        FireFault.Reset();
        NamingJob.Reset();
        await DeleteThisSchedulersRows();
    }

    [TearDown]
    public async Task CleanUp()
    {
        FireFault.Reset();
        await DeleteThisSchedulersRows();
    }

    /// <summary>
    /// A batch of three, the middle one failing: the two beside it commit as fired, the failed one's
    /// writes are gone, and the failed trigger's job-mate is not left <c>BLOCKED</c>.
    /// </summary>
    [Test]
    public async Task AFailedFireIsRolledBackAndTheRestOfTheBatchCommitsAsReported()
    {
        // Initialized, never started: every step is a store call awaited in sequence.
        JobStoreTX store = await BuildStore();
        try
        {
            await store.StoreJob(JobBuilder.Create<SerialNamingJob>().WithIdentity(SerialJobKey).StoreDurably().Build(), replaceExisting: true);
            await store.StoreJob(JobBuilder.Create<NamingJob>().WithIdentity(OrdinaryJobKey).StoreDurably().Build(), replaceExisting: true);

            // Ahead of now by a known margin, so the misfire cutoff stays out of the read and the fire-time
            // order is the one below: ordinary-1, poison, ordinary-2, sibling. The sibling is the poison
            // trigger's job-mate — what the poison's fire moves to BLOCKED and must not leave there.
            DateTimeOffset due = DateTimeOffset.UtcNow.AddSeconds(30);
            await Schedule(store, "ordinary-1", OrdinaryJobKey, due);
            await Schedule(store, "poison", SerialJobKey, due.AddMilliseconds(1));
            await Schedule(store, "ordinary-2", OrdinaryJobKey, due.AddMilliseconds(2));
            await Schedule(store, "sibling", SerialJobKey, due.AddMilliseconds(3));

            // Wide enough that triggers due milliseconds apart make one batch; a batch ends at the first
            // trigger's fire time plus the window.
            List<IOperableTrigger> acquired = (await store.AcquireNextTriggers(due.AddMinutes(1), 4, TimeSpan.FromSeconds(5))).ToList();
            acquired.Select(x => x.Key.Name).Should().Equal(new[] { "ordinary-1", "poison", "ordinary-2" },
                "a batch takes one trigger of a serial job, so the sibling stays behind");

            FireFault.FailFireOf = "poison";

            List<TriggerFiredResult> results = (await store.TriggersFired(acquired)).ToList();

            results.Should().HaveCount(3, "one answer per trigger, in the order asked");
            results[0].TriggerFiredBundle.Should().NotBeNull("ordinary-1 fired before the failure");

            // Read before the rest of the results are looked at, because it is the issue's PostgreSQL
            // question: there the failure aborted the transaction and the commit became a rollback, so a
            // fire reported to the scheduler had no row behind it by the time the job ran.
            (await FiredState("ordinary-1")).Should().Be("EXECUTING",
                "a result reported fired is a fire that committed; on PostgreSQL the failure used to abort the transaction, "
                + "and the commit became a rollback underneath this reported fire");

            results[1].TriggerFiredBundle.Should().BeNull("the poison fire failed");
            results[1].Exception.Should().NotBeNull("and says so, which is what the scheduler releases the trigger on");
            results[2].TriggerFiredBundle.Should().NotBeNull(
                "ordinary-2 fired after the failure; on PostgreSQL every statement after a failed one is refused until the "
                + "transaction ends, so this is the answer that says the failure was undone rather than carried");

            (await TriggerState("sibling")).Should().Be("WAITING",
                "the poison fire's BLOCKED of its job-mates went with the fire; left BLOCKED, nothing executing would ever let go of it");
            (await TriggerState("poison")).Should().Be("ACQUIRED", "the reservation is the scheduler's to release, not the store's");
            (await FiredState("poison")).Should().Be("ACQUIRED", "its fired row is the reservation as acquisition wrote it, the fire's update undone");
            (await FiredState("ordinary-2")).Should().Be("EXECUTING");
            FireFault.FireAttempts.Should().Equal(new[] { "ordinary-1", "poison", "ordinary-1", "ordinary-2" },
                "the attempt that met the failure is rolled back whole, and the batch is fired again without the failed trigger");

            // What the scheduler thread does with a failed result.
            await store.ReleaseAcquiredTrigger(acquired[1]);

            (await TriggerState("poison")).Should().Be("WAITING", "released, for the next acquisition to pick up");
            (await CountRows("SELECT COUNT(*) FROM QRTZ_FIRED_TRIGGERS WHERE SCHED_NAME = @schedulerName AND TRIGGER_NAME = @name", "poison"))
                .Should().Be(0);
            (await FiredState("ordinary-1")).Should().Be("EXECUTING", "the release lets go of the reservation it was asked to, and nothing else");
        }
        finally
        {
            await store.Shutdown();
        }
    }

    /// <summary>
    /// A scheduler with a trigger whose every fire fails: the ordinary trigger in the same batch keeps
    /// firing on schedule, the poison trigger never runs and is stored <c>ERROR</c> after the failures in
    /// a row the store allows, its job-mate fires after that, and nothing is left <c>BLOCKED</c> once the
    /// scheduler is down.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The poison trigger's fire time never advances, so it is first in the order every round and is in
    /// every batch, and its job-mate — one trigger of a serial job per batch — is skipped behind it for
    /// as long as it is acquired. Before #3963 that was for good: the scheduler released the failed
    /// trigger to be acquired again, however often it failed. Now it is stored <c>ERROR</c> on its fifth
    /// failure in a row, and the job-mate is first in the order.
    /// </para>
    /// <para>
    /// Before #3931 the job-mate was also moved to <c>BLOCKED</c> by each failed fire and left there, on
    /// SQL Server; on PostgreSQL the ordinary trigger, refused by the aborted transaction after the poison
    /// in every batch, never fired at all.
    /// </para>
    /// </remarks>
    [Test]
    public async Task ARunningSchedulerKeepsFiringTheRestOfTheBatchBesideATriggerWhoseFireKeepsFailing()
    {
        FireFault.FailFireOf = "poison";

        IScheduler node = await CreateScheduler();
        try
        {
            IJobDetail serialJob = JobBuilder.Create<SerialNamingJob>().WithIdentity(SerialJobKey).StoreDurably().Build();
            IJobDetail ordinaryJob = JobBuilder.Create<NamingJob>().WithIdentity(OrdinaryJobKey).StoreDurably().Build();
            await node.AddJob(serialJob, replace: true);
            await node.AddJob(ordinaryJob, replace: true);

            foreach ((string name, JobKey job) in new[] { ("poison", SerialJobKey), ("sibling", SerialJobKey), ("ordinary", OrdinaryJobKey) })
            {
                await node.ScheduleJob(TriggerBuilder.Create()
                    .WithIdentity(name, Group)
                    .ForJob(job)
                    .StartNow()
                    .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromSeconds(1)).RepeatForever())
                    .Build());
            }

            await node.Start();
            await Task.Delay(TimeSpan.FromSeconds(8));
        }
        finally
        {
            await node.Shutdown(waitForJobsToComplete: true);
        }

        List<string> firings = NamingJob.Firings.ToList();
        TestContext.Out.WriteLine("Firings: " + string.Join(", ", firings.GroupBy(x => x, StringComparer.Ordinal).Select(x => $"{x.Key}={x.Count()}")));
        TestContext.Out.WriteLine("Fires attempted: " + string.Join(", ", FireFault.FireAttempts));

        firings.Count(x => x == "ordinary").Should().BeGreaterThanOrEqualTo(5,
            "the ordinary trigger is due every second and shares every batch with the poison; the rest of a batch fires whatever "
            + "one trigger of it does, and on PostgreSQL it used to be refused by the aborted transaction, round after round");
        firings.Should().NotContain("poison", "a fire that fails is not a fire");
        (await TriggerState("poison")).Should().Be("ERROR",
            "its every fire failed, five times in a row, which is what JobStoreSupport.MaxConsecutiveFireFailures allows by default");
        FireFault.FireAttempts.Count(x => x == "poison").Should().Be(5, "stored ERROR on its fifth failure, it is not acquired again");
        firings.Should().Contain("sibling",
            "the poison's job-mate is behind it in the order and one trigger of a serial job per batch; it fires once the poison is stored ERROR");
        (await CountRows("SELECT COUNT(*) FROM QRTZ_TRIGGERS WHERE SCHED_NAME = @schedulerName AND TRIGGER_STATE = 'BLOCKED'"))
            .Should().Be(0, "nothing is executing after the shutdown, so nothing may be blocked");
    }

    private async Task<JobStoreTX> BuildStore()
    {
        string dataSource = $"trigger-fire-failure-{Guid.NewGuid():N}";
        DBConnectionManager.Instance.AddConnectionProvider(dataSource, new DbProvider(Provider, ConnectionString));

        SystemTextJsonObjectSerializer serializer = new SystemTextJsonObjectSerializer();
        serializer.Initialize();

        JobStoreTX store = new JobStoreTX
        {
            DataSource = dataSource,
            TablePrefix = "QRTZ_",
            InstanceName = SchedulerName,
            InstanceId = Node,
            DriverDelegateType = FaultingDelegate.AssemblyQualifiedName,
            ObjectSerializer = serializer,
            // TRIGGER_ACCESS taken on the database, inside the batch's transaction, as a clustered node
            // takes it.
            UseDBLocks = true,
        };

        await store.Initialize(new SimpleTypeLoadHelper(), A.Fake<ISchedulerSignaler>());
        return store;
    }

    private async Task<IScheduler> CreateScheduler()
    {
        NameValueCollection properties = new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = SchedulerName,
            ["quartz.scheduler.instanceId"] = Node,
            // Batched, so the poison trigger shares its batch with the ordinary one.
            ["quartz.scheduler.batchTriggerAcquisitionMaxCount"] = "4",
            ["quartz.threadPool.threadCount"] = "4",
            ["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz",
            ["quartz.jobStore.driverDelegateType"] = FaultingDelegate.AssemblyQualifiedName,
            ["quartz.jobStore.dataSource"] = "default",
            ["quartz.jobStore.tablePrefix"] = "QRTZ_",
            ["quartz.jobStore.clustered"] = "true",
            ["quartz.jobStore.clusterCheckinInterval"] = "1000",
            ["quartz.dataSource.default.provider"] = Provider,
            ["quartz.dataSource.default.connectionString"] = ConnectionString,
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType,
        };

        StdSchedulerFactory factory = new StdSchedulerFactory(properties);
        IScheduler scheduler = await factory.GetScheduler();

        // The repository's lookup is name-only, so a scheduler left bound would be handed back to the
        // next fixture that asks for this name.
        SchedulerRepository.Instance.Remove(SchedulerName, scheduler.SchedulerInstanceId);

        return scheduler;
    }

    private static Task Schedule(JobStoreTX store, string name, JobKey job, DateTimeOffset at)
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(name, Group)
            .ForJob(job)
            .StartAt(at)
            .Build();

        // What the scheduler does with a trigger before handing it to the store.
        trigger.ComputeFirstFireTimeUtc(null);
        return store.StoreTrigger(trigger, replaceExisting: false);
    }

    private Task<string> TriggerState(string triggerName)
    {
        return ReadString("SELECT TRIGGER_STATE FROM QRTZ_TRIGGERS WHERE SCHED_NAME = @schedulerName AND TRIGGER_NAME = @name", triggerName);
    }

    private Task<string> FiredState(string triggerName)
    {
        return ReadString("SELECT STATE FROM QRTZ_FIRED_TRIGGERS WHERE SCHED_NAME = @schedulerName AND TRIGGER_NAME = @name", triggerName);
    }

    private async Task<string> ReadString(string sql, string triggerName)
    {
        object result = await ExecuteScalar(sql, triggerName);
        return result is null or DBNull ? null : ((string) result).Trim();
    }

    private async Task<int> CountRows(string sql, string triggerName = null)
    {
        object result = await ExecuteScalar(sql, triggerName);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private async Task<object> ExecuteScalar(string sql, string triggerName)
    {
        using DbConnection connection = CreateConnection();
        await connection.OpenAsync();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "schedulerName", SchedulerName);
        if (triggerName != null)
        {
            AddParameter(command, "name", triggerName);
        }

        return await command.ExecuteScalarAsync();
    }

    private async Task DeleteThisSchedulersRows()
    {
        // A clustered node's own SCHEDULER_STATE row survives Shutdown, and the running test's triggers
        // repeat forever, so every row of this fixture's scheduler goes, one statement per round trip.
        string[] tables =
        {
            "QRTZ_FIRED_TRIGGERS", "QRTZ_SIMPLE_TRIGGERS", "QRTZ_CRON_TRIGGERS", "QRTZ_SIMPROP_TRIGGERS",
            "QRTZ_BLOB_TRIGGERS", "QRTZ_TRIGGERS", "QRTZ_JOB_DETAILS", "QRTZ_CALENDARS",
            "QRTZ_PAUSED_TRIGGER_GRPS", "QRTZ_SCHEDULER_STATE",
        };

        using DbConnection connection = CreateConnection();
        await connection.OpenAsync();
        foreach (string table in tables)
        {
            using DbCommand command = connection.CreateCommand();
            command.CommandText = "DELETE FROM " + table + " WHERE SCHED_NAME = @schedulerName";
            AddParameter(command, "schedulerName", SchedulerName);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    /// <summary>
    /// Records the name of the trigger that fired it.
    /// </summary>
    public class NamingJob : IJob
    {
        private static volatile ConcurrentQueue<string> firings = new ConcurrentQueue<string>();

        public static ConcurrentQueue<string> Firings => firings;

        public static void Reset() => Interlocked.Exchange(ref firings, new ConcurrentQueue<string>());

        public Task Execute(IJobExecutionContext context)
        {
            Firings.Enqueue(context.Trigger.Key.Name);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// The same, for a job whose fire moves its other triggers to <c>BLOCKED</c>.
    /// </summary>
    [DisallowConcurrentExecution]
    public sealed class SerialNamingJob : NamingJob
    {
    }
}
