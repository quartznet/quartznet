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

using FakeItEasy;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// A trigger registered with <c>AddTrigger</c> that the store already holds is re-applied when the
/// scheduler is created, and it keeps the time it last fired (#1834, #4122).
/// </summary>
/// <remarks>
/// Each start is a container of its own over the same store: the in-memory store is one instance handed
/// to both, the SQLite one is one file. The scheduler is never started; the firings are driven through
/// the store, and <see cref="TriggerFiredBundle.PreviousFireTimeUtc" /> is what the job's
/// <see cref="IJobExecutionContext.PreviousFireTimeUtc" /> is read from.
/// </remarks>
[NonParallelizable]
public sealed class TriggerReappliedAtStartupTest
{
    private static readonly DateTimeOffset epoch = new(2031, 6, 17, 10, 0, 0, TimeSpan.Zero);
    private static readonly JobKey jobKey = new("declared", "restart");
    private static readonly TriggerKey triggerKey = new("t-declared", "restart");

    private FakeTimeProvider clock = null!;
    private SqliteTestDatabase? database;

    [SetUp]
    public void StartTheClock()
    {
        clock = new FakeTimeProvider(epoch);
    }

    [TearDown]
    public void DeleteDatabase()
    {
        database?.Dispose();
        database = null;
    }

    [Test]
    public async Task InMemoryStoreKeepsThePreviousFireTimeOfARegisteredTriggerAcrossARestart()
    {
        RAMJobStore store = new(NullLoggerFactory.Instance, A.Fake<ISchedulerSignaler>(), clock);

        await KeepsThePreviousFireTimeAcrossARestart(q => q.UseJobStore(store), _ => store);
    }

    [Test]
    public async Task SqliteStoreKeepsThePreviousFireTimeOfARegisteredTriggerAcrossARestart()
    {
        database = new SqliteTestDatabase("trigger-reapplied-at-startup");
        string connectionString = database.ConnectionString;

        await KeepsThePreviousFireTimeAcrossARestart(
            q => q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, connectionString);
                store.ProvisionSchema();
            }),
            provider => provider.GetRequiredService<IJobStore>());
    }

    /// <summary>
    /// Carrying the previous fire time over must not move the next one. A simple trigger whose start time
    /// is behind it has that start moved up to now when it has never fired, and a previous fire time is what
    /// says it has: left to <c>RescheduleJob</c>, the carried value would have stopped the move and stored a
    /// next fire time a day in the past.
    /// </summary>
    [Test]
    public async Task CarryingThePreviousFireTimeLeavesTheNextFireTimeOfAPastStartWhereItWas()
    {
        RAMJobStore store = new(NullLoggerFactory.Instance, A.Fake<ISchedulerSignaler>(), clock);
        DateTimeOffset dayAgo = epoch.AddDays(-1);

        await using (ServiceProvider first = BuildContainer(q => q.UseJobStore(store), trigger => trigger.StartAt(dayAgo)))
        {
            await first.GetRequiredService<ISchedulerFactory>().GetScheduler();
            await FireAndComplete(store);
        }

        clock.Advance(TimeSpan.FromMinutes(10));
        DateTimeOffset restartedAt = clock.GetUtcNow();

        await using ServiceProvider restarted = BuildContainer(q => q.UseJobStore(store), trigger => trigger.StartAt(dayAgo));
        await restarted.GetRequiredService<ISchedulerFactory>().GetScheduler();

        ITrigger? stored = await store.GetTrigger(triggerKey);
        stored.Should().NotBeNull();
        stored!.PreviousFireTimeUtc.Should().Be(epoch, "the trigger last fired at the first start");
        stored.NextFireTimeUtc.Should().Be(restartedAt,
            "a re-applied simple trigger starting in the past is due from the restart, as it was before its "
            + "previous fire time was kept");
    }

    /// <summary>
    /// What a start does is not what an application's own <c>RescheduleJob</c> does: a trigger handed to
    /// that is a new trigger, and it has never fired.
    /// </summary>
    [Test]
    public async Task RescheduleJobByTheApplicationStartsTheNewTriggerWithNoPreviousFireTime()
    {
        RAMJobStore store = new(NullLoggerFactory.Instance, A.Fake<ISchedulerSignaler>(), clock);

        await using ServiceProvider provider = BuildContainer(q => q.UseJobStore(store));
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await FireAndComplete(store);

        (await store.GetTrigger(triggerKey))!.PreviousFireTimeUtc.Should().Be(epoch, "the premise: the trigger has fired");

        ITrigger replacement = TriggerBuilder.Create(clock)
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .StartNow()
            .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromMinutes(30)).RepeatForever())
            .Build();

        await scheduler.RescheduleJob(triggerKey, replacement);

        (await store.GetTrigger(triggerKey))!.PreviousFireTimeUtc.Should().BeNull(
            "an application that reschedules a job hands over a new trigger, and a new trigger has no previous "
            + "fire time; only re-applying a declared trigger at a start keeps the stored one");
    }

    private async Task KeepsThePreviousFireTimeAcrossARestart(
        Action<IQuartzBuilder> useStore,
        Func<ServiceProvider, IJobStore> storeOf)
    {
        await using (ServiceProvider first = BuildContainer(useStore))
        {
            // Created and never started: creating it stores the declared job and trigger.
            await first.GetRequiredService<ISchedulerFactory>().GetScheduler();

            TriggerFiredBundle firstFiring = await FireAndComplete(storeOf(first));
            firstFiring.PreviousFireTimeUtc.Should().BeNull("the premise: the first firing of a new trigger has no previous one");
        }

        clock.Advance(TimeSpan.FromMinutes(10));

        await using ServiceProvider restarted = BuildContainer(useStore);
        await restarted.GetRequiredService<ISchedulerFactory>().GetScheduler();
        IJobStore store = storeOf(restarted);

        ITrigger? stored = await store.GetTrigger(triggerKey);
        stored.Should().NotBeNull();
        stored!.PreviousFireTimeUtc.Should().Be(epoch,
            "creating the scheduler re-applies the declared trigger, and re-applying the same trigger must not "
            + "forget that it has fired");

        TriggerFiredBundle nextFiring = await FireAndComplete(store);
        nextFiring.PreviousFireTimeUtc.Should().Be(epoch,
            "context.PreviousFireTimeUtc on the first firing after a restart is the firing before the restart (#1834)");
    }

    private async Task<TriggerFiredBundle> FireAndComplete(IJobStore store)
    {
        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = clock.GetUtcNow().AddMinutes(1),
            MaxCount = 1,
            TimeWindow = TimeSpan.Zero,
        });
        IOperableTrigger firing = acquired.Should().ContainSingle().Which;
        firing.Key.Should().Be(triggerKey);

        List<TriggerFiredResult> fired = await store.TriggersFired(acquired);
        TriggerFiredBundle bundle = fired.Should().ContainSingle().Which.TriggerFiredBundle!;
        bundle.Should().NotBeNull("the trigger is due and nothing stands in its way");

        await store.TriggeredJobComplete(firing, bundle.JobDetail, SchedulerInstruction.NoInstruction);
        return bundle;
    }

    private ServiceProvider BuildContainer(
        Action<IQuartzBuilder> useStore,
        Action<ITriggerConfigurator<IJob>>? start = null)
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = "trigger-reapplied-at-startup";
                options.InstanceId = "one";
            });
            q.UseTimeProvider(clock);
            useStore(q);

            q.AddJob<NoOpJob>(job => job.WithIdentity(jobKey));
            q.AddTrigger(trigger =>
            {
                trigger
                    .WithIdentity(triggerKey)
                    .ForJob(jobKey)
                    .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromHours(1)).RepeatForever());

                if (start is null)
                {
                    trigger.StartNow();
                }
                else
                {
                    start(trigger);
                }
            });
        });

        return services.BuildServiceProvider();
    }

    public sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }
}
