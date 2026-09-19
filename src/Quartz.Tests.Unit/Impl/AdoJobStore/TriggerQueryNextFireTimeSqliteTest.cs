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
/// <see cref="TriggerQuery.NextFireTimeBefore" /> against a real database, where what it claims is a
/// claim about SQL rather than about a comparison in C#.
/// </summary>
/// <remarks>
/// <para>
/// Two of the claims can be made nowhere else. A row whose <c>NEXT_FIRE_TIME</c> is null has to fall
/// out of the result, because <c>NULL &lt; @cutoff</c> is unknown rather than true on every dialect;
/// and the count idiom runs a statement of its own, so the predicate has to reach that one too or a
/// count would answer for every trigger while the page answered for the overdue ones.
/// </para>
/// <para>
/// The overdue rows are written by hand. <c>QuartzScheduler.ScheduleJob</c> moves a simple trigger
/// whose start time has passed forward to now — deliberately, since #332 — so a trigger that is
/// genuinely late is a row that was written when it was due and has sat there since, which is what
/// these fixtures reproduce and what an operator with a stalled scheduler is looking at.
/// </para>
/// </remarks>
public sealed class TriggerQueryNextFireTimeSqliteTest
{
    private const string Group = "overdue";

    private SqliteTestDatabase database = null!;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        database = new SqliteTestDatabase("next-fire-time-filter");
    }

    [TearDown]
    public void DeleteDatabase()
    {
        database.Dispose();
    }

    [Test]
    public async Task TheFilterSelectsTheTriggersDueBeforeTheCutoffAndNothingElse()
    {
        await using SchedulerHandle handle = await BuildScheduler();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        await Schedule(handle.Scheduler, "was-due", now.AddHours(1));
        await Schedule(handle.Scheduler, "due-later", now.AddHours(1));
        await Schedule(handle.Scheduler, "spent", now.AddHours(1));

        await SetNextFireTime("was-due", now.AddHours(-1));
        await SetNextFireTime("spent", null);

        PagedResult<TriggerHeader> overdue = await handle.Scheduler.QueryTriggers(new TriggerQuery
        {
            NextFireTimeBefore = now
        });

        overdue.Items.Select(x => x.Key.Name).Should().Equal(
            ["was-due"],
            "a trigger due later is not overdue, and one with no next fire time at all is not due before "
            + "anything - NEXT_FIRE_TIME < @cutoff is unknown for a null column rather than true");

        PagedResult<TriggerHeader> everything = await handle.Scheduler.QueryTriggers(new TriggerQuery());
        everything.Items.Should().HaveCount(3, "a null cutoff filters nothing out");
    }

    [Test]
    public async Task TheFilterReachesTheCountStatementAsWellAsThePage()
    {
        await using SchedulerHandle handle = await BuildScheduler();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        await Schedule(handle.Scheduler, "was-due", now.AddHours(1));
        await Schedule(handle.Scheduler, "also-was-due", now.AddHours(1));
        await Schedule(handle.Scheduler, "due-later", now.AddHours(1));

        await SetNextFireTime("was-due", now.AddHours(-1));
        await SetNextFireTime("also-was-due", now.AddHours(-2));

        PagedResult<TriggerHeader> count = await handle.Scheduler.QueryTriggers(new TriggerQuery
        {
            NextFireTimeBefore = now,
            Take = 0,
            IncludeTotalCount = true
        });

        count.TotalCount.Should().Be(
            2,
            "the count idiom runs a statement of its own, and a count that ignored the filter would report "
            + "every trigger as overdue");
    }

    /// <summary>
    /// The query the health check actually issues: schedulable, late, and only the first one.
    /// </summary>
    [Test]
    public async Task TheFilterCombinesWithTheStateFilter()
    {
        await using SchedulerHandle handle = await BuildScheduler();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        await Schedule(handle.Scheduler, "waiting", now.AddHours(1));
        await Schedule(handle.Scheduler, "paused", now.AddHours(1));
        await handle.Scheduler.PauseTrigger(new TriggerKey("paused", Group));

        await SetNextFireTime("waiting", now.AddHours(-1));
        await SetNextFireTime("paused", now.AddHours(-2));

        PagedResult<TriggerHeader> overdue = await handle.Scheduler.QueryTriggers(new TriggerQuery
        {
            State = TriggerState.Normal,
            NextFireTimeBefore = now,
            Take = 1
        });

        overdue.Items.Select(x => x.Key.Name).Should().Equal(
            ["waiting"],
            "a paused trigger is overdue on paper and deliberate in fact, so both filters have to hold at once");
    }

    /// <summary>
    /// Backdates a stored trigger, or — with <see langword="null" /> — leaves the row a completed
    /// trigger has: stored, still <c>WAITING</c>, and with nothing left to fire.
    /// </summary>
    private async Task SetNextFireTime(string triggerName, DateTimeOffset? nextFireTime)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE QRTZ_TRIGGERS SET NEXT_FIRE_TIME = $next WHERE TRIGGER_NAME = $name";

        // UTC ticks, which is what the store writes; see StdAdoDelegate.GetDbDateTimeValue.
        command.Parameters.AddWithValue("$next", (object?) nextFireTime?.UtcTicks ?? DBNull.Value);
        command.Parameters.AddWithValue("$name", triggerName);

        (await command.ExecuteNonQueryAsync()).Should().Be(1, "the fixture has to have changed the row it meant to");
    }

    private static async Task Schedule(IScheduler scheduler, string name, DateTimeOffset startAt)
    {
        IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity(name, Group).Build();

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity(name, Group)
            .ForJob(job)
            .StartAt(startAt)
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
                options.InstanceName = "next-fire-time-filter";
                options.InstanceId = "one";
            });

            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                store.ProvisionSchema();
            });
        });

        ServiceProvider container = services.BuildServiceProvider();

        // Never started: a scheduler thread would acquire the triggers this test backdates, and the
        // misfire handler would give them a new fire time.
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();

        return new SchedulerHandle(container, scheduler);
    }

    private sealed class SchedulerHandle(ServiceProvider container, IScheduler scheduler) : IAsyncDisposable
    {
        public IScheduler Scheduler { get; } = scheduler;

        public async ValueTask DisposeAsync()
        {
            await Scheduler.Shutdown(waitForJobsToComplete: false);
            await container.DisposeAsync();
        }
    }

    public sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
