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

using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// The administration node the documentation describes: a process that edits the schedule and does not
/// have the job classes.
/// </summary>
/// <remarks>
/// <para>
/// Two schedulers over one SQLite file, built from two containers so each has its own repository and its
/// own <see cref="ITypeLoader" />, sharing a scheduler name because the rows are keyed by it. The worker
/// resolves the stored class names; the administration node resolves nothing, which is what a web
/// application that does not reference the worker's assembly looks like. The stored
/// <c>JOB_CLASS_NAME</c> is therefore a name no assembly in this process carries, so
/// <see cref="JobType.TryResolve" /> is genuinely false on the administration side.
/// </para>
/// <para>
/// #3705: <c>RescheduleJob</c> resolved the class and threw <c>JobPersistenceException : Couldn't
/// replace trigger: Could not load type '...'</c>. Passing <c>false</c> alone would have been worse —
/// the store would have read a non-concurrent job as concurrent and stored the replacement trigger
/// <c>WAITING</c> while the job was executing. Both halves are asserted here.
/// </para>
/// </remarks>
[NonParallelizable]
public sealed class AdministrationNodeSqliteTest
{
    private const string SchedulerName = "administration-node";

    /// <summary>The stored class name of the plain job, which resolves only through the worker's loader.</summary>
    private const string PlainJobClassName = "Acme.Jobs.NightlyReportJob, Acme.Jobs";

    /// <summary>The stored class name of the job that forbids concurrent execution.</summary>
    private const string GatedJobClassName = "Acme.Jobs.MessageCleanupJob, Acme.Jobs";

    private static readonly JobKey PlainJob = new("nightly-report", "acme");
    private static readonly JobKey GatedJob = new("message-cleanup", "acme");
    private static readonly TriggerKey PlainTrigger = new("nightly-report", "acme");
    private static readonly TriggerKey GatedTrigger = new("message-cleanup", "acme");

    private string databaseFile = null!;
    private string connectionString = null!;

    private StandaloneSchedulerFactory workerFactory = null!;
    private StandaloneSchedulerFactory adminFactory = null!;

    /// <summary>The process that owns the job classes and runs them.</summary>
    private IScheduler worker = null!;

    /// <summary>The process that edits the schedule and cannot load a single job class.</summary>
    private IScheduler admin = null!;

    [SetUp]
    public async Task TwoProcessesOverOneStore()
    {
        WorkerJobs.Reset();

        databaseFile = Path.Combine(Path.GetTempPath(), $"quartz-admin-node-{Guid.NewGuid():N}.db");
        connectionString = $"Data Source={databaseFile}";

        workerFactory = QuartzSchedulerBuilder.Create(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = SchedulerName;
                options.InstanceId = "worker";

                // The administration node's writes reach this scheduler through the store rather than
                // through its signaler, so the poll interval is what decides how soon it notices them.
                options.IdleWaitTime = TimeSpan.FromSeconds(1);
            });

            q.UseDefaultThreadPool(maxConcurrency: 4);

            // The worker has the assembly; here that is an alias rather than a reference, so that the
            // stored names are ones the administration node genuinely cannot resolve.
            q.UseTypeLoader(options => options
                .Map(PlainJobClassName, typeof(WorkerJobs.NightlyReportJob))
                .Map(GatedJobClassName, typeof(WorkerJobs.MessageCleanupJob)));

            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, connectionString);
                store.ProvisionSchema();
            });
        }).Build();

        worker = await workerFactory.GetScheduler();

        await worker.ScheduleJob(
            JobBuilder.Create()
                .WithIdentity(PlainJob)
                .OfType(new Quartz.JobType(PlainJobClassName, _ => typeof(WorkerJobs.NightlyReportJob)))
                .StoreDurably()
                .Build(),
            FarFutureTrigger(PlainTrigger, PlainJob));

        await worker.ScheduleJob(
            JobBuilder.Create()
                .WithIdentity(GatedJob)
                .OfType(new Quartz.JobType(GatedJobClassName, _ => typeof(WorkerJobs.MessageCleanupJob)))
                .StoreDurably()
                .Build(),
            FarFutureTrigger(GatedTrigger, GatedJob));

        adminFactory = QuartzSchedulerBuilder.Create(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = SchedulerName;
                options.InstanceId = "admin";
            });

            // Never runs a job, and is never started.
            q.UseThreadPool<ZeroSizeThreadPool>();
            q.UseTypeLoader<UnknownJobTypeLoader>();

            q.UsePersistentStore(store => store.UseSqlite(SqliteFactory.Instance, connectionString));
        }).Build();

        admin = await adminFactory.GetScheduler();
    }

    [TearDown]
    public async Task ShutDownBoth()
    {
        // Whatever is held at the gate finishes, so the worker's shutdown is not the thing that waits.
        WorkerJobs.Release();

        await admin.Shutdown(waitForJobsToComplete: false);
        await worker.Shutdown(waitForJobsToComplete: true);

        adminFactory.Dispose();
        workerFactory.Dispose();

        SqliteConnection.ClearAllPools();

        DeleteDatabaseFile();
    }

    /// <summary>
    /// Deletes the file, giving the last connection a moment to let go of it.
    /// </summary>
    /// <remarks>
    /// The two schedulers have been shut down and the pools cleared by the time this runs, but on
    /// Windows a handle that was closed a microsecond ago can still fail the delete, and a fixture that
    /// leaves a stray temporary file behind is worse than one that waits.
    /// </remarks>
    private void DeleteDatabaseFile()
    {
        for (int attempt = 0; attempt < 20 && File.Exists(databaseFile); attempt++)
        {
            try
            {
                File.Delete(databaseFile);
            }
            catch (IOException)
            {
                Thread.Sleep(50);
            }
        }
    }

    /// <summary>
    /// The reads, all of which answer on the stored name alone.
    /// </summary>
    [Test]
    public async Task ReadingTheScheduleNeedsNoJobClass()
    {
        IJobDetail? detail = await admin.GetJobDetail(GatedJob);

        detail.Should().NotBeNull();
        detail!.JobType.FullName.Should().Be(GatedJobClassName, "reading a job never rewrites the stored name");
        detail.JobType.TryResolve(out _).Should().BeFalse(
            "this process does not have the assembly, which is the arrangement the test is about");

        detail.ConcurrentExecutionDisallowed.Should().BeTrue(
            "IS_NONCONCURRENT is the record of the attribute, and it is readable without the assembly");
        detail.PersistJobDataAfterExecution.Should().BeTrue("IS_UPDATE_DATA says the same of the other attribute");

        (await admin.Exists(GatedJob)).Should().BeTrue();
        (await admin.Exists(GatedTrigger)).Should().BeTrue();
        (await admin.Exists(new JobKey("no-such-job", "acme"))).Should().BeFalse();

        List<ITrigger> triggers = await admin.GetTriggersOfJob(GatedJob);
        triggers.Should().ContainSingle().Which.Key.Should().Be(GatedTrigger);

        (await admin.GetTriggerState(GatedTrigger)).Should().Be(TriggerState.Normal);

        PagedResult<JobHeader> jobs = await admin.QueryJobs(new JobQuery { Take = PagedQuery.All });
        jobs.Items.Select(job => job.Key).Should().BeEquivalentTo([PlainJob, GatedJob]);
        jobs.Items.Single(job => job.Key.Equals(GatedJob)).ConcurrentExecutionDisallowed.Should().BeTrue(
            "a listing reads the column too, so the dashboard is right about a job it cannot load");

        PagedResult<TriggerHeader> storedTriggers = await admin.QueryTriggers(new TriggerQuery { Take = PagedQuery.All });
        storedTriggers.Items.Select(trigger => trigger.Key).Should().BeEquivalentTo([PlainTrigger, GatedTrigger]);
    }

    /// <summary>
    /// Pausing and resuming, by trigger and by job.
    /// </summary>
    [Test]
    public async Task PausingAndResumingNeedNoJobClass()
    {
        (await admin.PauseTrigger(PlainTrigger)).Should().BeTrue();
        (await admin.GetTriggerState(PlainTrigger)).Should().Be(TriggerState.Paused);

        (await admin.ResumeTrigger(PlainTrigger)).Should().BeTrue();
        (await admin.GetTriggerState(PlainTrigger)).Should().Be(TriggerState.Normal);

        (await admin.PauseJob(GatedJob)).Should().BeTrue();
        (await admin.GetTriggerState(GatedTrigger)).Should().Be(TriggerState.Paused);

        (await admin.ResumeJob(GatedJob)).Should().BeTrue();
        (await admin.GetTriggerState(GatedTrigger)).Should().Be(TriggerState.Normal);
    }

    /// <summary>
    /// The call #3705 reported: rescheduling from a process without the job's assembly.
    /// </summary>
    [Test]
    public async Task ReschedulingNeedsNoJobClass()
    {
        ITrigger replacement = TriggerBuilder.Create()
            .WithIdentity(GatedTrigger)
            .ForJob(GatedJob)
            .StartAt(DateTimeOffset.UtcNow.AddYears(2))
            .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromHours(2)).RepeatForever())
            .Build();

        Func<Task> act = async () => await admin.RescheduleJob(GatedTrigger, replacement);

        await act.Should().NotThrowAsync(
            "the job's key, durability and attribute flags all come from the row, so nothing on this path "
            + "needs the class - which is what threw JobPersistenceException: Couldn't replace trigger");

        (await admin.GetTriggerState(GatedTrigger)).Should().Be(
            TriggerState.Normal,
            "nothing is executing, so the replacement is ready to fire");
        (await StoredTriggerState(GatedTrigger)).Should().Be("WAITING");

        IJobDetail? detail = await admin.GetJobDetail(GatedJob);
        detail!.ConcurrentExecutionDisallowed.Should().BeTrue("rescheduling did not rewrite the job row");
    }

    /// <summary>
    /// Editing a trigger's metadata, which is the other caller that used to resolve the class.
    /// </summary>
    [Test]
    public async Task UpdatingATriggerNeedsNoJobClass()
    {
        Func<Task<bool>> act = async () => await admin.UpdateTriggerDetails(
            PlainTrigger,
            new TriggerDetailsUpdate().WithDescription("edited from the administration node").WithPriority(9));

        (await act.Should().NotThrowAsync()).Which.Should().BeTrue();

        ITrigger? updated = await admin.GetTrigger(PlainTrigger);
        updated!.Description.Should().Be("edited from the administration node");
        updated.Priority.Should().Be(9);
    }

    /// <summary>
    /// Editing a job's data map: read the detail, change a value, store it back over the old one.
    /// </summary>
    /// <remarks>
    /// The copy is built from the detail the store handed over, so the two attribute flags travel with
    /// it as stated values. Were they deduced instead, this write would clear <c>IS_NONCONCURRENT</c> on
    /// a job whose class this process cannot see — the same silent damage the trigger paths avoid.
    /// </remarks>
    [Test]
    public async Task EditingJobDataNeedsNoJobClassAndKeepsTheFlags()
    {
        IJobDetail original = (await admin.GetJobDetail(GatedJob))!;

        IJobDetail edited = original.GetJobBuilder()
            .UsingJobData("mailbox", "archive")
            .Build();

        await admin.AddJob(edited, AddJobOptions.Replacing);

        IJobDetail stored = (await admin.GetJobDetail(GatedJob))!;

        stored.JobDataMap.GetString("mailbox").Should().Be("archive", "the edit is what the write was for");
        stored.JobType.FullName.Should().Be(GatedJobClassName, "and the stored class name is untouched");
        stored.ConcurrentExecutionDisallowed.Should().BeTrue(
            "the copy carried the stated flag, so a write from a process without the class cannot turn a "
            + "non-concurrent job into a concurrent one");
        stored.PersistJobDataAfterExecution.Should().BeTrue();

        (await StoredJobFlags(GatedJob)).Should().Be((true, true), "which is what the row says as well");

        // And the worker still runs it, reading the data the administration node wrote.
        await worker.Start();
        await admin.TriggerJob(GatedJob);

        await WorkerJobs.MessageCleanupStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));
        WorkerJobs.MessageCleanupMailbox.Should().Be("archive");
    }

    /// <summary>
    /// Adding a second trigger to a job that already exists, which is the other half of schedule editing.
    /// </summary>
    [Test]
    public async Task AddingATriggerToAnExistingJobNeedsNoJobClass()
    {
        TriggerKey extra = new("message-cleanup-evening", "acme");

        Func<Task> act = async () => await admin.ScheduleJob(FarFutureTrigger(extra, GatedJob));

        await act.Should().NotThrowAsync(
            "the job the trigger names is read from the row, class and all, so a schedule-editing process "
            + "can add to a job it cannot load");

        (await admin.Exists(extra)).Should().BeTrue();
        (await admin.GetTriggerState(extra)).Should().Be(TriggerState.Normal);
        (await StoredTriggerState(extra)).Should().Be("WAITING", "nothing is executing");

        (await admin.GetTriggersOfJob(GatedJob)).Select(trigger => trigger.Key)
            .Should().BeEquivalentTo([GatedTrigger, extra]);
    }

    /// <summary>
    /// The state that tells the fix apart from simply not resolving the class.
    /// </summary>
    /// <remarks>
    /// <para>
    /// With the non-concurrent job executing on the worker, everything the administration node stores for
    /// that job has to be <c>BLOCKED</c> rather than <c>WAITING</c>, and that decision is made from
    /// <see cref="IJobDetail.ConcurrentExecutionDisallowed" />. A detail whose flags were deduced from a
    /// class this process cannot load would answer <see langword="false" /> and store both triggers ready
    /// to run — a concurrent execution of a job that forbids it.
    /// </para>
    /// <para>
    /// <c>QRTZ_TRIGGERS</c> has no non-concurrent column of its own; <c>TRIGGER_STATE</c> is where the
    /// decision lands, so that is what is read from the table.
    /// </para>
    /// </remarks>
    [Test]
    public async Task WhatTheAdminStoresWhileTheJobRunsIsBlocked()
    {
        await worker.Start();
        await admin.TriggerJob(GatedJob);

        await WorkerJobs.MessageCleanupStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));

        ITrigger replacement = TriggerBuilder.Create()
            .WithIdentity(GatedTrigger)
            .ForJob(GatedJob)
            .StartAt(DateTimeOffset.UtcNow.AddSeconds(1))
            .Build();

        await admin.RescheduleJob(GatedTrigger, replacement);

        (await admin.GetTriggerState(GatedTrigger)).Should().Be(
            TriggerState.Blocked,
            "the job forbids concurrent execution and is executing, so its replacement trigger waits for "
            + "the run in progress rather than joining it");
        (await StoredTriggerState(GatedTrigger)).Should().Be("BLOCKED");

        TriggerKey extra = new("message-cleanup-evening", "acme");
        await admin.ScheduleJob(FarFutureTrigger(extra, GatedJob));

        (await admin.GetTriggerState(extra)).Should().Be(
            TriggerState.Blocked,
            "a trigger added while the job runs is blocked for the same reason");

        WorkerJobs.Release();

        await WorkerJobs.MessageCleanupFiredAgain.Task.WaitAsync(TimeSpan.FromSeconds(30));

        // The completion of the run that blocked them is what lets them go, so the replacement fires
        // rather than being parked in BLOCKED for ever.
    }

    /// <summary>
    /// Triggering a job now, from a process that could not construct it.
    /// </summary>
    [Test]
    public async Task TriggeringAJobNeedsNoJobClassOnTheNodeThatAsks()
    {
        await worker.Start();

        Func<Task> act = async () => await admin.TriggerJob(PlainJob);

        await act.Should().NotThrowAsync();

        await WorkerJobs.NightlyReportStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Unscheduling and deleting, which never needed the class and still do not.
    /// </summary>
    [Test]
    public async Task UnschedulingAndDeletingNeedNoJobClass()
    {
        (await admin.UnscheduleJob(PlainTrigger)).Should().BeTrue();
        (await admin.Exists(PlainTrigger)).Should().BeFalse();

        (await admin.DeleteJob(GatedJob)).Should().BeTrue();
        (await admin.Exists(GatedJob)).Should().BeFalse();
        (await admin.Exists(GatedTrigger)).Should().BeFalse("a job takes its triggers with it");
    }

    /// <summary>
    /// Interruption is answered without the class, and answers <see langword="false" />.
    /// </summary>
    /// <remarks>
    /// <see cref="IScheduler.Interrupt(JobKey, CancellationToken)" /> is documented as not cluster aware:
    /// it cancels the tokens of the executions running in <em>this</em> scheduler, and an administration
    /// node runs none. So it is safe to call and cannot reach the worker — a limitation of the operation
    /// rather than of the missing assembly, and the one entry in this matrix that a schedule-editing
    /// process cannot use to act on another node.
    /// </remarks>
    [Test]
    public async Task InterruptingIsAnsweredWithoutTheJobClassAndReachesNoOtherNode()
    {
        await worker.Start();
        await admin.TriggerJob(GatedJob);

        await WorkerJobs.MessageCleanupStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));

        (await admin.Interrupt(GatedJob)).Should().BeFalse(
            "the execution is on the worker, and Interrupt only reaches this scheduler's own firings");
    }

    private static ITrigger FarFutureTrigger(TriggerKey key, JobKey job)
    {
        return TriggerBuilder.Create()
            .WithIdentity(key)
            .ForJob(job)
            // Far enough out that nothing fires while a test drives the store by hand.
            .StartAt(DateTimeOffset.UtcNow.AddYears(1))
            .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build();
    }

    /// <summary>
    /// <c>TRIGGER_STATE</c> as the table holds it, read without going through the store that wrote it.
    /// </summary>
    private async Task<string> StoredTriggerState(TriggerKey key)
    {
        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT TRIGGER_STATE FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = @name AND TRIGGER_GROUP = @group";
        command.Parameters.AddWithValue("@name", key.Name);
        command.Parameters.AddWithValue("@group", key.Group);

        return (string) (await command.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// The two attribute columns as the job row holds them.
    /// </summary>
    private async Task<(bool NonConcurrent, bool UpdateData)> StoredJobFlags(JobKey key)
    {
        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT IS_NONCONCURRENT, IS_UPDATE_DATA FROM QRTZ_JOB_DETAILS WHERE JOB_NAME = @name AND JOB_GROUP = @group";
        command.Parameters.AddWithValue("@name", key.Name);
        command.Parameters.AddWithValue("@group", key.Group);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();

        (await reader.ReadAsync()).Should().BeTrue("the job this fixture stored is still there");

        return (reader.GetBoolean(0), reader.GetBoolean(1));
    }

    /// <summary>
    /// The administration node's type loader: it knows nothing about the worker's jobs, and says so
    /// rather than substituting a placeholder.
    /// </summary>
    public sealed class UnknownJobTypeLoader : ITypeLoader
    {
        public Type? LoadType(string name) => Type.GetType(name, throwOnError: false);
    }

    /// <summary>
    /// The jobs as the worker compiles them, and the gates a test drives them with.
    /// </summary>
    public static class WorkerJobs
    {
        public static TaskCompletionSource NightlyReportStarted { get; private set; } = NewSource();

        public static TaskCompletionSource MessageCleanupStarted { get; private set; } = NewSource();

        public static TaskCompletionSource MessageCleanupFiredAgain { get; private set; } = NewSource();

        public static string? MessageCleanupMailbox { get; private set; }

        private static TaskCompletionSource gate = NewSource();

        public static void Reset()
        {
            NightlyReportStarted = NewSource();
            MessageCleanupStarted = NewSource();
            MessageCleanupFiredAgain = NewSource();
            MessageCleanupMailbox = null;
            gate = NewSource();
        }

        public static void Release() => gate.TrySetResult();

        internal static async ValueTask EnterMessageCleanup(IJobExecutionContext context, CancellationToken cancellationToken)
        {
            MessageCleanupMailbox = context.MergedJobDataMap.GetString("mailbox");

            if (!MessageCleanupStarted.TrySetResult())
            {
                // A second firing: the trigger the administration node stored while the first one was
                // blocked has been let go and run.
                MessageCleanupFiredAgain.TrySetResult();
                return;
            }

            await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        private static TaskCompletionSource NewSource() => new(TaskCreationOptions.RunContinuationsAsynchronously);

        public sealed class NightlyReportJob : IJob
        {
            public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
            {
                NightlyReportStarted.TrySetResult();
                return default;
            }
        }

        [DisallowConcurrentExecution]
        [PersistJobDataAfterExecution]
        public sealed class MessageCleanupJob : IJob
        {
            public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
            {
                return EnterMessageCleanup(context, cancellationToken);
            }
        }
    }
}
