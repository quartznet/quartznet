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

using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// Runs a scheduler against a schema the migration scripts produced, rather than one a fresh install
/// produced.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="MigrationScriptTest" /> proves the migrated schema has the same tables, columns and
/// indexes as a fresh one. That is a statement about shape, and shape is not behaviour: a column can
/// exist under the right name and still be the wrong type, be missing its default, or sit in a table
/// the store cannot write through. So one trigger of every persisted family is scheduled here, fired,
/// read back and shut down, against the migrated tables.
/// </para>
/// <para>
/// The trigger set is chosen to touch what the migrations added rather than only what 3.16 already
/// had: the recurrence family, which 4.x introduced; an execution group, which 3.18 added a column
/// for; and a preferred-node pin, which 3.19 added two. The pin names this very node, so the trigger
/// is acquirable — <c>PREFERRED_NODE = @instanceId</c> is one of the clauses the acquire query
/// accepts — which makes the pin observable as a fire rather than only as a stored string.
/// </para>
/// <para>
/// The 4.0 migration also adds <c>RETRY_POLICY</c> and <c>RETRY_ATTEMPT</c>, which nothing in the API
/// reaches yet, so no trigger here can carry them. They are asserted directly against the migrated
/// table instead — see <see cref="AssertRetryColumnsArePresent" />.
/// </para>
/// <para>
/// Everything runs under the migrated table prefix while the fresh <c>QRTZ_</c> schema sits in the
/// same database, so the closing row counts also show the prefix actually isolated the two.
/// </para>
/// </remarks>
internal static class MigratedSchemaWorkload
{
    private static readonly TimeSpan FireTimeout = TimeSpan.FromSeconds(60);

    public static async Task RunAsync(DbConnection connection, string dialect, string connectionString, string tablePrefix)
    {
        string run = Guid.NewGuid().ToString("N");
        string schedulerName = $"MigratedSchema_{dialect}_{run}";
        string instanceId = $"node_{run}";
        string group = "migrated";

        JobKey jobKey = new JobKey("job", group);

        MigratedSchemaJob.Reset();

        IScheduler scheduler = await BuildScheduler(dialect, connectionString, tablePrefix, schedulerName, instanceId);

        IJobDetail job = JobBuilder.Create<MigratedSchemaJob>()
            .WithIdentity(jobKey)
            .UsingJobData("marker", "migrated")
            .Build();

        List<ITrigger> triggers = BuildTriggers(jobKey, group, instanceId);

        try
        {
            await scheduler.ScheduleJob(job, triggers, new ScheduleJobOptions { Replace = true });
            await scheduler.Start();

            await WaitForEveryTriggerToFire(triggers);

            await AssertRoundTrip(scheduler, jobKey, group, instanceId);
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }

        scheduler.Status.Should().Be(SchedulerStatus.Shutdown, "the scheduler has to come down cleanly on the migrated schema");

        await AssertPrefixIsolation(connection, tablePrefix, schedulerName, triggers.Count);
        await AssertRetryColumnsArePresent(connection, tablePrefix, schedulerName, triggers.Count);
    }

    /// <summary>
    /// One trigger of every family the ADO store has a persistence delegate for, all of them due
    /// immediately so the fixture is not waiting on a clock.
    /// </summary>
    private static List<ITrigger> BuildTriggers(JobKey jobKey, string group, string instanceId)
    {
        DateTimeOffset startAt = TimeProvider.System.GetUtcNow();

        return
        [
            // QRTZ_SIMPLE_TRIGGERS
            TriggerBuilder.Create()
                .WithIdentity("simple", group)
                .ForJob(jobKey)
                .StartAt(startAt)
                .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(1)).RepeatForever())
                .Build(),

            // QRTZ_CRON_TRIGGERS
            TriggerBuilder.Create()
                .WithIdentity("cron", group)
                .ForJob(jobKey)
                .StartAt(startAt)
                .WithCronSchedule("0/1 * * * * ?")
                .Build(),

            // QRTZ_SIMPROP_TRIGGERS, daily time interval flavour
            TriggerBuilder.Create()
                .WithIdentity("daily", group)
                .ForJob(jobKey)
                .StartAt(startAt)
                .WithDailyTimeIntervalSchedule(x => x
                    .WithInterval(1, IntervalUnit.Second)
                    .OnEveryDay()
                    .StartingDailyAt(new TimeOnly(0, 0, 0))
                    .EndingDailyAt(new TimeOnly(23, 59, 59)))
                .Build(),

            // QRTZ_SIMPROP_TRIGGERS, calendar interval flavour
            TriggerBuilder.Create()
                .WithIdentity("calendar", group)
                .ForJob(jobKey)
                .StartAt(startAt)
                .WithCalendarIntervalSchedule(x => x.WithInterval(1, IntervalUnit.Second))
                .Build(),

            // QRTZ_SIMPROP_TRIGGERS, RFC 5545 recurrence flavour -- new in 4.x, so it has never run
            // against anything but a fresh schema before
            TriggerBuilder.Create()
                .WithIdentity("recurrence", group)
                .ForJob(jobKey)
                .StartAt(startAt)
                .WithRecurrenceSchedule("FREQ=SECONDLY;INTERVAL=1")
                .Build(),

            // EXECUTION_GROUP (3.18) and PREFERRED_NODE / PREFERRED_NODE_AUTO (3.19), pinned to this
            // node so the pin has to be readable for the trigger to fire at all
            TriggerBuilder.Create()
                .WithIdentity("pinned", group)
                .ForJob(jobKey)
                .StartAt(startAt)
                .WithExecutionGroup("migrated-execution-group")
                .WithPreferredNode(PreferredNode.For(instanceId))
                .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(1)).RepeatForever())
                .Build()
        ];
    }

    private static async Task WaitForEveryTriggerToFire(List<ITrigger> triggers)
    {
        Stopwatch elapsed = Stopwatch.StartNew();

        while (elapsed.Elapsed < FireTimeout)
        {
            if (triggers.TrueForAll(t => MigratedSchemaJob.HasFired(t.Key)))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }

        List<string> missing = triggers
            .Where(t => !MigratedSchemaJob.HasFired(t.Key))
            .Select(t => t.Key.Name)
            .ToList();

        missing.Should().BeEmpty($"every trigger family should fire on the migrated schema within {FireTimeout.TotalSeconds:0} seconds");
    }

    /// <summary>
    /// Reads the job and its triggers back out of the migrated tables, which is where a column that
    /// migrated to the wrong type or lost its data shows up.
    /// </summary>
    private static async Task AssertRoundTrip(IScheduler scheduler, JobKey jobKey, string group, string instanceId)
    {
        IJobDetail storedJob = await scheduler.GetJobDetail(jobKey);
        storedJob.Should().NotBeNull("the job has to come back out of the migrated QRTZ_JOB_DETAILS");
        storedJob.JobDataMap.GetString("marker").Should().Be("migrated", "the job data blob has to survive the round trip");

        (await scheduler.GetTrigger(new TriggerKey("simple", group)))
            .Should().BeAssignableTo<ISimpleTrigger>();
        (await scheduler.GetTrigger(new TriggerKey("cron", group)))
            .Should().BeAssignableTo<ICronTrigger>();
        (await scheduler.GetTrigger(new TriggerKey("daily", group)))
            .Should().BeAssignableTo<IDailyTimeIntervalTrigger>();
        (await scheduler.GetTrigger(new TriggerKey("calendar", group)))
            .Should().BeAssignableTo<ICalendarIntervalTrigger>();
        (await scheduler.GetTrigger(new TriggerKey("recurrence", group)))
            .Should().BeAssignableTo<IRecurrenceTrigger>();

        ITrigger pinned = await scheduler.GetTrigger(new TriggerKey("pinned", group));
        pinned.Should().NotBeNull();
        pinned.ExecutionGroup.Should().Be("migrated-execution-group",
            "EXECUTION_GROUP is one of the columns the migration adds, so it has to hold what was written to it");
        pinned.PreferredNode.Node.Should().Be(instanceId,
            "PREFERRED_NODE is one of the columns the migration adds, so the pin has to survive being stored");
        pinned.PreferredNode.IsAutomatic.Should().BeFalse("an explicit pin is not an automatic one");
    }

    /// <summary>
    /// The fresh schema is sitting in the same database under <c>QRTZ_</c>. Nothing this scheduler
    /// wrote may have landed there.
    /// </summary>
    private static async Task AssertPrefixIsolation(DbConnection connection, string tablePrefix, string schedulerName, int triggerCount)
    {
        long migrated = await Count(connection, $"SELECT COUNT(*) FROM {tablePrefix}TRIGGERS WHERE SCHED_NAME = '{schedulerName}'");
        migrated.Should().Be(triggerCount, "every scheduled trigger belongs in the migrated tables");

        long fresh = await Count(connection, $"SELECT COUNT(*) FROM QRTZ_TRIGGERS WHERE SCHED_NAME = '{schedulerName}'");
        fresh.Should().Be(0, "the configured table prefix has to keep the scheduler out of the fresh schema");
    }

    /// <summary>
    /// RETRY_POLICY and RETRY_ATTEMPT are the two columns the 4.0 migration adds that no API member
    /// reaches yet, so they are asserted where they live rather than through a trigger.
    /// </summary>
    /// <remarks>
    /// Naming both columns is the existence check: the statement does not parse when one of them is
    /// missing, whatever the rows say. Requiring them null on every row the scheduler just wrote is
    /// the rest of the contract — nullable, no default — which a column that migrated as NOT NULL
    /// with a default would satisfy the first half of and fail here.
    /// </remarks>
    private static async Task AssertRetryColumnsArePresent(DbConnection connection, string tablePrefix, string schedulerName, int triggerCount)
    {
        long unset = await Count(connection,
            $"SELECT COUNT(*) FROM {tablePrefix}TRIGGERS WHERE SCHED_NAME = '{schedulerName}' "
            + "AND RETRY_POLICY IS NULL AND RETRY_ATTEMPT IS NULL");

        unset.Should().Be(triggerCount,
            "the migration has to leave RETRY_POLICY and RETRY_ATTEMPT on the migrated QRTZ_TRIGGERS, nullable and without a default");
    }

    private static async Task<long> Count(DbConnection connection, string sql)
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;

        object value = await command.ExecuteScalarAsync();
        return Convert.ToInt64(value);
    }

    private static async Task<IScheduler> BuildScheduler(
        string dialect,
        string connectionString,
        string tablePrefix,
        string schedulerName,
        string instanceId)
    {
        QuartzSchedulerBuilder builder = QuartzSchedulerBuilder.Create();

        builder.ConfigureScheduler(o =>
        {
            o.InstanceName = schedulerName;
            o.InstanceId = instanceId;
        });

        builder.UseDefaultThreadPool(x => x.MaxConcurrency = 5);

        builder.UsePersistentStore(store =>
        {
            store.ConfigureStore(o =>
            {
                o.TablePrefix = tablePrefix;

                // The migrated tables have to pass the same startup check a fresh install does, and a
                // mis-prefixed or missing one is reported by name rather than as a later failure.
                o.SchemaProvisioning = SchemaProvisioning.Validate;
            });

            UseDialect(store, dialect, connectionString);
            store.UseSystemTextJsonSerializer();
        });

        return await builder.BuildScheduler();
    }

    /// <summary>
    /// Points a store at the container running this dialect. Internal rather than private because
    /// <see cref="SchemaProvisioningTest" /> builds schedulers of its own against the same containers,
    /// and one mapping is one place for a dialect to be added.
    /// </summary>
    internal static void UseDialect(IPersistentStoreBuilder store, string dialect, string connectionString)
    {
        switch (dialect)
        {
            case "sqlite":
                store.UseSqlite(connectionString);
                break;
            case "sqlServer":
                store.UseSqlServer(connectionString);
                break;
            case "postgres":
                store.UsePostgres(connectionString);
                break;
            case "mysql_innodb":
                store.UseMySqlConnector(connectionString);
                break;
            case "oracle":
                store.UseOracle(connectionString);
                break;
            case "firebird":
                store.UseFirebird(connectionString);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(dialect), dialect, "no store configuration for this dialect");
        }
    }
}

/// <summary>
/// Records which trigger fired it. Public with a public constructor because the store hands the job
/// factory nothing but the type name it read back out of JOB_CLASS_NAME.
/// </summary>
public sealed class MigratedSchemaJob : IJob
{
    private static readonly ConcurrentDictionary<TriggerKey, int> fires = new();

    /// <summary>
    /// Clears the record. The fixture is not parallelized internally, so one static tally per process
    /// is enough as long as each run starts from empty.
    /// </summary>
    public static void Reset() => fires.Clear();

    public static bool HasFired(TriggerKey key) => fires.ContainsKey(key);

    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        fires.AddOrUpdate(context.Trigger.Key, 1, static (_, count) => count + 1);
        return default;
    }
}
