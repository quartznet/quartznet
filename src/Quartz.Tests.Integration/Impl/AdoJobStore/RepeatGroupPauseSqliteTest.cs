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

using Quartz.Impl.Matchers;
using Quartz.Listener;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// Pausing a group that is paused already pauses every member of it again, one resumed on its own
/// included, and names the group in its answer, which the scheduler hands its listeners (#4331).
/// </summary>
/// <remarks>
/// <para>
/// The ADO.NET store has always done this. <c>RAMJobStore</c> paused the members of a group only when
/// the call newly recorded the group, and left that group out of its answer, so the same script read
/// differently against the two stores. It runs against both here.
/// </para>
/// <para>
/// SQLite in a file rather than a container. Nothing here is SQLite's: the statements are
/// <c>StdAdoDelegate</c>'s and therefore every dialect's.
/// </para>
/// </remarks>
[NonParallelizable]
[Category("db-sqlite")]
[TestFixture("sqlite")]
[TestFixture("ram")]
public sealed class RepeatGroupPauseSqliteTest
{
    private const string Group = "pauses";

    private readonly string store;

    private string databaseFile;
    private IScheduler scheduler;
    private PauseRecorder recorder;

    public RepeatGroupPauseSqliteTest(string store)
    {
        this.store = store;
    }

    [SetUp]
    public async Task BuildScheduler()
    {
        SchedulerBuilder config = SchedulerBuilder.Create("one", "repeat-group-pause-" + store);

        if (store == "sqlite")
        {
            databaseFile = Path.Combine(Path.GetTempPath(), $"quartz-repeatpause-{Guid.NewGuid():N}.db");
            string connectionString = $"Data Source={databaseFile}";
            InstallSchemaFromFreshInstallScript(connectionString);

            config.UsePersistentStore(options =>
            {
                options.UseMicrosoftSQLite(connectionString);
                options.UseSystemTextJsonSerializer();
            });
        }
        else
        {
            config.UseInMemoryStore();
        }

        // Never started: the triggers are a year out and the test reads the store rather than watching it run.
        scheduler = await config.BuildScheduler();
        recorder = new PauseRecorder();
        scheduler.ListenerManager.AddSchedulerListener(recorder);
    }

    [TearDown]
    public async Task DeleteDatabase()
    {
        if (scheduler != null)
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }

        if (databaseFile != null)
        {
            SqliteConnection.ClearAllPools();

            if (File.Exists(databaseFile))
            {
                File.Delete(databaseFile);
            }
        }
    }

    [Test]
    public async Task PausingAPausedTriggerGroupAgainPausesATriggerResumedOnItsOwn()
    {
        TriggerKey resumed = await Schedule("resumed");
        TriggerKey stayed = await Schedule("stayed");

        await scheduler.PauseTriggers(GroupMatcher<TriggerKey>.GroupEquals(Group));
        recorder.Take().Should().Equal(["trigger group " + Group]);
        await scheduler.ResumeTrigger(resumed);
        (await scheduler.GetTriggerState(resumed)).Should().Be(TriggerState.Normal);

        await scheduler.PauseTriggers(GroupMatcher<TriggerKey>.GroupEquals(Group));

        (await scheduler.GetTriggerState(resumed)).Should().Be(TriggerState.Paused,
            "a group pause pauses every trigger in the group, whether or not the group was paused already");
        (await scheduler.GetTriggerState(stayed)).Should().Be(TriggerState.Paused);
        recorder.Take().Should().Equal(["trigger group " + Group],
            "a group that was paused already is still a group this call matched and paused");
    }

    [Test]
    public async Task PausingAPausedJobGroupAgainPausesAJobResumedOnItsOwn()
    {
        IJobDetail resumedJob = Job("resumed", Group);
        IJobDetail stayedJob = Job("stayed", Group);
        TriggerKey resumed = await Schedule("resumed", resumedJob);
        TriggerKey stayed = await Schedule("stayed", stayedJob);

        await scheduler.PauseJobs(GroupMatcher<JobKey>.GroupEquals(Group));
        recorder.Take().Should().Equal(["job group " + Group]);
        await scheduler.ResumeJob(resumedJob.Key);
        (await scheduler.GetTriggerState(resumed)).Should().Be(TriggerState.Normal);

        await scheduler.PauseJobs(GroupMatcher<JobKey>.GroupEquals(Group));

        (await scheduler.GetTriggerState(resumed)).Should().Be(TriggerState.Paused,
            "a group pause pauses every job in the group, whether or not the group was paused already");
        (await scheduler.GetTriggerState(stayed)).Should().Be(TriggerState.Paused);
        recorder.Take().Should().Equal(["job group " + Group],
            "a group that was paused already is still a group this call matched and paused");
    }

    [Test]
    public async Task APatternTriggerGroupPauseOverAPausedGroupAndANewOnePausesBothAndAnswersBoth()
    {
        TriggerKey resumed = await Schedule("resumed", group: "batch-old");
        TriggerKey fresh = await Schedule("fresh", group: "batch-new");

        await scheduler.PauseTriggers(GroupMatcher<TriggerKey>.GroupEquals("batch-old"));
        await scheduler.ResumeTrigger(resumed);
        recorder.Take();

        await scheduler.PauseTriggers(GroupMatcher<TriggerKey>.GroupStartsWith("batch-"));

        (await scheduler.GetTriggerState(resumed)).Should().Be(TriggerState.Paused,
            "the pattern matched the group that was paused already, so its triggers are paused again");
        (await scheduler.GetTriggerState(fresh)).Should().Be(TriggerState.Paused);
        recorder.Take().Should().BeEquivalentTo(["trigger group batch-old", "trigger group batch-new"],
            "the answer is every group the pattern matched, the one that was paused already included");
    }

    [Test]
    public async Task APatternJobGroupPauseOverAPausedGroupAndANewOnePausesBothAndAnswersBoth()
    {
        IJobDetail resumedJob = Job("resumed", "batch-old");
        IJobDetail freshJob = Job("fresh", "batch-new");
        TriggerKey resumed = await Schedule("resumed", resumedJob);
        TriggerKey fresh = await Schedule("fresh", freshJob);

        await scheduler.PauseJobs(GroupMatcher<JobKey>.GroupEquals("batch-old"));
        await scheduler.ResumeJob(resumedJob.Key);
        recorder.Take();

        await scheduler.PauseJobs(GroupMatcher<JobKey>.GroupStartsWith("batch-"));

        (await scheduler.GetTriggerState(resumed)).Should().Be(TriggerState.Paused,
            "the pattern matched the group that was paused already, so its jobs are paused again");
        (await scheduler.GetTriggerState(fresh)).Should().Be(TriggerState.Paused);
        recorder.Take().Should().BeEquivalentTo(["job group batch-old", "job group batch-new"],
            "the answer is every group the pattern matched, the one that was paused already included");
    }

    [Test]
    public async Task PauseAllPausesATriggerResumedOnItsOwnInsideAPausedGroup()
    {
        TriggerKey resumed = await Schedule("resumed");
        TriggerKey stayed = await Schedule("stayed");

        await scheduler.PauseTriggers(GroupMatcher<TriggerKey>.GroupEquals(Group));
        await scheduler.ResumeTrigger(resumed);

        await scheduler.PauseAll();

        (await scheduler.GetTriggerState(resumed)).Should().Be(TriggerState.Paused,
            "pause-all pauses every trigger, including one in a group that was paused already");
        (await scheduler.GetTriggerState(stayed)).Should().Be(TriggerState.Paused);
    }

    private static IJobDetail Job(string name, string group)
    {
        return JobBuilder.Create<NoOpTestJob>().WithIdentity(name, group).Build();
    }

    /// <summary>
    /// Schedules a job of its own with a trigger in <paramref name="group" />.
    /// </summary>
    private Task<TriggerKey> Schedule(string name, string group = Group)
    {
        return Schedule(name, Job("job-" + name + "-" + group, Group), group);
    }

    /// <summary>
    /// A year out: every assertion here is about stored state, and nothing should fire while the test
    /// reads it.
    /// </summary>
    private async Task<TriggerKey> Schedule(string name, IJobDetail job, string group = Group)
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity(name, group)
            .StartAt(DateTimeOffset.UtcNow.AddYears(1))
            .Build();

        await scheduler.ScheduleJob(job, trigger);
        return trigger.Key;
    }

    private static void InstallSchemaFromFreshInstallScript(string connectionString)
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
    /// What the scheduler told its listeners, which on this branch is the only place a caller sees
    /// the groups a group pause answered.
    /// </summary>
    private sealed class PauseRecorder : SchedulerListenerSupport
    {
        private readonly ConcurrentQueue<string> events = new ConcurrentQueue<string>();

        /// <summary>
        /// The events since the last call, in the order they were raised.
        /// </summary>
        public List<string> Take()
        {
            List<string> taken = [];
            while (events.TryDequeue(out string item))
            {
                taken.Add(item);
            }

            return taken;
        }

        public override Task JobsPaused(string jobGroup, CancellationToken cancellationToken = default)
        {
            events.Enqueue("job group " + jobGroup);
            return Task.CompletedTask;
        }

        public override Task TriggersPaused(string triggerGroup, CancellationToken cancellationToken = default)
        {
            events.Enqueue("trigger group " + triggerGroup);
            return Task.CompletedTask;
        }
    }

    public sealed class NoOpTestJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }
}
