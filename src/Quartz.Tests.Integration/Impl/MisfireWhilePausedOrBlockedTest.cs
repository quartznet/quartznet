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

using Quartz.Extensibility;

namespace Quartz.Tests.Integration.Impl;

/// <summary>
/// A trigger that is not eligible to fire cannot misfire either. Pausing one, and blocking one behind a
/// job that forbids concurrent execution, are the two ways that happens — and both end with the debt
/// being settled later rather than forgiven.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class MisfireWhilePausedOrBlockedTest : MisfireThroughAStoreTestBase
{
    /// <summary>
    /// One "fire now" instruction and one "reschedule to the next slot" instruction, since resuming a
    /// paused trigger is where the difference between them is most visible: one owes an immediate
    /// firing and the other has written the missed one off.
    /// </summary>
    public static IEnumerable<MisfireMatrixCase> Cases()
    {
        yield return MisfireMatrixCases.Cell(MisfireTriggerShape.Cron, nameof(CronTriggerMisfireInstruction.FireAndProceed));
        yield return MisfireMatrixCases.Cell(MisfireTriggerShape.Cron, nameof(CronTriggerMisfireInstruction.DoNothing));
        yield return MisfireMatrixCases.Cell(MisfireTriggerShape.SimpleOneShot, nameof(SimpleTriggerMisfireInstruction.FireNow));
    }

    /// <summary>
    /// A paused trigger accrues no misfire, however far past its fire time the clock goes: a pause is a
    /// decision not to fire, so there is nothing to be late for. Resuming it is what settles the debt,
    /// and it settles it through the trigger's own misfire policy rather than by firing whatever was
    /// missed.
    /// </summary>
    [TestCaseSource(nameof(Cases))]
    public async Task APausedTriggerMisfiresOnResumeAndNotBefore(MisfireMatrixCase testCase)
    {
        DateTimeOffset anchor = Anchor();
        DateTimeOffset scheduled = anchor - HalfPeriod;

        foreach (MisfireStoreUnderTest store in await BothStores(anchor))
        {
            TriggerKey triggerKey = new("paused-" + Guid.NewGuid().ToString("N"), Group);
            JobKey jobKey = new(triggerKey.Name, Group);

            IOperableTrigger trigger = (IOperableTrigger) testCase.Trigger(anchor, store.Clock)
                .WithIdentity(triggerKey)
                .ForJob(jobKey)
                .Build();

            await Store(store, Job(jobKey), trigger, scheduled);

            (await store.Store.PauseTrigger(triggerKey)).Should().BeTrue(
                "{0} has to actually pause '{1}', or the rest of this test is about a waiting trigger", store.Name, testCase);

            store.Clock.Advance(HalfPeriod);

            await store.Sweep(scheduled - TimeSpan.FromTicks(1));

            (await store.Store.GetTriggerState(triggerKey)).Should().Be(TriggerState.Paused,
                "{0} must leave '{1}' paused: a misfire pass that resumed a trigger would start it firing "
                + "behind the operator's back", store.Name, testCase);
            (await store.Store.GetTrigger(triggerKey)).NextFireTimeUtc.Should().Be(scheduled,
                "{0} must leave a paused '{1}' on the fire time it was paused with, so that resuming it has "
                + "the missed firing to apply a policy to", store.Name, testCase);

            // The ADO store applies a resumed trigger's misfire policy only while it believes the
            // scheduler is running; the in-memory store has no such condition. See MarkSchedulerRunning.
            if (store is SqliteMisfireStore ado)
            {
                await ado.MarkSchedulerRunning();
            }

            IOperableTrigger detached = (IOperableTrigger) testCase.Trigger(anchor, store.Clock)
                .WithIdentity(triggerKey)
                .ForJob(jobKey)
                .Build();
            detached.ComputeFirstFireTimeUtc(null);
            detached.NextFireTimeUtc = scheduled;

            MisfireExpectation expected = MisfireExpectation.From(detached, calendar: null, store.Clock);

            (await store.Store.ResumeTrigger(triggerKey)).Should().BeTrue(
                "{0} has to report that it resumed '{1}'", store.Name, testCase);

            TriggerState state = await store.Store.GetTriggerState(triggerKey);
            IOperableTrigger readBack = await store.Store.GetTrigger(triggerKey);

            expected.AssertAgainst(store.Name, testCase + " on resume", state, readBack.NextFireTimeUtc);
        }
    }

    #region Blocked behind a job that forbids concurrent execution

    /// <summary>The trigger shape and instruction the blocked cases use.</summary>
    private static MisfireMatrixCase BlockedCase =>
        MisfireMatrixCases.Cell(MisfireTriggerShape.Cron, nameof(CronTriggerMisfireInstruction.DoNothing));

    /// <summary>
    /// The in-memory store's half of the blocked case. A trigger blocked behind a running execution of a
    /// <see cref="DisallowConcurrentExecutionAttribute" /> job is out of the acquisition set entirely, so
    /// no pass can reach it however late it gets. Completing the execution puts it back with the missed
    /// fire time still on it, and the next acquisition is what applies its policy.
    /// </summary>
    /// <remarks>
    /// <c>RAMJobStore.TriggeredJobComplete</c> moves a blocked trigger to <c>Waiting</c> and adds it back
    /// to <c>timeTriggers</c>, and does nothing else — <c>ApplyMisfireNoLock</c> is reached from
    /// acquisition and from resume, and the unblocking path is neither. So the debt is settled by the
    /// very next acquisition, which is also the first thing that could have fired the trigger.
    /// </remarks>
    [Test]
    public async Task TheInMemoryStoreLeavesAnUnblockedTriggersMisfireToTheNextAcquisition()
    {
        DateTimeOffset anchor = Anchor();
        DateTimeOffset scheduled = anchor - HalfPeriod;

        MisfireStoreUnderTest store = await InMemoryStore(anchor);
        BlockedTrigger blocked = await GivenATriggerBlockedPastItsThreshold(store, anchor);

        await store.Store.TriggeredJobComplete(blocked.Firing, blocked.Job, SchedulerInstruction.NoInstruction);

        (await store.Store.GetTriggerState(blocked.Key)).Should().Be(TriggerState.Normal,
            "completing the execution is what lets the blocked siblings go");
        (await store.Store.GetTrigger(blocked.Key)).NextFireTimeUtc.Should().Be(scheduled,
            "RAMJobStore's unblocking returns the trigger to the acquisition set and nothing more, so the "
            + "missed firing is still on the books at this point");

        IOperableTrigger detached = Detached(anchor, store.Clock, blocked.Key, blocked.Job.Key, scheduled);

        MisfireExpectation expected = MisfireExpectation.From(detached, calendar: null, store.Clock);

        await store.Sweep(scheduled - TimeSpan.FromTicks(1));

        expected.AssertAgainst(
            store.Name,
            BlockedCase + " unblocked",
            await store.Store.GetTriggerState(blocked.Key),
            (await store.Store.GetTrigger(blocked.Key)).NextFireTimeUtc);
    }

    /// <summary>
    /// The ADO store's half, which settles the debt a step earlier: <c>TriggeredJobComplete</c> unblocks
    /// the job's triggers and then calls <c>RecoverUnblockedMisfires</c>, so the policy has already run
    /// by the time the caller can look.
    /// </summary>
    [Test]
    public async Task TheAdoStoreAppliesAnUnblockedTriggersMisfireAsItUnblocksIt()
    {
        DateTimeOffset anchor = Anchor();
        DateTimeOffset scheduled = anchor - HalfPeriod;

        MisfireStoreUnderTest store = await SqliteStore(anchor);
        BlockedTrigger blocked = await GivenATriggerBlockedPastItsThreshold(store, anchor);

        IOperableTrigger detached = Detached(anchor, store.Clock, blocked.Key, blocked.Job.Key, scheduled);

        MisfireExpectation expected = MisfireExpectation.From(detached, calendar: null, store.Clock);

        await store.Store.TriggeredJobComplete(blocked.Firing, blocked.Job, SchedulerInstruction.NoInstruction);

        expected.AssertAgainst(
            store.Name,
            BlockedCase + " unblocked",
            await store.Store.GetTriggerState(blocked.Key),
            (await store.Store.GetTrigger(blocked.Key)).NextFireTimeUtc);
    }

    /// <summary>
    /// The two stores settle an unblocked trigger's misfire at different moments, which is a finding
    /// rather than something for these tests to smooth over.
    /// </summary>
    /// <remarks>
    /// Reported rather than asserted, per the rule that a matrix says what 4.0 does and does not change
    /// it. The window between the two is real but narrow — the in-memory store's next acquisition — and
    /// a caller that reads the trigger inside it sees a fire time in the past on one store and a
    /// recomputed one on the other.
    /// </remarks>
    [Test]
    public void BothStoresAgreeOnWhenAnUnblockedTriggerMisfires()
    {
        Assert.Inconclusive(
            "The two stores apply an unblocked trigger's misfire policy at different moments. "
            + "Observed: AdoJobStoreBase.TriggeredJobComplete unblocks the job's triggers and then calls "
            + "RecoverUnblockedMisfires, so the policy has run by the time TriggeredJobComplete returns; "
            + "RAMJobStore.TriggeredJobComplete only moves the trigger from Blocked to Waiting and adds it "
            + "back to timeTriggers, leaving the policy to the next AcquireNextTriggers. Expected: the same "
            + "moment on both, since a caller reading the trigger between the two gets a different answer "
            + "depending on which store it is talking to. The two tests above pin each store's actual "
            + "behaviour, so this reports the gap rather than asserting it away.");
    }

    /// <summary>
    /// Gets a store into the state the blocked cases are about: one trigger of a
    /// <see cref="DisallowConcurrentExecutionAttribute" /> job firing, a second trigger of the same job
    /// blocked behind it, the clock moved past the threshold, and one misfire pass already run to show
    /// that it could not reach the blocked trigger.
    /// </summary>
    private async Task<BlockedTrigger> GivenATriggerBlockedPastItsThreshold(MisfireStoreUnderTest store, DateTimeOffset anchor)
    {
        DateTimeOffset scheduled = anchor - HalfPeriod;

        JobKey jobKey = new("blocking-" + Guid.NewGuid().ToString("N"), Group);
        IJobDetail job = JobBuilder.Create<NonConcurrentMisfireTestJob>().WithIdentity(jobKey).Build();

        // Due exactly on the store's clock, so the trigger that does the blocking is not itself late.
        IOperableTrigger running = (IOperableTrigger) TriggerBuilder.Create(store.Clock)
            .WithIdentity("running-" + jobKey.Name, Group)
            .ForJob(jobKey)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromDays(1)).RepeatForever())
            .Build();

        await Store(store, job, running, scheduled);

        List<IOperableTrigger> acquired = await store.Store.AcquireNextTriggers(
            new TriggerAcquisitionRequest { NoLaterThan = scheduled, MaxCount = 1 });

        acquired.Should().ContainSingle(
            "{0} has to hand over the trigger that does the blocking before it can fire", store.Name);

        // Stored after the acquisition, so it is the fan-out in TriggersFired that blocks it rather than
        // the store having found the job already blocked when the trigger was added.
        TriggerKey blockedKey = new("blocked-" + jobKey.Name, Group);
        IOperableTrigger blocked = (IOperableTrigger) BlockedCase.Trigger(anchor, store.Clock)
            .WithIdentity(blockedKey)
            .ForJob(jobKey)
            .Build();

        await StoreTrigger(store, blocked, scheduled);

        List<TriggerFiredResult> fired = await store.Store.TriggersFired(acquired);

        fired.Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull(
            "the blocking execution has to actually start on {0}, or nothing is blocked", store.Name);

        (await store.Store.GetTriggerState(blockedKey)).Should().Be(TriggerState.Blocked,
            "firing one trigger of a job that forbids concurrent execution blocks the rest of them");

        store.Clock.Advance(HalfPeriod);

        await store.Sweep(scheduled - TimeSpan.FromTicks(1));

        (await store.Store.GetTriggerState(blockedKey)).Should().Be(TriggerState.Blocked,
            "{0} does not sweep a blocked trigger: a misfire pass looks at what is waiting", store.Name);
        (await store.Store.GetTrigger(blockedKey)).NextFireTimeUtc.Should().Be(scheduled,
            "the missed firing is still owed while the trigger is blocked");

        return new BlockedTrigger(blockedKey, job, acquired[0]);
    }

    private static IOperableTrigger Detached(DateTimeOffset anchor, TimeProvider clock, TriggerKey triggerKey, JobKey jobKey, DateTimeOffset scheduled)
    {
        IOperableTrigger detached = (IOperableTrigger) BlockedCase.Trigger(anchor, clock)
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .Build();

        detached.ComputeFirstFireTimeUtc(null);
        detached.NextFireTimeUtc = scheduled;

        return detached;
    }

    /// <summary>The blocked trigger, the job blocking it, and the firing that has to complete to let go.</summary>
    private sealed record BlockedTrigger(TriggerKey Key, IJobDetail Job, IOperableTrigger Firing);

    #endregion
}
