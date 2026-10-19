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
/// A declared trigger the store already holds is re-applied when the scheduler is created, and keeps
/// what it has done: how many times it has fired, a retry it is waiting on, and a pause an operator put
/// on it (#4128). A changed schedule starts fresh.
/// </summary>
/// <remarks>
/// The shape of <see cref="TriggerReappliedAtStartupTest" />: each start is a container of its own over
/// the same store, the scheduler is never started, and the firings are driven through the store on a
/// <see cref="FakeTimeProvider" />.
/// </remarks>
[NonParallelizable]
public sealed class TriggerRunStateAcrossARestartTest
{
    public enum StoreKind
    {
        InMemory,
        Sqlite,
    }

    private static readonly DateTimeOffset epoch = new(2031, 6, 17, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan interval = TimeSpan.FromHours(1);
    private static readonly JobKey jobKey = new("declared", "run-state");
    private static readonly TriggerKey triggerKey = new("t-declared", "run-state");

    private FakeTimeProvider clock = null!;
    private RAMJobStore? memoryStore;
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
        memoryStore = null;
    }

    [TestCase(StoreKind.InMemory)]
    [TestCase(StoreKind.Sqlite)]
    public async Task AFiniteRepeatCountIsNotRestartedByAStart(StoreKind kind)
    {
        Action<ITriggerConfigurator<IJob>> fourFirings = t => t.WithSimpleSchedule(s => s.WithInterval(interval).WithRepeatCount(3));

        await using (ServiceProvider first = await Start(kind, fourFirings))
        {
            await FireAndComplete(StoreOf(first));
            clock.Advance(interval);
            await FireAndComplete(StoreOf(first));
        }

        clock.Advance(TimeSpan.FromMinutes(30));
        await using ServiceProvider restarted = await Start(kind, fourFirings);
        IJobStore store = StoreOf(restarted);

        ISimpleTrigger stored = (ISimpleTrigger) (await store.GetTrigger(triggerKey))!;
        stored.TimesTriggered.Should().Be(2, "the trigger fired twice before the start, and the start re-applies the same trigger");

        (await FireUntilDone(store)).Should().Be(2,
            "a repeat count of three is four firings in all, two of which happened before the start");
    }

    [TestCase(StoreKind.InMemory)]
    [TestCase(StoreKind.Sqlite)]
    public async Task AChangedRepeatCountStartsTheCountAgain(StoreKind kind)
    {
        await using (ServiceProvider first = await Start(kind, t => t.WithSimpleSchedule(s => s.WithInterval(interval).WithRepeatCount(3))))
        {
            await FireAndComplete(StoreOf(first));
            clock.Advance(interval);
            await FireAndComplete(StoreOf(first));
        }

        clock.Advance(TimeSpan.FromMinutes(30));
        await using ServiceProvider restarted = await Start(kind, t => t.WithSimpleSchedule(s => s.WithInterval(interval).WithRepeatCount(5)));
        IJobStore store = StoreOf(restarted);

        ((ISimpleTrigger) (await store.GetTrigger(triggerKey))!).TimesTriggered.Should().Be(0,
            "the declaration changed its schedule, which makes it a new trigger");

        (await FireUntilDone(store)).Should().Be(6, "a changed schedule wins, and runs its whole count");
    }

    [TestCase(StoreKind.InMemory)]
    [TestCase(StoreKind.Sqlite)]
    public async Task ARecurrenceRuleCountIsNotRestartedByAStart(StoreKind kind)
    {
        Action<ITriggerConfigurator<IJob>> threeOccurrences = t => t.WithSchedule(
            RecurrenceScheduleBuilder.Create("FREQ=HOURLY;COUNT=3").InTimeZone(TimeZoneInfo.Utc));

        await using (ServiceProvider first = await Start(kind, threeOccurrences))
        {
            await FireAndComplete(StoreOf(first));
            clock.Advance(interval);
            await FireAndComplete(StoreOf(first));
        }

        clock.Advance(TimeSpan.FromMinutes(30));
        await using ServiceProvider restarted = await Start(kind, threeOccurrences);

        (await FireUntilDone(StoreOf(restarted))).Should().Be(1,
            "COUNT=3 is counted against the trigger's fire count, and two of the three happened before the start");
    }

    [TestCase(StoreKind.InMemory)]
    [TestCase(StoreKind.Sqlite)]
    public async Task ARetryPendingAtShutdownKeepsItsTimeAndAttempt(StoreKind kind)
    {
        Action<ITriggerConfigurator<IJob>> retrying = t => t
            .WithSimpleSchedule(s => s.WithInterval(interval).RepeatForever())
            .WithRetryPolicy(RetryPolicy.Fixed(3, TimeSpan.FromMinutes(5)));
        DateTimeOffset retryAt = epoch.AddMinutes(5);

        await using (ServiceProvider first = await Start(kind, retrying))
        {
            await FireAndComplete(StoreOf(first), new JobExecutionException(new InvalidOperationException("transient")));

            ITrigger beforeShutdown = (await StoreOf(first).GetTrigger(triggerKey))!;
            beforeShutdown.RetryAttempt.Should().Be(1, "the premise: the failed firing scheduled a retry");
            beforeShutdown.NextFireTimeUtc.Should().Be(retryAt, "the premise: the retry is five minutes after the failure");
        }

        clock.Advance(TimeSpan.FromMinutes(2));
        await using ServiceProvider restarted = await Start(kind, retrying);
        IJobStore store = StoreOf(restarted);

        ITrigger stored = (await store.GetTrigger(triggerKey))!;
        stored.RetryAttempt.Should().Be(1, "the retry pending at shutdown is still pending");
        stored.NextFireTimeUtc.Should().Be(retryAt, "a retry pending at shutdown still happens at its time");

        clock.Advance(TimeSpan.FromMinutes(3));
        (IOperableTrigger retry, TriggerFiredBundle bundle) = await Fire(store);

        retry.RetryAttempt.Should().Be(1, "the firing is the retry, and says which attempt it is");
        retry.NextFireTimeUtc.Should().Be(epoch.Add(interval),
            "after the retry the trigger goes back to the occurrence its schedule calls for, from the start it was scheduled from");
        await Complete(store, retry, bundle);
    }

    [TestCase(StoreKind.InMemory)]
    [TestCase(StoreKind.Sqlite)]
    public async Task ATriggerPausedOnItsOwnStaysPausedAcrossAStart(StoreKind kind)
    {
        await using (ServiceProvider first = await Start(kind))
        {
            IScheduler scheduler = await SchedulerOf(first);
            (await scheduler.PauseTriggerWith(triggerKey, new PauseDetails { Reason = "maintenance window", RequestedBy = "ops" }))
                .Should().BeTrue();
        }

        clock.Advance(TimeSpan.FromMinutes(10));
        await using ServiceProvider restarted = await Start(kind);
        IScheduler again = await SchedulerOf(restarted);

        (await again.GetTriggerState(triggerKey)).Should().Be(TriggerState.Paused,
            "an operator paused this trigger, and a redeploy is not somebody resuming it");

        PauseInfo? pause = await again.GetTriggerPause(triggerKey);
        pause.Should().NotBeNull("the pause keeps its record, so the dashboard still says why the trigger is paused");
        pause!.Reason.Should().Be("maintenance window");
        pause.RequestedBy.Should().Be("ops");
        pause.PausedAtUtc.Should().Be(epoch, "the pause began before the start, not at it");

        (await AcquireDue(StoreOf(restarted))).Should().BeEmpty("a paused trigger is never acquired");
    }

    [TestCase(StoreKind.InMemory)]
    [TestCase(StoreKind.Sqlite)]
    public async Task ATriggerInAPausedGroupStaysPausedAcrossAStart(StoreKind kind)
    {
        await using (ServiceProvider first = await Start(kind))
        {
            IScheduler scheduler = await SchedulerOf(first);
            await scheduler.PauseTriggerGroups(GroupMatcher<TriggerKey>.GroupEquals(triggerKey.Group));
        }

        await using ServiceProvider restarted = await Start(kind);
        IScheduler again = await SchedulerOf(restarted);

        (await again.GetTriggerState(triggerKey)).Should().Be(TriggerState.Paused, "a paused group imposes its pause on what is stored into it");

        await again.ResumeTriggerGroups(GroupMatcher<TriggerKey>.GroupEquals(triggerKey.Group));
        (await again.GetTriggerState(triggerKey)).Should().Be(TriggerState.Normal, "resuming the group resumes its triggers, as before");
    }

    /// <summary>
    /// Only a start keeps a pause through a replace. A trigger handed to <c>RescheduleJob</c> by the
    /// application is a new schedule, and ends an individual pause as it always has — including a trigger
    /// read back from the store just after a start re-applied it.
    /// </summary>
    [TestCase(StoreKind.InMemory)]
    [TestCase(StoreKind.Sqlite)]
    public async Task RescheduleJobByTheApplicationStillEndsAnIndividualPause(StoreKind kind)
    {
        await using (ServiceProvider first = await Start(kind))
        {
            await (await SchedulerOf(first)).PauseTrigger(triggerKey);
        }

        await using ServiceProvider restarted = await Start(kind);
        IScheduler again = await SchedulerOf(restarted);
        (await again.GetTriggerState(triggerKey)).Should().Be(TriggerState.Paused, "the premise: the start kept the pause");

        ITrigger readBack = (await again.GetTrigger(triggerKey))!;
        (await again.RescheduleJob(triggerKey, readBack)).Should().NotBeNull();

        (await again.GetTriggerState(triggerKey)).Should().Be(TriggerState.Normal,
            "a replace the application asks for decides the state afresh, which is the store contract and Java's");
    }

    private async Task<ServiceProvider> Start(StoreKind kind, Action<ITriggerConfigurator<IJob>>? schedule = null)
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = "trigger-run-state-across-a-restart";
                options.InstanceId = "one";
            });
            q.UseTimeProvider(clock);
            UseStore(q, kind);

            q.AddJob<NoOpJob>(job => job.WithIdentity(jobKey));
            q.AddTrigger(trigger =>
            {
                trigger.WithIdentity(triggerKey).ForJob(jobKey).StartNow();

                if (schedule is null)
                {
                    trigger.WithSimpleSchedule(s => s.WithInterval(interval).RepeatForever());
                }
                else
                {
                    schedule(trigger);
                }
            });
        });

        ServiceProvider provider = services.BuildServiceProvider();

        // Created and never started: creating it stores the declared job and trigger, or re-applies them.
        await SchedulerOf(provider);
        return provider;
    }

    private void UseStore(IQuartzBuilder q, StoreKind kind)
    {
        if (kind == StoreKind.InMemory)
        {
            memoryStore ??= new RAMJobStore(NullLoggerFactory.Instance, A.Fake<ISchedulerSignaler>(), clock);
            q.UseJobStore(memoryStore);
            return;
        }

        database ??= new SqliteTestDatabase("trigger-run-state");
        string connectionString = database.ConnectionString;
        q.UsePersistentStore(store =>
        {
            store.UseSqlite(SqliteFactory.Instance, connectionString);
            store.ProvisionSchema();
        });
    }

    private static ValueTask<IScheduler> SchedulerOf(ServiceProvider provider)
    {
        return provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
    }

    private IJobStore StoreOf(ServiceProvider provider)
    {
        return memoryStore ?? provider.GetRequiredService<IJobStore>();
    }

    private ValueTask<List<IOperableTrigger>> AcquireDue(IJobStore store)
    {
        return store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = clock.GetUtcNow().AddMinutes(1),
            MaxCount = 1,
            TimeWindow = TimeSpan.Zero,
        });
    }

    private async Task<(IOperableTrigger Firing, TriggerFiredBundle Bundle)> Fire(IJobStore store)
    {
        return await Fire(store, await AcquireDue(store));
    }

    /// <remarks>
    /// The firing is the bundle's trigger, as the run shell's is: the persistent store advances a copy of
    /// its own, and leaves the acquired one where it was.
    /// </remarks>
    private static async Task<(IOperableTrigger Firing, TriggerFiredBundle Bundle)> Fire(IJobStore store, List<IOperableTrigger> acquired)
    {
        acquired.Should().ContainSingle("the declared trigger is due and is the only trigger")
            .Which.Key.Should().Be(triggerKey);

        List<TriggerFiredResult> fired = await store.TriggersFired(acquired);
        TriggerFiredBundle bundle = fired.Should().ContainSingle().Which.TriggerFiredBundle!;
        bundle.Should().NotBeNull("the trigger is due and nothing stands in its way");

        return (bundle.Trigger, bundle);
    }

    /// <summary>
    /// What the run shell does with a firing that has run: asks the trigger what comes next, and tells the
    /// store.
    /// </summary>
    private static async Task Complete(IJobStore store, IOperableTrigger firing, TriggerFiredBundle bundle, JobExecutionException? failure = null)
    {
        SchedulerInstruction instruction = firing.ExecutionComplete(A.Fake<IJobExecutionContext>(), failure);
        await store.TriggeredJobComplete(firing, bundle.JobDetail, instruction);
    }

    private async Task FireAndComplete(IJobStore store, JobExecutionException? failure = null)
    {
        (IOperableTrigger firing, TriggerFiredBundle bundle) = await Fire(store);
        await Complete(store, firing, bundle, failure);
    }

    /// <summary>
    /// Fires the trigger at each of its fire times until it has none left, and says how many times it fired.
    /// </summary>
    private async Task<int> FireUntilDone(IJobStore store)
    {
        const int Ceiling = 20;

        for (int fired = 0; fired < Ceiling; fired++)
        {
            List<IOperableTrigger> acquired = await AcquireDue(store);
            if (acquired.Count == 0)
            {
                return fired;
            }

            (IOperableTrigger firing, TriggerFiredBundle bundle) = await Fire(store, acquired);
            await Complete(store, firing, bundle);
            clock.Advance(interval);
        }

        return Ceiling;
    }

    public sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }
}
