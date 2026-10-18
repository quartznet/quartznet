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
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Data.Sqlite;

using Quartz.Simpl;
using Quartz.Xml;

namespace Quartz.Tests.Integration.Xml;

/// <summary>
/// A declared trigger the store already holds is re-applied, as <c>AddTrigger</c> registrations are when
/// the scheduler is created, and it keeps the time it last fired (#1834, #4122).
/// </summary>
/// <remarks>
/// <see cref="DeclaringProcessor" /> declares its job and trigger the way the container's processor does,
/// so what runs is the processor's own re-application rather than a copy of it.
/// </remarks>
[NonParallelizable]
public sealed class TriggerReappliedAtStartupTest
{
    private static readonly JobKey jobKey = new JobKey("declared", "restart");
    private static readonly TriggerKey triggerKey = new TriggerKey("t-declared", "restart");
    private static readonly TimeSpan firingTimeout = TimeSpan.FromSeconds(30);

    private string databaseFile;

    [SetUp]
    public void ResetRecordings()
    {
        RecordingJob.Reset();
    }

    [TearDown]
    public void DeleteDatabase()
    {
        if (databaseFile is null)
        {
            return;
        }

        SqliteConnection.ClearAllPools();
        if (File.Exists(databaseFile))
        {
            File.Delete(databaseFile);
        }

        databaseFile = null;
    }

    [Test]
    public async Task InMemoryStoreKeepsThePreviousFireTimeOfAReappliedTrigger()
    {
        IScheduler scheduler = await SchedulerBuilder.Create("one", "reapplied-in-memory").UseInMemoryStore().BuildScheduler();
        try
        {
            await new DeclaringProcessor(startNow: true).ScheduleJobs(scheduler);
            DateTimeOffset previous = await FireOnceThenStandBy(scheduler);

            await new DeclaringProcessor(startNow: true).ScheduleJobs(scheduler);

            (await scheduler.GetTrigger(triggerKey)).GetPreviousFireTimeUtc().Should().Be(previous,
                "re-applying the same declared trigger must not forget that it has fired");

            await scheduler.Start();
            (await RecordingJob.NextFiring()).Should().Be(previous,
                "context.PreviousFireTimeUtc on the first firing after the re-application is the firing before it");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }
    }

    [Test]
    [Category("db-sqlite")]
    public async Task SqliteStoreKeepsThePreviousFireTimeOfARegisteredTriggerAcrossARestart()
    {
        databaseFile = Path.Combine(Path.GetTempPath(), $"quartz-reapplied-{Guid.NewGuid():N}.db");
        string connectionString = $"Data Source={databaseFile}";
        InstallSchema(connectionString);

        DateTimeOffset previous;
        IScheduler first = await BuildSqliteScheduler(connectionString);
        try
        {
            await new DeclaringProcessor(startNow: true).ScheduleJobs(first);
            previous = await FireOnceThenStandBy(first);
        }
        finally
        {
            await first.Shutdown(waitForJobsToComplete: true);
        }

        IScheduler restarted = await BuildSqliteScheduler(connectionString);
        try
        {
            await new DeclaringProcessor(startNow: true).ScheduleJobs(restarted);

            (await restarted.GetTrigger(triggerKey)).GetPreviousFireTimeUtc().Should().Be(previous,
                "a start re-applies the declared trigger, and re-applying the same trigger must not forget that it has fired");

            await restarted.Start();
            (await RecordingJob.NextFiring()).Should().Be(previous,
                "context.PreviousFireTimeUtc on the first firing after a restart is the firing before the restart (#1834)");
        }
        finally
        {
            await restarted.Shutdown(waitForJobsToComplete: true);
        }
    }

    /// <summary>
    /// Carrying the previous fire time over must not move the next one: a simple trigger whose start is
    /// behind it is still due from the re-application, as it was before its previous fire time was kept.
    /// </summary>
    [Test]
    public async Task CarryingThePreviousFireTimeLeavesTheNextFireTimeOfAPastStartWhereItWas()
    {
        IScheduler scheduler = await SchedulerBuilder.Create("one", "reapplied-past-start").UseInMemoryStore().BuildScheduler();
        try
        {
            await new DeclaringProcessor(startNow: false).ScheduleJobs(scheduler);
            DateTimeOffset previous = await FireOnceThenStandBy(scheduler);

            DateTimeOffset reappliedAt = DateTimeOffset.UtcNow;
            await new DeclaringProcessor(startNow: false).ScheduleJobs(scheduler);

            ITrigger stored = await scheduler.GetTrigger(triggerKey);
            stored.GetPreviousFireTimeUtc().Should().Be(previous);
            stored.GetNextFireTimeUtc().Should().BeOnOrAfter(reappliedAt,
                "a re-applied simple trigger starting a day ago is due from the re-application, not a day in the past");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }
    }

    /// <summary>
    /// What a start does is not what an application's own <c>RescheduleJob</c> does: a trigger handed to
    /// that is a new trigger, and it has never fired.
    /// </summary>
    [Test]
    public async Task RescheduleJobByTheApplicationStartsTheNewTriggerWithNoPreviousFireTime()
    {
        IScheduler scheduler = await SchedulerBuilder.Create("one", "reapplied-reschedule").UseInMemoryStore().BuildScheduler();
        try
        {
            await new DeclaringProcessor(startNow: true).ScheduleJobs(scheduler);
            await FireOnceThenStandBy(scheduler);

            ITrigger replacement = TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .ForJob(jobKey)
                .StartAt(DateTimeOffset.UtcNow.AddHours(1))
                .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
                .Build();

            await scheduler.RescheduleJob(triggerKey, replacement);

            (await scheduler.GetTrigger(triggerKey)).GetPreviousFireTimeUtc().Should().BeNull(
                "an application that reschedules a job hands over a new trigger, and a new trigger has no previous "
                + "fire time; only re-applying a declared trigger keeps the stored one");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }
    }

    /// <summary>
    /// Starts the scheduler, waits for the declared trigger's first firing, and puts the scheduler on standby
    /// once the store has recorded it, answering the previous fire time the trigger now has.
    /// </summary>
    private static async Task<DateTimeOffset> FireOnceThenStandBy(IScheduler scheduler)
    {
        await scheduler.Start();
        (await RecordingJob.NextFiring()).Should().BeNull("the premise: a new trigger's first firing has no previous one");
        await scheduler.Standby();

        DateTimeOffset deadline = DateTimeOffset.UtcNow + firingTimeout;
        while (true)
        {
            DateTimeOffset? previous = (await scheduler.GetTrigger(triggerKey)).GetPreviousFireTimeUtc();
            if (previous is not null && (await scheduler.GetCurrentlyExecutingJobs()).Count == 0)
            {
                return previous.Value;
            }

            DateTimeOffset.UtcNow.Should().BeBefore(deadline, "the firing's completion is recorded promptly");
            await Task.Delay(50);
        }
    }

    private static Task<IScheduler> BuildSqliteScheduler(string connectionString)
    {
        SchedulerBuilder config = SchedulerBuilder.Create("one", "reapplied-sqlite");
        config.UsePersistentStore(store =>
        {
            store.UseMicrosoftSQLite(connectionString);
            store.UseSystemTextJsonSerializer();
        });

        return config.BuildScheduler();
    }

    private static void InstallSchema(string connectionString)
    {
        using SqliteConnection connection = new SqliteConnection(connectionString);
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = File.ReadAllText(ResolveRepositoryFile("database", "tables", "tables_sqlite.sql"));
        command.ExecuteNonQuery();
    }

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
    /// Declares one job and one trigger as the container's processor declares registrations: freshly built
    /// for each start, existing data overwritten.
    /// </summary>
    private sealed class DeclaringProcessor : XMLSchedulingDataProcessor
    {
        private readonly List<IJobDetail> jobs;
        private readonly List<ITrigger> triggers;

        public DeclaringProcessor(bool startNow) : base(new SimpleTypeLoadHelper())
        {
            jobs = [JobBuilder.Create<RecordingJob>().WithIdentity(jobKey).Build()];

            TriggerBuilder trigger = TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .ForJob(jobKey)
                .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever());
            trigger = startNow ? trigger.StartNow() : trigger.StartAt(DateTimeOffset.UtcNow.AddDays(-1));
            triggers = [trigger.Build()];
        }

        public override bool OverWriteExistingData => true;

        protected override IReadOnlyList<IJobDetail> LoadedJobs => jobs;

        protected override IReadOnlyList<ITrigger> LoadedTriggers => triggers;
    }

    public sealed class RecordingJob : IJob
    {
        private static ConcurrentQueue<DateTimeOffset?> firings = new ConcurrentQueue<DateTimeOffset?>();
        private static SemaphoreSlim fired = new SemaphoreSlim(0);

        public static void Reset()
        {
            firings = new ConcurrentQueue<DateTimeOffset?>();
            fired = new SemaphoreSlim(0);
        }

        /// <summary>
        /// The <see cref="IJobExecutionContext.PreviousFireTimeUtc" /> of the next firing to happen.
        /// </summary>
        public static async Task<DateTimeOffset?> NextFiring()
        {
            (await fired.WaitAsync(firingTimeout)).Should().BeTrue("the declared trigger is due now");
            firings.TryDequeue(out DateTimeOffset? previous).Should().BeTrue();
            return previous;
        }

        public Task Execute(IJobExecutionContext context)
        {
            firings.Enqueue(context.PreviousFireTimeUtc);
            fired.Release();
            return Task.CompletedTask;
        }
    }
}
