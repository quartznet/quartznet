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
using System.IO;
using System.Threading.Tasks;

using Microsoft.Data.Sqlite;

using Quartz.Impl.Matchers;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// A resume-all reaches a trigger group that is paused but holds no triggers.
/// </summary>
/// <remarks>
/// <para>
/// <c>JobStoreSupport.ResumeAll</c> resumed the groups it found in <c>QRTZ_TRIGGERS</c> and then
/// deleted only its all-groups marker, so a group paused before anything was scheduled into it kept
/// its <c>QRTZ_PAUSED_TRIGGER_GRPS</c> row: the resume-all resumed everything visible and left a pause
/// that went on being imposed on whatever was added to that group afterwards. Pausing an empty group
/// is a documented use of the exact-name matcher, so it is a row the store writes on purpose and could
/// not take back.
/// </para>
/// <para>
/// SQLite in a file rather than a container. Nothing here is SQLite's — the statement is
/// <c>StdAdoDelegate</c>'s and therefore every dialect's; SQLite is only the cheapest database to run
/// it against.
/// </para>
/// </remarks>
[NonParallelizable]
[Category("db-sqlite")]
public sealed class ResumeAllEmptyPausedGroupSqliteTest
{
    private const string EmptyGroup = "paused-while-empty";
    private const string OccupiedGroup = "elsewhere";

    private string databaseFile;
    private string connectionString;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        databaseFile = Path.Combine(Path.GetTempPath(), $"quartz-resumeall-{Guid.NewGuid():N}.db");
        connectionString = $"Data Source={databaseFile}";

        InstallSchemaFromFreshInstallScript();
    }

    [TearDown]
    public void DeleteDatabase()
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(databaseFile))
        {
            File.Delete(databaseFile);
        }
    }

    [Test]
    public async Task AResumeAllReachesAGroupThatIsPausedButHoldsNoTriggers()
    {
        IScheduler scheduler = await BuildPersistentScheduler();

        try
        {
            await ResumeAllAndAssertTheEmptyGroupIsFree(scheduler);
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    /// <summary>
    /// The in-memory store has always cleared its whole set of paused groups here, which is the
    /// behaviour the persistent store is being brought in line with — so the same script has to read
    /// the same way against both.
    /// </summary>
    [Test]
    public async Task TheInMemoryStoreDoesTheSame()
    {
        SchedulerBuilder config = SchedulerBuilder.Create("one", "resumeall-ram");
        config.UseInMemoryStore();

        IScheduler scheduler = await config.BuildScheduler();

        try
        {
            await ResumeAllAndAssertTheEmptyGroupIsFree(scheduler);
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    private static async Task ResumeAllAndAssertTheEmptyGroupIsFree(IScheduler scheduler)
    {
        // Something for the resume-all to find in the trigger table, so that the loop over the groups it
        // knows about is not vacuously empty.
        await scheduler.ScheduleJob(Job("occupied", OccupiedGroup), Trigger("occupied", OccupiedGroup));

        await scheduler.PauseTriggers(GroupMatcher<TriggerKey>.GroupEquals(EmptyGroup));

        (await scheduler.GetPausedTriggerGroups()).Should().Contain(EmptyGroup,
            "pausing a group that holds nothing yet is how a caller pauses what is about to be added to it");

        await scheduler.ResumeAll();

        (await scheduler.GetPausedTriggerGroups()).Should().BeEmpty(
            "a resume-all has to reach a group that is paused but holds no triggers, which the group "
            + "listing could otherwise never show a caller a way to resume");

        await scheduler.ScheduleJob(Job("late", EmptyGroup), Trigger("late", EmptyGroup));

        (await scheduler.GetTriggerState(new TriggerKey("late", EmptyGroup))).Should().Be(TriggerState.Normal,
            "resume-all resumed everything, so there is no pause left to impose on what is added afterwards");
    }

    private static IJobDetail Job(string name, string group)
    {
        return JobBuilder.Create<NoOpTestJob>().WithIdentity(name, group).Build();
    }

    /// <summary>
    /// A year out: every assertion here is about stored state, and nothing should fire while the test
    /// reads it.
    /// </summary>
    private static ITrigger Trigger(string name, string group)
    {
        return TriggerBuilder.Create()
            .WithIdentity(name, group)
            .StartAt(DateTimeOffset.UtcNow.AddYears(1))
            .Build();
    }

    /// <summary>
    /// Never started: the triggers are a year out and the test reads the store rather than watching it
    /// run.
    /// </summary>
    private Task<IScheduler> BuildPersistentScheduler()
    {
        SchedulerBuilder config = SchedulerBuilder.Create("one", "resumeall-sqlite");
        config.UsePersistentStore(store =>
        {
            store.UseMicrosoftSQLite(connectionString);
            store.UseSystemTextJsonSerializer();
        });

        return config.BuildScheduler();
    }

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

    public sealed class NoOpTestJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }
}
