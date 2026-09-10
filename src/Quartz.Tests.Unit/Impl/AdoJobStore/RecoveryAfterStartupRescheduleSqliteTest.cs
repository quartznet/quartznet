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

#nullable enable

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// A job that asked to be recovered is recovered after a hard kill even though the process that comes
/// back re-applies its <c>AddJob</c>/<c>AddTrigger</c> registrations before it starts (#3759).
/// </summary>
/// <remarks>
/// <para>
/// The registrations are applied when the scheduler is created, and a trigger the store already holds is
/// applied as a reschedule. A reschedule replaces the trigger, and until #3759 the replacement took the
/// trigger's fired-trigger rows with it — the very row recovery reads once the scheduler starts. Every
/// restart of an application that declares its triggers therefore erased the evidence of the execution
/// the kill had interrupted, and recovery found nothing to do.
/// </para>
/// <para>
/// SQLite refuses to be clustered, so this is the non-clustered recovery sweep; the clustered one reads
/// the same rows after the same reschedule, and is <c>RecoverJobsTest</c> in the integration suite. The
/// kill is emulated through the store rather than a parked thread: the trigger is acquired and fired and
/// its completion never arrives, which leaves exactly the row a dead process leaves.
/// </para>
/// </remarks>
[NonParallelizable]
public sealed class RecoveryAfterStartupRescheduleSqliteTest
{
    private static readonly JobKey jobKey = new("interrupted", "recovery");
    private static readonly TriggerKey triggerKey = new("t-interrupted", "recovery");

    private SqliteTestDatabase database = null!;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        database = new SqliteTestDatabase("recovery-after-reschedule");
        Recordings.Reset();
    }

    [TearDown]
    public void DeleteDatabase()
    {
        database.Dispose();
    }

    [Test]
    public async Task TheStartupRescheduleKeepsTheInterruptedExecutionAndRecoveryRerunsIt()
    {
        await LeaveAnInterruptedExecutionBehind<RecordingJob>();

        await using ServiceProvider restarted = BuildContainer<RecordingJob>();
        IScheduler scheduler = await restarted.GetRequiredService<ISchedulerFactory>().GetScheduler();

        (await FiredTriggerStates()).Should().Equal(["EXECUTING"],
            "creating the scheduler re-applies the declared trigger as a reschedule, and a reschedule "
            + "has to leave the interrupted execution's row where recovery will look for it");

        await scheduler.Start();

        Recording recovered = await Recordings.Recovered.WaitAsync(TimeSpan.FromSeconds(30));
        recovered.JobKey.Should().Be(jobKey);
        recovered.RecoveringTriggerKey.Should().Be(triggerKey,
            "the recovery run names the trigger whose firing it stands in for");

        await scheduler.Shutdown(waitForJobsToComplete: true);
    }

    /// <summary>
    /// The row that is kept is a row the store reads: a replacement trigger of a job that disallows
    /// concurrent execution is stored behind the interrupted execution, as any trigger of the job would
    /// be while it runs, and the recovery sweep is what lets it go.
    /// </summary>
    [Test]
    public async Task TheReplacedTriggerOfASerialJobWaitsBehindTheInterruptedExecutionUntilRecovery()
    {
        await LeaveAnInterruptedExecutionBehind<SerialRecordingJob>();

        await using ServiceProvider restarted = BuildContainer<SerialRecordingJob>();
        IScheduler scheduler = await restarted.GetRequiredService<ISchedulerFactory>().GetScheduler();

        (await ReadColumn("SELECT TRIGGER_STATE FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = 't-interrupted'"))
            .Should().Equal(["BLOCKED"], "the execution the kept row records is, as far as the store can tell, still running the job");
        (await scheduler.GetTriggerState(triggerKey)).Should().Be(TriggerState.Executing,
            "the kept row is the trigger's own execution, and that is what the scheduler reports it as");

        await scheduler.Start();

        Recording recovered = await Recordings.Recovered.WaitAsync(TimeSpan.FromSeconds(30));
        recovered.JobKey.Should().Be(jobKey);

        await scheduler.Shutdown(waitForJobsToComplete: true);

        (await FiredTriggerStates()).Should().BeEmpty("every execution has been accounted for by now");
        (await ReadColumn("SELECT TRIGGER_STATE FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = 't-interrupted'"))
            .Should().Equal(["WAITING"], "the recovery sweep releases what the dead execution was holding");
    }

    /// <summary>
    /// What a process that is killed mid-execution leaves in the database: the declared job and trigger,
    /// and one fired-trigger row saying the trigger is executing on this instance and asks to be recovered.
    /// </summary>
    private async Task LeaveAnInterruptedExecutionBehind<TJob>() where TJob : class, IJob
    {
        await using ServiceProvider killed = BuildContainer<TJob>();

        // Created and never started: creating it stores the declared job and trigger.
        await killed.GetRequiredService<ISchedulerFactory>().GetScheduler();

        IJobStore store = killed.GetRequiredService<IJobStore>();
        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = TimeProvider.System.GetUtcNow().AddMinutes(1),
            MaxCount = 1,
            TimeWindow = TimeSpan.Zero,
        });
        acquired.Should().ContainSingle().Which.Key.Should().Be(triggerKey);

        List<TriggerFiredResult> fired = await store.TriggersFired(acquired);
        fired.Should().ContainSingle();

        // No TriggeredJobComplete: this is where the process dies.
        (await FiredTriggerStates()).Should().Equal(["EXECUTING"], "the premise: the kill interrupted a firing the store knows about");
        (await ReadColumn("SELECT STATE FROM QRTZ_FIRED_TRIGGERS WHERE REQUESTS_RECOVERY = 1")).Should().Equal(["EXECUTING"],
            "the premise: the job asked to be recovered, and the row says so");
    }

    private ServiceProvider BuildContainer<TJob>() where TJob : class, IJob
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = "recovery-after-reschedule";
                options.InstanceId = "one";
            });

            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                store.ProvisionSchema();
            });

            q.AddJob<TJob>(job => job.WithIdentity(jobKey).RequestRecovery());

            // Repeating, so that the trigger row is still there to be read once its firings are done.
            q.AddTrigger(trigger => trigger
                .WithIdentity(triggerKey)
                .ForJob(jobKey)
                .StartNow()
                .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromHours(1)).RepeatForever()));
        });

        return services.BuildServiceProvider();
    }

    private Task<List<string>> FiredTriggerStates()
    {
        return ReadColumn("SELECT STATE FROM QRTZ_FIRED_TRIGGERS");
    }

    private async Task<List<string>> ReadColumn(string sql)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;

        List<string> values = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private sealed record Recording(JobKey JobKey, TriggerKey? RecoveringTriggerKey);

    /// <summary>
    /// The recovery run, once it happens. The replacement trigger fires on its own account as well, and
    /// that run is not the one this test is about.
    /// </summary>
    private static class Recordings
    {
        private static TaskCompletionSource<Recording> recovered = NewSource();

        public static Task<Recording> Recovered => recovered.Task;

        public static void Reset()
        {
            recovered = NewSource();
        }

        public static void Record(IJobExecutionContext context)
        {
            if (context.Recovering)
            {
                recovered.TrySetResult(new Recording(context.JobDetail.Key, context.RecoveringTriggerKey));
            }
        }

        private static TaskCompletionSource<Recording> NewSource()
        {
            return new TaskCompletionSource<Recording>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public sealed class RecordingJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Recordings.Record(context);
            return default;
        }
    }

    [DisallowConcurrentExecution]
    public sealed class SerialRecordingJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Recordings.Record(context);
            return default;
        }
    }
}
