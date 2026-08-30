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

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// Deleting a whole group against a real database, and answering the same as the in-memory store does.
/// </summary>
/// <remarks>
/// <para>
/// <c>AdoBulkDeleteTest</c> pins how the group form reaches the database — one lock, keys resolved by
/// the delegate inside it — over a faked delegate that reaches nothing. What that cannot show is the
/// cascade actually running: a trigger row taken with its job, a non-durable job taken with its last
/// trigger, a paused group left recorded. Those are claims about SQL, and SQLite is a file, so they
/// can be made here rather than only in the container legs.
/// </para>
/// <para>
/// The assertions are deliberately the ones <c>BulkKeySetOperationsTest</c> makes of
/// <c>RAMJobStore</c>. Two stores answering the same question the same way is the contract; a
/// divergence found here is a bug in one of them rather than a dialect detail.
/// </para>
/// </remarks>
public sealed class AdoGroupDeleteSqliteTest
{
    private const string SagaGroup = "saga-17";
    private const string OtherGroup = "saga-18";

    private string databaseFile = null!;
    private string connectionString = null!;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        databaseFile = Path.Combine(Path.GetTempPath(), $"quartz-group-delete-{Guid.NewGuid():N}.db");
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
    public async Task DeletingAGroupOfJobsTakesTheirTriggersAndLeavesTheOtherGroups()
    {
        await using SchedulerHandle handle = await BuildScheduler();
        IScheduler scheduler = handle.Scheduler;

        await Schedule(scheduler, "first", SagaGroup);
        await Schedule(scheduler, "second", SagaGroup);
        await Schedule(scheduler, "elsewhere", OtherGroup);

        List<JobKey> deleted = await scheduler.DeleteJobs(GroupMatcher<JobKey>.GroupEquals(SagaGroup));

        deleted.Should().BeEquivalentTo([new JobKey("first", SagaGroup), new JobKey("second", SagaGroup)],
            "the answer names the jobs the call deleted, as the in-memory store's does");

        (await scheduler.Exists(new JobKey("first", SagaGroup))).Should().BeFalse();
        (await scheduler.Exists(new JobKey("second", SagaGroup))).Should().BeFalse();
        (await scheduler.Exists(new TriggerKey("first", SagaGroup))).Should().BeFalse(
            "a job's triggers go with it, which against a database is a cascade rather than one statement");

        (await scheduler.Exists(new JobKey("elsewhere", OtherGroup))).Should().BeTrue();
        (await scheduler.Exists(new TriggerKey("elsewhere", OtherGroup))).Should().BeTrue();
    }

    [Test]
    public async Task UnschedulingAGroupOfTriggersTakesTheNonDurableJobsItOrphans()
    {
        await using SchedulerHandle handle = await BuildScheduler();
        IScheduler scheduler = handle.Scheduler;

        await Schedule(scheduler, "perishable", SagaGroup);
        await Schedule(scheduler, "durable", SagaGroup, durable: true);
        await Schedule(scheduler, "elsewhere", OtherGroup);

        List<TriggerKey> unscheduled = await scheduler.UnscheduleJobs(GroupMatcher<TriggerKey>.GroupEquals(SagaGroup));

        unscheduled.Should().BeEquivalentTo(
            [new TriggerKey("perishable", SagaGroup), new TriggerKey("durable", SagaGroup)],
            "the answer names triggers only — the job an unschedule orphans is deleted, not reported");

        (await scheduler.Exists(new JobKey("perishable", SagaGroup))).Should().BeFalse(
            "a non-durable job that has lost its last trigger goes with it");
        (await scheduler.Exists(new JobKey("durable", SagaGroup))).Should().BeTrue(
            "a durable job survives having no triggers, which is what durable means");

        (await scheduler.Exists(new TriggerKey("elsewhere", OtherGroup))).Should().BeTrue();
    }

    /// <summary>
    /// Emptying a paused group does not un-pause it.
    /// </summary>
    /// <remarks>
    /// The pause is remembered per group precisely so that a job added to it later is born paused, and
    /// a delete has no business forgetting that: the group is emptier, not resumed. The in-memory
    /// store keeps its entry in the paused-group set, and the ADO store must keep its
    /// <c>PAUSED_JOB_GRPS</c> row — which is asserted against the table itself, because "a group with
    /// no jobs in it" is exactly the case a listing has an opinion about.
    /// </remarks>
    [Test]
    public async Task EmptyingAPausedJobGroupLeavesThePauseRecorded()
    {
        await using SchedulerHandle handle = await BuildScheduler();
        IScheduler scheduler = handle.Scheduler;

        await Schedule(scheduler, "first", SagaGroup);
        await scheduler.PauseJobs(GroupMatcher<JobKey>.GroupEquals(SagaGroup));

        (await CountOfPausedJobGroups()).Should().Be(1, "the pause was recorded before anything was deleted");

        await scheduler.DeleteJobs(GroupMatcher<JobKey>.GroupEquals(SagaGroup));

        (await CountOfPausedJobGroups()).Should().Be(1,
            "a delete empties the group; resuming it is a separate decision the caller has not made");
    }

    [Test]
    public async Task AGroupThatMatchesNothingDeletesNothing()
    {
        await using SchedulerHandle handle = await BuildScheduler();
        IScheduler scheduler = handle.Scheduler;

        await Schedule(scheduler, "untouched", SagaGroup);

        (await scheduler.DeleteJobs(GroupMatcher<JobKey>.GroupEquals("no-such-group"))).Should().BeEmpty();
        (await scheduler.UnscheduleJobs(GroupMatcher<TriggerKey>.GroupEquals("no-such-group"))).Should().BeEmpty();

        (await scheduler.Exists(new JobKey("untouched", SagaGroup))).Should().BeTrue();
    }

    private async Task<int> CountOfPausedJobGroups()
    {
        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM QRTZ_PAUSED_JOB_GRPS";

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task Schedule(IScheduler scheduler, string name, string group, bool durable = false)
    {
        IJobDetail job = JobBuilder.Create<NoOpGroupJob>()
            .WithIdentity(name, group)
            .StoreDurably(durable)
            .Build();

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity(name, group)
            .ForJob(job)
            // Far enough out that nothing fires while the test drives the store by hand.
            .StartAt(DateTimeOffset.UtcNow.AddYears(1))
            .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build();

        await scheduler.ScheduleJob(job, trigger);
    }

    private async Task<SchedulerHandle> BuildScheduler()
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = "group-delete";
                options.InstanceId = "one";
            });

            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, connectionString);
                // #3550: the store creates the schema it needs, so the test needs no script of its own.
                store.ProvisionSchema();
            });
        });

        ServiceProvider container = services.BuildServiceProvider();

        // Never started: the scheduler thread would acquire the triggers this test deletes underneath it.
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();

        return new SchedulerHandle(container, scheduler);
    }

    private sealed class SchedulerHandle : IAsyncDisposable
    {
        private readonly ServiceProvider container;

        public SchedulerHandle(ServiceProvider container, IScheduler scheduler)
        {
            this.container = container;
            Scheduler = scheduler;
        }

        public IScheduler Scheduler { get; }

        public async ValueTask DisposeAsync()
        {
            await Scheduler.Shutdown(waitForJobsToComplete: false);
            await container.DisposeAsync();
        }
    }

    public sealed class NoOpGroupJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
