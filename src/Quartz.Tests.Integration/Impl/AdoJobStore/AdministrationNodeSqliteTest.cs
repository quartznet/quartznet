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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Data.Sqlite;

using Quartz.Impl;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The administration node the documentation describes: a process that edits the schedule and does not
/// have the job classes.
/// </summary>
/// <remarks>
/// <para>
/// Two schedulers over one SQLite file, sharing a scheduler name because the rows are keyed by it. The
/// worker resolves the stored class names; the administration node cannot, and it is built twice — once
/// with a type load helper that answers nothing, which is what a web application that does not reference
/// the worker's assembly looks like, and once with the placeholder the 3.x documentation tells such an
/// application to substitute. The placeholder has no attributes, which is the hazard: whether it carried
/// <see cref="DisallowConcurrentExecutionAttribute" /> used to decide how a replacement trigger was stored
/// for a job it was standing in for.
/// </para>
/// <para>
/// #3705: <c>RescheduleJob</c> resolved the class and threw <c>JobPersistenceException : Couldn't
/// replace trigger: Could not load type '...'</c>. Passing <c>false</c> alone would have been worse —
/// the store would have read a non-concurrent job as concurrent and stored the replacement trigger
/// <c>WAITING</c> while the job was executing. Both halves are asserted here, under both loaders.
/// </para>
/// <para>
/// Reading a job row still needs a type on 3.x, because <see cref="JobDetailImpl.JobType" /> cannot be
/// null there; the cases that go through such a read say so, and pin what each loader gets.
/// </para>
/// </remarks>
[NonParallelizable]
[Category("db-sqlite")]
[TestFixture(AdminLoader.Unknown)]
[TestFixture(AdminLoader.Placeholder)]
public sealed class AdministrationNodeSqliteTest
{
    private const string SchedulerName = "administration-node";

    private static readonly JobKey PlainJob = new JobKey("nightly-report", "acme");
    private static readonly JobKey GatedJob = new JobKey("message-cleanup", "acme");
    private static readonly TriggerKey PlainTrigger = new TriggerKey("nightly-report", "acme");
    private static readonly TriggerKey GatedTrigger = new TriggerKey("message-cleanup", "acme");

    private static readonly TimeSpan observationDeadline = TimeSpan.FromSeconds(30);

    private readonly AdminLoader adminLoader;

    private string databaseFile;
    private string connectionString;

    /// <summary>The process that owns the job classes and runs them.</summary>
    private IScheduler worker;

    /// <summary>The process that edits the schedule and cannot load a single job class.</summary>
    private IScheduler admin;

    public AdministrationNodeSqliteTest(AdminLoader adminLoader)
    {
        this.adminLoader = adminLoader;
    }

    [SetUp]
    public async Task TwoProcessesOverOneStore()
    {
        WorkerJobs.Reset();

        databaseFile = Path.Combine(Path.GetTempPath(), $"quartz-admin-node-{Guid.NewGuid():N}.db");
        connectionString = $"Data Source={databaseFile}";

        InstallSchemaFromFreshInstallScript();

        SchedulerBuilder workerConfig = SchedulerBuilder.Create("worker", SchedulerName);
        // The administration node's writes reach this scheduler through the store rather than through
        // its signaler, so the poll interval is what decides how soon it notices them.
        workerConfig.SetProperty(StdSchedulerFactory.PropertySchedulerIdleWaitTime, "1000");
        workerConfig.UseDefaultThreadPool(maxConcurrency: 4);
        workerConfig.UsePersistentStore(store =>
        {
            store.UseMicrosoftSQLite(connectionString);
            store.UseSystemTextJsonSerializer();
        });

        worker = await workerConfig.BuildScheduler();

        await worker.ScheduleJob(
            JobBuilder.Create<WorkerJobs.NightlyReportJob>()
                .WithIdentity(PlainJob)
                .StoreDurably()
                .Build(),
            FarFutureTrigger(PlainTrigger, PlainJob));

        await worker.ScheduleJob(
            JobBuilder.Create<WorkerJobs.MessageCleanupJob>()
                .WithIdentity(GatedJob)
                .StoreDurably()
                .Build(),
            FarFutureTrigger(GatedTrigger, GatedJob));

        // The repository hands out schedulers by name, so the worker steps out of it before the
        // administration node of the same name is built; each keeps running regardless.
        SchedulerRepository.Instance.Remove(SchedulerName, "worker");

        SchedulerBuilder adminConfig = SchedulerBuilder.Create("admin", SchedulerName);
        // Never runs a job, and is never started.
        adminConfig.UseZeroSizeThreadPool();
        if (adminLoader == AdminLoader.Unknown)
        {
            adminConfig.UseTypeLoader<UnknownJobTypeLoader>();
        }
        else
        {
            adminConfig.UseTypeLoader<PlaceholderJobTypeLoader>();
        }

        adminConfig.UsePersistentStore(store =>
        {
            store.UseMicrosoftSQLite(connectionString);
            store.UseSystemTextJsonSerializer();
        });

        admin = await adminConfig.BuildScheduler();
    }

    [TearDown]
    public async Task ShutDownBoth()
    {
        // Whatever is held at the gate finishes, so the worker's shutdown is not the thing that waits.
        WorkerJobs.Release();

        if (admin != null)
        {
            await admin.Shutdown(waitForJobsToComplete: false);
        }

        if (worker != null)
        {
            await worker.Shutdown(waitForJobsToComplete: true);
        }

        SqliteConnection.ClearAllPools();

        DeleteDatabaseFile();
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
        StoredTriggerState(GatedTrigger).Should().Be("WAITING");

        StoredJobFlags(GatedJob).Should().Be((true, true), "rescheduling did not rewrite the job row");
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

        ITrigger updated = await admin.GetTrigger(PlainTrigger);
        updated.Description.Should().Be("edited from the administration node");
        updated.Priority.Should().Be(9);
    }

    /// <summary>
    /// Pausing and resuming, by trigger and by job.
    /// </summary>
    [Test]
    public async Task PausingAndResumingNeedNoJobClass()
    {
        await admin.PauseTrigger(PlainTrigger);
        (await admin.GetTriggerState(PlainTrigger)).Should().Be(TriggerState.Paused);

        await admin.ResumeTrigger(PlainTrigger);
        (await admin.GetTriggerState(PlainTrigger)).Should().Be(TriggerState.Normal);

        await admin.PauseJob(GatedJob);
        (await admin.GetTriggerState(GatedTrigger)).Should().Be(TriggerState.Paused);

        await admin.ResumeJob(GatedJob);
        (await admin.GetTriggerState(GatedTrigger)).Should().Be(TriggerState.Normal);
    }

    /// <summary>
    /// Unscheduling and deleting, which never needed the class and still do not.
    /// </summary>
    [Test]
    public async Task UnschedulingAndDeletingNeedNoJobClass()
    {
        (await admin.UnscheduleJob(PlainTrigger)).Should().BeTrue();
        (await admin.CheckExists(PlainTrigger)).Should().BeFalse();

        (await admin.DeleteJob(GatedJob)).Should().BeTrue();
        (await admin.CheckExists(GatedJob)).Should().BeFalse();
        (await admin.CheckExists(GatedTrigger)).Should().BeFalse("a job takes its triggers with it");
    }

    /// <summary>
    /// Adding a second trigger to a job that already exists, which reads the job row — and on 3.x a job
    /// row cannot be read without a type, so this is where the two loaders part company.
    /// </summary>
    [Test]
    public async Task AddingATriggerToAnExistingJobReadsTheJobRow()
    {
        TriggerKey extra = new TriggerKey("message-cleanup-evening", "acme");

        Func<Task> act = async () => await admin.ScheduleJob(FarFutureTrigger(extra, GatedJob));

        if (adminLoader == AdminLoader.Unknown)
        {
            await act.Should().ThrowAsync<JobPersistenceException>(
                "storing a trigger for an existing job reads the job's row, and on 3.x a job detail cannot "
                + "exist without a type - which is why the documentation tells an administration node to "
                + "substitute a placeholder");
            return;
        }

        await act.Should().NotThrowAsync("the placeholder gives the row a type to be read into");

        (await admin.CheckExists(extra)).Should().BeTrue();
        (await admin.GetTriggerState(extra)).Should().Be(TriggerState.Normal);
        StoredTriggerState(extra).Should().Be("WAITING", "nothing is executing");

        (await admin.GetTriggersOfJob(GatedJob)).Select(trigger => trigger.Key)
            .Should().BeEquivalentTo(new[] { GatedTrigger, extra });
    }

    /// <summary>
    /// The flags a placeholder reads are the row's, not the placeholder's.
    /// </summary>
    [Test]
    public async Task ReadingAJobThroughAPlaceholderTellsTheTruthAboutItsFlags()
    {
        Func<Task<IJobDetail>> act = async () => await admin.GetJobDetail(GatedJob);

        if (adminLoader == AdminLoader.Unknown)
        {
            await act.Should().ThrowAsync<JobPersistenceException>(
                "on 3.x a job detail cannot exist without a type, so a loader that answers nothing cannot read one");
            return;
        }

        IJobDetail detail = (await act.Should().NotThrowAsync()).Which;

        detail.JobType.Should().Be<PlaceholderJobTypeLoader.PlaceholderJob>("the loader substituted it");
        detail.ConcurrentExecutionDisallowed.Should().BeTrue(
            "IS_NONCONCURRENT is the record of the attribute, and the placeholder - which carries no "
            + "attribute at all - does not get a say");
        detail.PersistJobDataAfterExecution.Should().BeTrue("IS_UPDATE_DATA says the same of the other attribute");
    }

    /// <summary>
    /// The state that tells the fix apart from simply not resolving the class.
    /// </summary>
    /// <remarks>
    /// <para>
    /// With the non-concurrent job executing on the worker, everything the administration node stores for
    /// that job has to be <c>BLOCKED</c> rather than <c>WAITING</c>, and that decision is made from
    /// <see cref="IJobDetail.ConcurrentExecutionDisallowed" />. A detail whose flags were deduced from a
    /// class this process cannot load would answer <see langword="false" /> — and so would one deduced
    /// from the placeholder — and store the replacement ready to run: a concurrent execution of a job
    /// that forbids it.
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
        await worker.TriggerJob(GatedJob);

        await ShouldObserve(WorkerJobs.MessageCleanupStarted.Task, "the worker runs the job it was told to");

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
        StoredTriggerState(GatedTrigger).Should().Be("BLOCKED");

        if (adminLoader == AdminLoader.Placeholder)
        {
            TriggerKey extra = new TriggerKey("message-cleanup-evening", "acme");
            await admin.ScheduleJob(FarFutureTrigger(extra, GatedJob));

            (await admin.GetTriggerState(extra)).Should().Be(
                TriggerState.Blocked,
                "a trigger added while the job runs is blocked for the same reason, and the placeholder's "
                + "missing attribute does not decide otherwise");
        }

        WorkerJobs.Release();

        // The completion of the run that blocked them is what lets them go, so the replacement fires
        // rather than being parked in BLOCKED for ever.
        await ShouldObserve(WorkerJobs.MessageCleanupFiredAgain.Task,
            "the replacement trigger was let go when the run that blocked it completed");
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

    private static async Task ShouldObserve(Task observation, string because)
    {
        Func<Task> act = () => observation;
        await act.Should().CompleteWithinAsync(observationDeadline, because);
    }

    /// <summary>
    /// <c>TRIGGER_STATE</c> as the table holds it, read without going through the store that wrote it.
    /// </summary>
    private string StoredTriggerState(TriggerKey key)
    {
        using (SqliteConnection connection = new SqliteConnection(connectionString))
        {
            connection.Open();

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT TRIGGER_STATE FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = @name AND TRIGGER_GROUP = @group";
                command.Parameters.AddWithValue("@name", key.Name);
                command.Parameters.AddWithValue("@group", key.Group);

                return (string) command.ExecuteScalar();
            }
        }
    }

    /// <summary>
    /// The two attribute columns as the job row holds them.
    /// </summary>
    private (bool NonConcurrent, bool UpdateData) StoredJobFlags(JobKey key)
    {
        using (SqliteConnection connection = new SqliteConnection(connectionString))
        {
            connection.Open();

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT IS_NONCONCURRENT, IS_UPDATE_DATA FROM QRTZ_JOB_DETAILS WHERE JOB_NAME = @name AND JOB_GROUP = @group";
                command.Parameters.AddWithValue("@name", key.Name);
                command.Parameters.AddWithValue("@group", key.Group);

                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    reader.Read().Should().BeTrue("the job this fixture stored is still there");
                    return (reader.GetBoolean(0), reader.GetBoolean(1));
                }
            }
        }
    }

    /// <summary>
    /// Runs <c>database/tables/tables_sqlite.sql</c>, the script the documentation tells a reader to
    /// run, against the empty file. The whole text goes to one command: SQLite's own parser splits it.
    /// </summary>
    private void InstallSchemaFromFreshInstallScript()
    {
        using (SqliteConnection connection = new SqliteConnection(connectionString))
        {
            connection.Open();

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = File.ReadAllText(ResolveRepositoryFile("database", "tables", "tables_sqlite.sql"));
                command.ExecuteNonQuery();
            }
        }
    }

    /// <summary>
    /// A file in the working tree the test assembly was built in, found by walking up from the output
    /// directory rather than counting directories.
    /// </summary>
    private static string ResolveRepositoryFile(params string[] pathSegments)
    {
        string relativePath = Path.Combine(pathSegments);
        DirectoryInfo current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current != null)
        {
            string candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not find '{relativePath}' above '{AppContext.BaseDirectory}'.");
    }

    /// <summary>
    /// Deletes the file, giving the last connection a moment to let go of it.
    /// </summary>
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
    /// A type load helper for a process that does not have the worker's assembly. The worker's jobs
    /// are compiled into this very assembly, so the helper resolves everything the way
    /// <see cref="SimpleTypeLoadHelper" /> does — the scheduler factory loads its own parts through
    /// it — and treats the worker's job classes alone as names it cannot answer.
    /// </summary>
    public abstract class AdministrationNodeTypeLoader : ITypeLoadHelper
    {
        public void Initialize()
        {
        }

        public Type LoadType(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            Type type = Type.GetType(name, throwOnError: false);
            return type != null && type.DeclaringType == typeof(WorkerJobs)
                ? WithoutTheWorkerAssembly(name)
                : type;
        }

        protected abstract Type WithoutTheWorkerAssembly(string name);
    }

    /// <summary>
    /// The administration node's type loader when it has no assembly to answer from: it knows nothing
    /// about the worker's jobs, and says so rather than substituting a placeholder.
    /// </summary>
    public sealed class UnknownJobTypeLoader : AdministrationNodeTypeLoader
    {
        protected override Type WithoutTheWorkerAssembly(string name) => null;
    }

    /// <summary>
    /// The type loader the 3.x documentation tells an administration node to substitute: every stored
    /// job name it cannot load resolves to one placeholder job, which carries no attribute of any kind.
    /// </summary>
    public sealed class PlaceholderJobTypeLoader : AdministrationNodeTypeLoader
    {
        protected override Type WithoutTheWorkerAssembly(string name) => typeof(PlaceholderJob);

        public sealed class PlaceholderJob : IJob
        {
            public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
        }
    }

    /// <summary>
    /// The jobs as the worker compiles them, and the gates a test drives them with.
    /// </summary>
    public static class WorkerJobs
    {
        public static TaskCompletionSource<bool> MessageCleanupStarted { get; private set; } = NewSource();

        public static TaskCompletionSource<bool> MessageCleanupFiredAgain { get; private set; } = NewSource();

        private static TaskCompletionSource<bool> gate = NewSource();

        public static void Reset()
        {
            MessageCleanupStarted = NewSource();
            MessageCleanupFiredAgain = NewSource();
            gate = NewSource();
        }

        public static void Release() => gate.TrySetResult(true);

        private static TaskCompletionSource<bool> NewSource() => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public sealed class NightlyReportJob : IJob
        {
            public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
        }

        [DisallowConcurrentExecution]
        [PersistJobDataAfterExecution]
        public sealed class MessageCleanupJob : IJob
        {
            public async Task Execute(IJobExecutionContext context)
            {
                if (!MessageCleanupStarted.TrySetResult(true))
                {
                    // A second firing: the trigger the administration node stored while the first one
                    // was blocked has been let go and run.
                    MessageCleanupFiredAgain.TrySetResult(true);
                    return;
                }

                await gate.Task.ConfigureAwait(false);
            }
        }
    }
}

/// <summary>
/// What the administration node's type load helper answers for a stored class name it cannot load.
/// </summary>
public enum AdminLoader
{
    /// <summary>Nothing: the loader returns <see langword="null" />.</summary>
    Unknown,

    /// <summary>A placeholder job type with no attributes, as the 3.x documentation suggests.</summary>
    Placeholder,
}
