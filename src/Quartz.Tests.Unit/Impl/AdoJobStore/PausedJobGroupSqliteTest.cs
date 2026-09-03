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

using Quartz.Jobs;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// A paused job group binds what is added to it next, on the persistent store as much as on the
/// in-memory one.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IScheduler.PauseJobGroups" /> promises a pause that "is imposed on jobs added to the
/// group afterwards", and an equality matcher pauses a group that holds no job at all precisely so a
/// caller can pause what is about to be deployed into it. The ADO store used to record the pause and
/// then not impose it: a trigger stored for a job in a recorded-paused group consulted only its own
/// trigger group and the all-groups sentinel, so it was born <c>WAITING</c> and ran — while
/// <c>IsJobGroupPaused</c> and every listing answered that the group was paused.
/// </para>
/// <para>
/// <c>JobStoreContractTest</c> holds both stores to this on every dialect leg. This is the same claim
/// at unit cost: SQLite is a file, so a whole persistent store is a temporary path, and the path the
/// assertion runs through is the ADO store's own store-trigger path rather than a fake's.
/// </para>
/// </remarks>
public sealed class PausedJobGroupSqliteTest
{
    private const string PausedGroup = "gated";
    private const string TriggerGroup = "open";

    private static readonly JobKey jobKey = new("late", PausedGroup);
    private static readonly TriggerKey triggerKey = new("t-late", TriggerGroup);

    private string databaseFile = null!;
    private string connectionString = null!;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        databaseFile = Path.Combine(Path.GetTempPath(), $"quartz-paused-job-group-{Guid.NewGuid():N}.db");
        connectionString = $"Data Source={databaseFile}";
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
    public async Task ATriggerForAJobInAPausedGroupIsBornPaused()
    {
        await using ServiceProvider container = BuildContainer();
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();

        // Nothing is in the group yet, which is the case the equality matcher exists for.
        List<string> paused = await scheduler.PauseJobGroups(GroupMatcher<JobKey>.GroupEquals(PausedGroup));

        paused.Should().Equal([PausedGroup]);
        (await scheduler.IsJobGroupPaused(PausedGroup)).Should().BeTrue(
            "the pause is recorded against the group itself, which is what makes it outlive the jobs "
            + "that were in it when the call was made");

        await scheduler.ScheduleJob(
            JobBuilder.Create<NoOpJob>().WithIdentity(jobKey).Build(),
            TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .StartAt(DateTimeOffset.UtcNow.AddDays(1))
                .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
                .Build());

        (await scheduler.GetTriggerState(triggerKey)).Should().Be(TriggerState.Paused,
            "a job added to a paused group is paused by that pause, so its trigger is born paused "
            + "even though the trigger's own group was never paused");

        (await scheduler.IsTriggerGroupPaused(TriggerGroup)).Should().BeFalse(
            "a job group pause is not recorded as a trigger group pause, or resuming the job group "
            + "would leave the trigger group paused for ever");

        await scheduler.ResumeJobGroups(GroupMatcher<JobKey>.GroupEquals(PausedGroup));

        (await scheduler.GetTriggerState(triggerKey)).Should().Be(TriggerState.Normal,
            "resuming the group releases the triggers the pause was imposing itself on");

        await scheduler.Shutdown(waitForJobsToComplete: false);
    }

    /// <summary>
    /// The store is provisioned and the scheduler is never started, so nothing acquires the trigger
    /// out from under the assertions.
    /// </summary>
    private ServiceProvider BuildContainer()
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = "paused-job-group";
                options.InstanceId = "one";
            });

            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, connectionString);
                store.ProvisionSchema();
            });
        });

        return services.BuildServiceProvider();
    }
}
