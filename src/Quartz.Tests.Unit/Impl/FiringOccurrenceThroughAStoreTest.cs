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

using FakeItEasy;

using Quartz.Core;
using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.Triggers;

using Quartz.Tests.Impl;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// What <see cref="JobExecutionContextOccurrenceExtensions.GetOccurrence" /> answers for the firings a store
/// hands back: each origin's retry and misfire catch-up, on the in-memory store and on a real SQLite one
/// (#4246).
/// </summary>
/// <remarks>
/// <para>
/// A retry fires the stored trigger again, and a catch-up fires it late, so the manual and backfill marks on
/// the trigger's data survive both only if the store keeps that data across the write and the read back.
/// That is what these assert, beside the occurrence each one serves.
/// </para>
/// <para>
/// Recovery is the persistent store's alone: the in-memory one has nothing to recover from. It is in
/// <c>FiringOccurrenceRecoverySqliteTest</c>.
/// </para>
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class FiringOccurrenceThroughAStoreTest : MisfireThroughAStoreTestBase
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(5);

    /// <summary>The slot a backfill trigger stands in for: a day before anything here fires.</summary>
    private static DateTimeOffset SlotBefore(DateTimeOffset now) => new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero).AddDays(-1);

    [TestCase(FiringOrigin.Schedule)]
    [TestCase(FiringOrigin.Manual)]
    [TestCase(FiringOrigin.Backfill)]
    public async Task EveryRetryServesTheSameOccurrenceAndKeepsItsOrigin(FiringOrigin origin)
    {
        foreach (MisfireStoreUnderTest store in await BothStores(Anchor()))
        {
            DateTimeOffset now = store.Clock.GetUtcNow();
            (IJobDetail job, IOperableTrigger trigger, DateTimeOffset expected) = await Given(store, origin, now, RetryPolicy.Fixed(2, RetryDelay));

            TriggerFiredBundle first = await Fire(store, trigger.Key);
            FiringOccurrence firstOccurrence = OccurrenceOf(first);

            SchedulerInstruction instruction = first.Trigger.ExecutionComplete(A.Fake<IJobExecutionContext>(), Failure());
            instruction.Should().Be(SchedulerInstruction.RetryTrigger, "{0}: the premise: a failure with attempts left schedules a retry", store.Name);
            await store.Store.TriggeredJobComplete(first.Trigger, job, instruction);

            store.Clock.Advance(RetryDelay);
            FiringOccurrence retried = OccurrenceOf(await Fire(store, trigger.Key));

            firstOccurrence.Should().Be(new FiringOccurrence(expected, origin), "{0}: the first attempt", store.Name);
            retried.Should().Be(firstOccurrence with { RetryAttempt = 1 },
                "{0}: the retry fires the stored trigger again, its data with it, for the same occurrence", store.Name);
        }
    }

    [TestCase(FiringOrigin.Schedule)]
    [TestCase(FiringOrigin.Manual)]
    [TestCase(FiringOrigin.Backfill)]
    public async Task AMisfireCatchUpServesTheOccurrenceItMissedAndKeepsItsOrigin(FiringOrigin origin)
    {
        foreach (MisfireStoreUnderTest store in await BothStores(Anchor()))
        {
            DateTimeOffset missed = store.Clock.GetUtcNow();
            (_, IOperableTrigger trigger, DateTimeOffset expected) = await Given(store, origin, missed, retryPolicy: null);

            // Half a day late, which is far past the threshold: the trigger's misfire instruction fires it now.
            store.Clock.Advance(HalfPeriod);
            await store.Sweep(store.Clock.GetUtcNow().AddMinutes(-1));

            TriggerFiredBundle late = await Fire(store, trigger.Key);

            late.FireTimeUtc.Should().Be(missed + HalfPeriod, "{0}: the premise: the catch-up runs now", store.Name);
            OccurrenceOf(late).Should().Be(new FiringOccurrence(expected, origin),
                "{0}: a catch-up serves the occurrence it missed; how late it ran is FireTimeUtc's to say", store.Name);
        }
    }

    /// <summary>
    /// Stores a trigger of the given origin due at <paramref name="now" />, and says which occurrence its
    /// firing serves.
    /// </summary>
    private static async ValueTask<(IJobDetail Job, IOperableTrigger Trigger, DateTimeOffset Expected)> Given(
        MisfireStoreUnderTest store,
        FiringOrigin origin,
        DateTimeOffset now,
        RetryPolicy retryPolicy)
    {
        string id = Guid.NewGuid().ToString("N");
        JobKey jobKey = new("job-" + id, Group);
        IJobDetail job = JobBuilder.Create<MisfireTestJob>().WithIdentity(jobKey).StoreDurably().Build();

        IOperableTrigger trigger;
        DateTimeOffset expected = now;
        switch (origin)
        {
            case FiringOrigin.Schedule:
                // Hourly, firing now on a misfire: the catch-up a recurring schedule makes. The next
                // occurrence is an hour on, which leaves room for a retry five minutes after this one.
                trigger = (IOperableTrigger) TriggerBuilder.Create(store.Clock)
                    .WithIdentity("scheduled-" + id, Group)
                    .ForJob(jobKey)
                    .StartAt(now)
                    .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromHours(1)).RepeatForever().WithMisfireInstruction(SimpleTriggerMisfireInstruction.FireNow))
                    .WithRetryPolicy(retryPolicy)
                    .Build();
                break;

            case FiringOrigin.Manual:
                // The trigger TriggerJob stores, built by the code that builds it.
                SimpleTriggerImpl manual = ManualFiring.CreateTrigger(jobKey, data: null, executionGroup: null, store.Clock);
                manual.RetryPolicy = retryPolicy;
                trigger = manual;
                break;

            default:
                // What a backfill schedules for a slot: the slot under its key, fired now, FireNow on a misfire.
                DateTimeOffset slot = SlotBefore(now);
                JobDataMap data = new();
                data.PutAsString(SchedulerConstants.BackfillOriginalFireTime, slot);
                trigger = (IOperableTrigger) TriggerBuilder.Create(store.Clock)
                    .WithIdentity($"scheduled-{id}@{slot:yyyy-MM-ddTHH:mm:ssZ}", SchedulerConstants.BackfillGroupPrefix + Group)
                    .ForJob(jobKey)
                    .UsingJobData(data)
                    .WithRetryPolicy(retryPolicy)
                    .StartAt(now)
                    .WithSimpleSchedule(schedule => schedule.WithMisfireInstruction(SimpleTriggerMisfireInstruction.FireNow))
                    .Build();
                expected = slot;
                break;
        }

        await Store(store, job, trigger, now);
        return (job, trigger, expected);
    }

    /// <summary>
    /// Acquires and fires the trigger under test, and returns what the scheduler would build the context from.
    /// </summary>
    private static async ValueTask<TriggerFiredBundle> Fire(MisfireStoreUnderTest store, TriggerKey key)
    {
        List<IOperableTrigger> acquired = await store.Store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = store.Clock.GetUtcNow().AddDays(400),
            MaxCount = 1,
        });

        acquired.Select(x => x.Key).Should().Equal([key], "{0} must hand back the trigger under test as the next one due", store.Name);

        List<TriggerFiredResult> results = await store.Store.TriggersFired(acquired);
        TriggerFiredBundle bundle = results.Should().ContainSingle().Which.TriggerFiredBundle;
        bundle.Should().NotBeNull("{0} must commit the firing", store.Name);
        return bundle;
    }

    private static FiringOccurrence OccurrenceOf(TriggerFiredBundle bundle)
    {
        using JobExecutionContextImpl context = new(A.Fake<IScheduler>(), bundle, new MisfireTestJob());
        return context.GetOccurrence();
    }

    private static JobExecutionException Failure() => new(new InvalidOperationException("boom"));
}
