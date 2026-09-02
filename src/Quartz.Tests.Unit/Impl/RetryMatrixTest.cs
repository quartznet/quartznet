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

using Quartz.Extensibility;
using Quartz.Impl.Triggers;

using Quartz.Tests.Impl;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// What a store does when a trigger's job fails and the trigger carries a retry policy: every trigger
/// shape, on the in-memory store and on a real SQLite one, both asserted against the same expectation.
/// </summary>
/// <remarks>
/// <para>
/// The retry decision belongs to the trigger — <c>ExecutionComplete</c> makes it and
/// <c>RetryFired</c> unwinds it — and a store's contribution is to record what came out and hand it
/// back at the next acquisition. So each row here computes what a detached copy of the stored trigger
/// says the schedule should be, and then asserts that both stores arrived at exactly that; the stores
/// are compared with each other by being compared with one expectation rather than two.
/// </para>
/// <para>
/// <c>RetryEngineTest</c> in the unit tests is the other half of this: it is the decision itself,
/// against a trigger and a clock with no store at all. This one is about what the stores do with it.
/// </para>
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class RetryMatrixTest : MisfireThroughAStoreTestBase
{
    public static IEnumerable<RetryMatrixCase> Cases() => RetryMatrixCases.All();

    private static readonly TimeSpan RetryDelay = RetryMatrixCases.RetryDelay;
    private static readonly TimeSpan Period = RetryMatrixCases.Period;

    /// <summary>A failure the job asked nothing about, which is what a thrown exception becomes.</summary>
    private static JobExecutionException Failure() => new JobExecutionException(new InvalidOperationException("boom"));

    private static IJobExecutionContext Context() => A.Fake<IJobExecutionContext>();

    /// <summary>
    /// Where a store's clock actually stands when a test starts.
    /// </summary>
    /// <remarks>
    /// The shared fixture starts both clocks half a period <em>before</em> the anchor it is handed,
    /// because that is where a misfire matrix trigger's missed firing sits. A retry test's trigger is
    /// due now rather than overdue, so it reads the clock instead of assuming the anchor.
    /// </remarks>
    private static DateTimeOffset Now(MisfireStoreUnderTest store) => store.Clock.GetUtcNow();

    /// <summary>
    /// Guards the table itself: a trigger shape added to the enum without a row here would otherwise
    /// silently go untested against either store.
    /// </summary>
    [Test]
    public void EveryShapeHasACase()
    {
        Cases().Select(x => x.Shape).Should().BeEquivalentTo(Enum.GetValues<RetryTriggerShape>(),
            "the matrix is only a matrix while every trigger shape has a row");
    }

    /// <summary>
    /// Acquires and fires whichever trigger the store has due, and returns the copy the scheduler would
    /// have been handed.
    /// </summary>
    private static async ValueTask<IOperableTrigger> Fire(MisfireStoreUnderTest store, TriggerKey key)
    {
        List<IOperableTrigger> acquired = await store.Store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = store.Clock.GetUtcNow().AddDays(400),
            MaxCount = 1,
        });

        acquired.Select(x => x.Key).Should().Equal([key],
            "{0} must hand back the trigger under test as the next one due", store.Name);

        List<TriggerFiredResult> results = await store.Store.TriggersFired(acquired);
        TriggerFiredBundle bundle = results.Should().ContainSingle().Which.TriggerFiredBundle;
        bundle.Should().NotBeNull("{0} must commit the firing before it can be completed", store.Name);

        // The bundle's trigger, not the acquired one. The ADO store fires a clone and leaves the copy
        // the caller handed it untouched, so the acquired instance still carries its pre-fire schedule;
        // the run shell completes what the bundle carries, and so does this.
        return (IOperableTrigger) bundle.Trigger;
    }

    /// <summary>Completes a firing the way the run shell does: decide, then tell the store.</summary>
    private static async ValueTask<SchedulerInstruction> Complete(
        MisfireStoreUnderTest store,
        IOperableTrigger firing,
        IJobDetail job,
        JobExecutionException result)
    {
        SchedulerInstruction instruction = firing.ExecutionComplete(Context(), result);
        await store.Store.TriggeredJobComplete(firing, job, instruction);
        return instruction;
    }

    /// <summary>
    /// Stores the row's trigger due at the now, and returns it with its job.
    /// </summary>
    private static async ValueTask<(IJobDetail Job, IOperableTrigger Trigger)> Given(
        MisfireStoreUnderTest store,
        RetryMatrixCase testCase,
        DateTimeOffset now,
        string name)
    {
        TriggerKey triggerKey = new(name + "-" + Guid.NewGuid().ToString("N"), Group);
        JobKey jobKey = new(triggerKey.Name, Group);

        IJobDetail job = Job(jobKey);
        IOperableTrigger trigger = (IOperableTrigger) testCase.Trigger(now, store.Clock)
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .Build();

        await Store(store, job, trigger, now);
        return (job, trigger);
    }

    /// <summary>
    /// What the schedule says the occurrence after the failing one is: a detached copy, fired once, is
    /// the trigger's own arithmetic with no store involved.
    /// </summary>
    private static DateTimeOffset? RegularNextAfterOneFire(RetryMatrixCase testCase, DateTimeOffset now, TimeProvider clock)
    {
        IOperableTrigger detached = (IOperableTrigger) testCase.Trigger(now, clock)
            .WithIdentity("detached", Group)
            .ForJob("detached", Group)
            .Build();

        detached.ComputeFirstFireTimeUtc(null);
        detached.NextFireTimeUtc = now;
        detached.Triggered(null);
        return detached.NextFireTimeUtc;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // The matrix
    //////////////////////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// The matrix. One trigger, fired at the now; its job fails; the retry is stored; the clock is
    /// moved to the retry instant; the retry fires and succeeds. What must come out at the end is the
    /// schedule the trigger would have had if nothing had failed — with no counter moved, and no
    /// attempt left behind.
    /// </summary>
    [TestCaseSource(nameof(Cases))]
    public async Task ARetryRunsAndLeavesTheScheduleExactlyWhereItWas(RetryMatrixCase testCase)
    {
        DateTimeOffset anchor = Anchor();

        foreach (MisfireStoreUnderTest store in await BothStores(anchor))
        {
            DateTimeOffset now = Now(store);

            (IJobDetail job, IOperableTrigger trigger) = await Given(store, testCase, now, "matrix");

            DateTimeOffset? regularNext = RegularNextAfterOneFire(testCase, now, store.Clock);

            IOperableTrigger firing = await Fire(store, trigger.Key);
            firing.RetryAttempt.Should().Be(0, "{0}: the first firing is the scheduled occurrence", store.Name);

            int? countedAfterOneFire = testCase.TimesTriggered?.Invoke(await store.Store.GetTrigger(trigger.Key));

            (await Complete(store, firing, job, Failure())).Should().Be(SchedulerInstruction.RetryTrigger,
                "{0}: a failure with attempts left schedules a retry", store.Name);

            IOperableTrigger stored = await store.Store.GetTrigger(trigger.Key);
            stored.NextFireTimeUtc.Should().Be(store.Clock.GetUtcNow() + RetryDelay,
                "{0}: the stored trigger waits for the retry instant", store.Name);
            stored.RetryAttempt.Should().Be(1, "{0}: the store records which attempt is next", store.Name);
            (await store.Store.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Normal,
                "{0}: a failed occurrence with attempts left is not a broken trigger", store.Name);

            // The retry comes round.
            store.Clock.Advance(RetryDelay);

            IOperableTrigger retryFiring = await Fire(store, trigger.Key);
            retryFiring.RetryAttempt.Should().Be(1,
                "{0}: the firing carries the attempt, which is what the execution context reports", store.Name);

            testCase.TimesTriggered?.Invoke(await store.Store.GetTrigger(trigger.Key)).Should().Be(countedAfterOneFire,
                "{0}: a retry is a second go at an occurrence that has already been counted, so it burns no "
                + "repeat count and no recurrence slot", store.Name);

            (await Complete(store, retryFiring, job, result: null)).Should().Be(
                testCase.HasFurtherOccurrences ? SchedulerInstruction.NoInstruction : SchedulerInstruction.DeleteTrigger,
                "{0}: a retry that worked leaves the trigger on its ordinary schedule", store.Name);

            if (testCase.HasFurtherOccurrences)
            {
                IOperableTrigger afterRetry = await store.Store.GetTrigger(trigger.Key);
                afterRetry.NextFireTimeUtc.Should().Be(regularNext,
                    "{0}: the occurrence the schedule called for is exactly where it was before anything failed",
                    store.Name);
                afterRetry.RetryAttempt.Should().Be(0,
                    "{0}: the occurrence is done with, so the next failure starts from the first wait again",
                    store.Name);
            }
            else
            {
                (await store.Store.GetTrigger(trigger.Key)).Should().BeNull(
                    "{0}: a one-shot trigger whose retry succeeded has nothing left to fire", store.Name);
            }
        }
    }

    /// <summary>
    /// A one-shot trigger may still fire again while it waits to retry, which is what keeps the run
    /// shell from announcing it finalized a firing too early.
    /// </summary>
    [Test]
    public async Task AOneShotTriggerSurvivesUntilItsRetriesAreSpent()
    {
        DateTimeOffset anchor = Anchor();
        RetryMatrixCase testCase = RetryMatrixCases.Row(RetryTriggerShape.SimpleOneShot);

        foreach (MisfireStoreUnderTest store in await BothStores(anchor))
        {
            DateTimeOffset now = Now(store);

            (IJobDetail job, IOperableTrigger trigger) = await Given(store, testCase, now, "one-shot");

            IOperableTrigger firing = await Fire(store, trigger.Key);
            await Complete(store, firing, job, Failure());

            firing.MayFireAgain.Should().BeTrue(
                "{0}: the run shell announces TriggerFinalized off MayFireAgain, and a trigger about to retry "
                + "is not finished", store.Name);
            (await store.Store.GetTrigger(trigger.Key)).Should().NotBeNull(
                "{0}: the trigger is waiting for its retry, not gone", store.Name);

            // Both attempts fail; only then is it really finished.
            store.Clock.Advance(RetryDelay);
            IOperableTrigger first = await Fire(store, trigger.Key);
            (await Complete(store, first, job, Failure())).Should().Be(SchedulerInstruction.RetryTrigger);

            store.Clock.Advance(RetryDelay);
            IOperableTrigger second = await Fire(store, trigger.Key);
            second.RetryAttempt.Should().Be(2, "{0}: the second retry is attempt two", store.Name);

            (await Complete(store, second, job, Failure())).Should().Be(SchedulerInstruction.DeleteTrigger,
                "{0}: the attempts are spent and there is no occurrence left, so the trigger is finished — "
                + "and finished is not Error", store.Name);
            (await store.Store.GetTrigger(trigger.Key)).Should().BeNull();
        }
    }

    /// <summary>
    /// Exhaustion on a trigger that does have a schedule left: back to the ordinary occurrence, attempt
    /// cleared, and emphatically not <see cref="TriggerState.Error" />.
    /// </summary>
    [Test]
    public async Task SpentAttemptsGoBackToTheScheduleAndNotToError()
    {
        DateTimeOffset anchor = Anchor();
        RetryMatrixCase testCase = RetryMatrixCases.Row(RetryTriggerShape.Cron);

        foreach (MisfireStoreUnderTest store in await BothStores(anchor))
        {
            DateTimeOffset now = Now(store);

            (IJobDetail job, IOperableTrigger trigger) = await Given(store, testCase, now, "exhausted");
            DateTimeOffset? regularNext = RegularNextAfterOneFire(testCase, now, store.Clock);

            await Complete(store, await Fire(store, trigger.Key), job, Failure());
            store.Clock.Advance(RetryDelay);
            await Complete(store, await Fire(store, trigger.Key), job, Failure());
            store.Clock.Advance(RetryDelay);

            IOperableTrigger last = await Fire(store, trigger.Key);
            last.RetryAttempt.Should().Be(2);

            (await Complete(store, last, job, Failure())).Should().Be(SchedulerInstruction.NoInstruction,
                "{0}: one bad hour must not kill a cron trigger", store.Name);

            (await store.Store.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Normal,
                "{0}: a trigger that has spent its attempts is waiting, not in error", store.Name);

            IOperableTrigger stored = await store.Store.GetTrigger(trigger.Key);
            stored.NextFireTimeUtc.Should().Be(regularNext, "{0}: back on the schedule it always had", store.Name);
            stored.RetryAttempt.Should().Be(0, "{0}: and counting from zero again", store.Name);
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // The supersede rule
    //////////////////////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// A retry never displaces the next scheduled occurrence. An hourly trigger whose policy waits
    /// ninety minutes is a policy that does nothing.
    /// </summary>
    [Test]
    public async Task ARetryThatWouldPassTheNextOccurrenceIsDropped()
    {
        DateTimeOffset anchor = Anchor();

        foreach (MisfireStoreUnderTest store in await BothStores(anchor))
        {
            DateTimeOffset now = Now(store);

            TriggerKey triggerKey = new("superseded-" + Guid.NewGuid().ToString("N"), Group);
            JobKey jobKey = new(triggerKey.Name, Group);
            IJobDetail job = Job(jobKey);

            IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create(store.Clock)
                .WithIdentity(triggerKey)
                .ForJob(jobKey)
                .StartAt(now)
                .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
                .WithRetryPolicy(RetryPolicy.Fixed(3, TimeSpan.FromMinutes(90)))
                .Build();

            await Store(store, job, trigger, now);

            IOperableTrigger firing = await Fire(store, triggerKey);
            (await Complete(store, firing, job, Failure())).Should().Be(SchedulerInstruction.NoInstruction,
                "{0}: ninety minutes is past the next hourly occurrence, so the schedule wins", store.Name);

            IOperableTrigger stored = await store.Store.GetTrigger(triggerKey);
            stored.NextFireTimeUtc.Should().Be(now.AddHours(1),
                "{0}: the occurrence supersedes the retry rather than being pushed by it", store.Name);
            stored.RetryAttempt.Should().Be(0, "{0}: a dropped retry is not an attempt spent", store.Name);
        }
    }

    /// <summary>
    /// Where the supersede rule draws its line, to the tick. The margin is a whole second because
    /// <c>CalendarIntervalTriggerImpl.GetFireTimeAfter</c> and
    /// <c>DailyTimeIntervalTriggerImpl.GetFireTimeAfter</c> each add a second before searching, so a
    /// retry closer than that could not be told apart from the occurrence itself.
    /// </summary>
    public sealed record SupersedeEdgeCase(string Position, TimeSpan Delay, bool Retries)
    {
        public override string ToString() => Position;
    }

    public static IEnumerable<SupersedeEdgeCase> SupersedeEdgeCases()
    {
        TimeSpan hour = TimeSpan.FromHours(1);
        TimeSpan second = TimeSpan.FromSeconds(1);

        yield return new SupersedeEdgeCase("a tick inside the one-second margin", hour - second - TimeSpan.FromTicks(1), Retries: true);
        yield return new SupersedeEdgeCase("exactly one second short of the occurrence", hour - second, Retries: false);
        yield return new SupersedeEdgeCase("exactly on the occurrence", hour, Retries: false);
    }

    [TestCaseSource(nameof(SupersedeEdgeCases))]
    public async Task TheSupersedeMarginIsAWholeSecondWide(SupersedeEdgeCase testCase)
    {
        DateTimeOffset anchor = Anchor();

        foreach (MisfireStoreUnderTest store in await BothStores(anchor))
        {
            DateTimeOffset now = Now(store);

            TriggerKey triggerKey = new("edge-" + Guid.NewGuid().ToString("N"), Group);
            JobKey jobKey = new(triggerKey.Name, Group);
            IJobDetail job = Job(jobKey);

            IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create(store.Clock)
                .WithIdentity(triggerKey)
                .ForJob(jobKey)
                .StartAt(now)
                .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
                .WithRetryPolicy(RetryPolicy.Fixed(3, testCase.Delay))
                .Build();

            await Store(store, job, trigger, now);

            SchedulerInstruction instruction = await Complete(store, await Fire(store, triggerKey), job, Failure());

            instruction.Should().Be(testCase.Retries ? SchedulerInstruction.RetryTrigger : SchedulerInstruction.NoInstruction,
                "{0}: '{1}' decides whether there is room for a retry", store.Name, testCase.Position);
            (await store.Store.GetTrigger(triggerKey)).RetryAttempt.Should().Be(testCase.Retries ? 1 : 0,
                "{0}: '{1}'", store.Name, testCase.Position);
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Misfire, pause and resume
    //////////////////////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// A retry the scheduler never got to is a misfire like any other: the trigger's own instruction
    /// decides what happens, and the attempt is cleared because the occurrence it belonged to is gone.
    /// The two misfire families diverge on where the trigger ends up, so both are asserted.
    /// </summary>
    [TestCase(CronTriggerMisfireInstruction.FireAndProceed)]
    [TestCase(CronTriggerMisfireInstruction.DoNothing)]
    public async Task AMisfiredRetryTakesTheTriggersOwnInstructionAndClearsTheAttempt(CronTriggerMisfireInstruction instruction)
    {
        DateTimeOffset anchor = Anchor();

        foreach (MisfireStoreUnderTest store in await BothStores(anchor))
        {
            DateTimeOffset now = Now(store);

            TriggerKey triggerKey = new("misfired-retry-" + Guid.NewGuid().ToString("N"), Group);
            JobKey jobKey = new(triggerKey.Name, Group);
            IJobDetail job = Job(jobKey);

            IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create(store.Clock)
                .WithIdentity(triggerKey)
                .ForJob(jobKey)
                .StartAt(now - Period)
                .WithCronSchedule(DailyCronAt(now), x => x
                    .InTimeZone(TimeZoneInfo.Utc)
                    .WithMisfireInstruction(instruction))
                .WithRetryPolicy(RetryMatrixCases.Policy)
                .Build();

            await Store(store, job, trigger, now);

            await Complete(store, await Fire(store, triggerKey), job, Failure());

            DateTimeOffset retryInstant = (await store.Store.GetTrigger(triggerKey)).NextFireTimeUtc!.Value;
            (await store.Store.GetTrigger(triggerKey)).RetryAttempt.Should().Be(1);

            // Nobody fires the retry, and the clock runs far past the misfire threshold. The sweep
            // window stops short of the retry instant so the pass applies the misfire policy without
            // also acquiring the trigger and clouding what is being asserted.
            store.Clock.Advance(HalfPeriod);
            await store.Sweep(retryInstant - TimeSpan.FromTicks(1));

            IOperableTrigger stored = await store.Store.GetTrigger(triggerKey);

            stored.RetryAttempt.Should().Be(0,
                "{0}: misfire handling recomputed the trigger from its schedule, so the occurrence that was "
                + "waiting to be retried is gone and the attempt goes with it", store.Name);

            if (instruction == CronTriggerMisfireInstruction.FireAndProceed)
            {
                stored.NextFireTimeUtc.Should().Be(store.Clock.GetUtcNow(),
                    "{0}: FireOnceNow catches the missed firing up to now", store.Name);
            }
            else
            {
                stored.NextFireTimeUtc.Should().BeAfter(store.Clock.GetUtcNow(),
                    "{0}: DoNothing skips to the next scheduled slot", store.Name);
            }
        }
    }

    /// <summary>
    /// Pausing a trigger that is waiting to retry keeps the retry instant; resuming it long afterwards
    /// runs the misfire update, which clears the attempt.
    /// </summary>
    [Test]
    public async Task PausingDuringTheWaitKeepsTheRetryAndResumingPastItClearsTheAttempt()
    {
        DateTimeOffset anchor = Anchor();
        RetryMatrixCase testCase = RetryMatrixCases.Row(RetryTriggerShape.Cron);

        foreach (MisfireStoreUnderTest store in await BothStores(anchor))
        {
            DateTimeOffset now = Now(store);

            (IJobDetail job, IOperableTrigger trigger) = await Given(store, testCase, now, "paused");

            await Complete(store, await Fire(store, trigger.Key), job, Failure());

            DateTimeOffset retryAt = (await store.Store.GetTrigger(trigger.Key)).NextFireTimeUtc!.Value;

            await store.Store.PauseTrigger(trigger.Key);

            (await store.Store.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Paused);
            (await store.Store.GetTrigger(trigger.Key)).NextFireTimeUtc.Should().Be(retryAt,
                "{0}: pausing is a decision not to fire, not a decision to move the fire time", store.Name);
            (await store.Store.GetTrigger(trigger.Key)).RetryAttempt.Should().Be(1,
                "{0}: and not a decision about the occurrence's attempts either", store.Name);

            // Resumed long after the retry instant went by, which is the misfire path.
            store.Clock.Advance(HalfPeriod);

            // The ADO store applies a resumed trigger's misfire policy only while it believes the
            // scheduler is running; the in-memory store has no such condition. See MarkSchedulerRunning.
            if (store is SqliteMisfireStore ado)
            {
                await ado.MarkSchedulerRunning();
            }

            await store.Store.ResumeTrigger(trigger.Key);

            IOperableTrigger stored = await store.Store.GetTrigger(trigger.Key);
            stored.RetryAttempt.Should().Be(0,
                "{0}: resuming past the retry instant settles the debt through the misfire path, which starts "
                + "the trigger's next occurrence with no retries behind it", store.Name);
            (await store.Store.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Normal,
                "{0}: and leaves it waiting", store.Name);
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Concurrency and failover
    //////////////////////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// A <c>[DisallowConcurrentExecution]</c> job's other trigger is unblocked by the completion that
    /// schedules a retry, and the retry itself is left alone by the unblock sweep.
    /// </summary>
    [Test]
    public async Task TheSiblingUnblockRunsAndDoesNotEatTheRetry()
    {
        DateTimeOffset anchor = Anchor();

        foreach (MisfireStoreUnderTest store in await BothStores(anchor))
        {
            DateTimeOffset now = Now(store);

            TriggerKey firedKey = new("blocking-" + Guid.NewGuid().ToString("N"), Group);
            TriggerKey siblingKey = new("sibling-" + Guid.NewGuid().ToString("N"), Group);
            JobKey jobKey = new(firedKey.Name, Group);

            IJobDetail job = JobBuilder.Create<NonConcurrentMisfireTestJob>().WithIdentity(jobKey).Build();

            IOperableTrigger fired = (IOperableTrigger) TriggerBuilder.Create(store.Clock)
                .WithIdentity(firedKey).ForJob(jobKey)
                .StartAt(now)
                .WithSimpleSchedule(x => x.WithInterval(Period).RepeatForever())
                .WithRetryPolicy(RetryMatrixCases.Policy)
                .Build();

            IOperableTrigger sibling = (IOperableTrigger) TriggerBuilder.Create(store.Clock)
                .WithIdentity(siblingKey).ForJob(jobKey)
                .StartAt(now)
                .WithSimpleSchedule(x => x.WithInterval(Period).RepeatForever())
                .Build();

            await Store(store, job, fired, now);
            await StoreTrigger(store, sibling, now + TimeSpan.FromMinutes(1));

            IOperableTrigger firing = await Fire(store, firedKey);

            (await store.Store.GetTriggerState(siblingKey)).Should().Be(TriggerState.Blocked,
                "{0}: a job that disallows concurrent execution blocks its other triggers while it runs", store.Name);

            (await Complete(store, firing, job, Failure())).Should().Be(SchedulerInstruction.RetryTrigger);

            (await store.Store.GetTriggerState(siblingKey)).Should().Be(TriggerState.Normal,
                "{0}: the completion unblocks the job's other triggers whether it succeeded or scheduled a retry",
                store.Name);

            IOperableTrigger stored = await store.Store.GetTrigger(firedKey);
            stored.NextFireTimeUtc.Should().Be(store.Clock.GetUtcNow() + RetryDelay,
                "{0}: the unblock sweep must not recompute the trigger that is waiting to retry", store.Name);
            stored.RetryAttempt.Should().Be(1, "{0}: nor clear its attempt", store.Name);
        }
    }

    /// <summary>
    /// A retry mid-wait is an ordinary waiting row: nothing about it is owned by the node that scheduled
    /// it, so another node acquires it with the attempt intact. This is what makes a retry survive the
    /// node that decided on it going away.
    /// </summary>
    [Test]
    public async Task ARetryWaitingIsAPlainWaitingRowAnotherNodeCanAcquire()
    {
        DateTimeOffset anchor = Anchor();
        RetryMatrixCase testCase = RetryMatrixCases.Row(RetryTriggerShape.Cron);

        foreach (MisfireStoreUnderTest store in await BothStores(anchor))
        {
            DateTimeOffset now = Now(store);

            (IJobDetail job, IOperableTrigger trigger) = await Given(store, testCase, now, "failover");

            await Complete(store, await Fire(store, trigger.Key), job, Failure());

            (await store.Store.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Normal,
                "{0}: nothing in the row says a particular node is going to run the retry", store.Name);

            store.Clock.Advance(RetryDelay);

            // A second acquisition is what another node's scheduling loop would do.
            IOperableTrigger acquiredElsewhere = await Fire(store, trigger.Key);

            acquiredElsewhere.RetryAttempt.Should().Be(1,
                "{0}: whichever node picks the retry up knows how far through the policy the occurrence is, "
                + "or a failover restarts the retries from the beginning", store.Name);
            acquiredElsewhere.RetryPolicy.Should().Be(RetryMatrixCases.Policy,
                "{0}: and what the remaining waits are", store.Name);
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Directives that win over the policy
    //////////////////////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// The explicit directives a job can put on a <see cref="JobExecutionException" /> outrank the
    /// trigger's retry policy, and none of them spends an attempt.
    /// </summary>
    public sealed record DirectiveCase(string Name, Func<JobExecutionException> Result, SchedulerInstruction Expected)
    {
        public override string ToString() => Name;
    }

    public static IEnumerable<DirectiveCase> DirectiveCases()
    {
        yield return new DirectiveCase(
            "RefireImmediately",
            () => new JobExecutionException { RefireImmediately = true },
            SchedulerInstruction.ReExecuteJob);
        yield return new DirectiveCase(
            "UnscheduleFiringTrigger",
            () => new JobExecutionException { UnscheduleFiringTrigger = true },
            SchedulerInstruction.SetTriggerComplete);
        yield return new DirectiveCase(
            "UnscheduleAllTriggers",
            () => new JobExecutionException { UnscheduleAllTriggers = true },
            SchedulerInstruction.SetAllJobTriggersComplete);
    }

    [TestCaseSource(nameof(DirectiveCases))]
    public async Task AnExplicitDirectiveWinsOverTheRetryPolicy(DirectiveCase testCase)
    {
        DateTimeOffset anchor = Anchor();
        RetryMatrixCase row = RetryMatrixCases.Row(RetryTriggerShape.Cron);

        foreach (MisfireStoreUnderTest store in await BothStores(anchor))
        {
            DateTimeOffset now = Now(store);

            (IJobDetail job, IOperableTrigger trigger) = await Given(store, row, now, "directive");

            IOperableTrigger firing = await Fire(store, trigger.Key);
            SchedulerInstruction instruction = await Complete(store, firing, job, testCase.Result());

            instruction.Should().Be(testCase.Expected,
                "{0}: '{1}' is a decision the job made, and it outranks the trigger's policy", store.Name, testCase.Name);
            firing.RetryAttempt.Should().Be(0,
                "{0}: '{1}' is not an attempt at the policy", store.Name, testCase.Name);
        }
    }

    /// <summary>
    /// A cancellation on the scheduler's own token leaves no <see cref="JobExecutionException" /> behind
    /// at all, which is how shutdown and interrupt say "this was not the job failing".
    /// </summary>
    [Test]
    public async Task ACancelledJobIsNotRetried()
    {
        DateTimeOffset anchor = Anchor();
        RetryMatrixCase testCase = RetryMatrixCases.Row(RetryTriggerShape.Cron);

        foreach (MisfireStoreUnderTest store in await BothStores(anchor))
        {
            DateTimeOffset now = Now(store);

            (IJobDetail job, IOperableTrigger trigger) = await Given(store, testCase, now, "cancelled");
            DateTimeOffset? regularNext = RegularNextAfterOneFire(testCase, now, store.Clock);

            (await Complete(store, await Fire(store, trigger.Key), job, result: null))
                .Should().Be(SchedulerInstruction.NoInstruction);

            IOperableTrigger stored = await store.Store.GetTrigger(trigger.Key);
            stored.NextFireTimeUtc.Should().Be(regularNext,
                "{0}: shutdown and interrupt are operator decisions, not failures to retry", store.Name);
            stored.RetryAttempt.Should().Be(0, store.Name);
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Daylight saving time
    //////////////////////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// A retry is an absolute instant a few minutes out, so it is unaffected by a zone's clocks moving —
    /// but the occurrence it must not displace is not, and the two are compared. Asserted on the two
    /// families whose arithmetic is done in the trigger's own zone.
    /// </summary>
    [TestCase(RetryTriggerShape.CalendarInterval)]
    [TestCase(RetryTriggerShape.DailyTimeInterval)]
    public async Task ARetryAcrossASpringForwardKeepsTheOccurrenceItWouldHavePassed(RetryTriggerShape shape)
    {
        // 02:00 on 30 March 2025 does not exist in Helsinki: the clocks go straight to 03:00.
        TimeZoneInfo zone = TimeZones.FindById("FLE Standard Time");
        DateTimeOffset anchor = new DateTimeOffset(2025, 3, 30, 0, 30, 0, TimeSpan.Zero) + HalfPeriod;

        foreach (MisfireStoreUnderTest store in await BothStores(anchor))
        {
            DateTimeOffset now = Now(store);

            TriggerKey triggerKey = new("dst-" + Guid.NewGuid().ToString("N"), Group);
            JobKey jobKey = new(triggerKey.Name, Group);
            IJobDetail job = Job(jobKey);

            TriggerBuilder<IJob> builder = TriggerBuilder.Create(store.Clock)
                .WithIdentity(triggerKey)
                .ForJob(jobKey)
                .StartAt(now)
                .WithRetryPolicy(RetryMatrixCases.Policy);

            builder = shape == RetryTriggerShape.CalendarInterval
                ? builder.WithCalendarIntervalSchedule(x => x.WithInterval(1, IntervalUnit.Day).InTimeZone(zone))
                : builder.WithDailyTimeIntervalSchedule(x => x
                    .StartingDailyAt(new TimeOnly(0, 30))
                    .EndingDailyAt(new TimeOnly(0, 30))
                    .WithInterval(1, IntervalUnit.Hour)
                    .InTimeZone(zone));

            IOperableTrigger trigger = (IOperableTrigger) builder.Build();
            await Store(store, job, trigger, now);

            IOperableTrigger firing = await Fire(store, triggerKey);
            DateTimeOffset? regularNext = firing.NextFireTimeUtc;

            regularNext.Should().NotBeNull("{0}: the schedule has an occurrence after the gap", store.Name);

            (await Complete(store, firing, job, Failure())).Should().Be(SchedulerInstruction.RetryTrigger,
                "{0}: five minutes is nowhere near the next daily occurrence, gap or no gap", store.Name);

            IOperableTrigger stored = await store.Store.GetTrigger(triggerKey);
            stored.NextFireTimeUtc.Should().Be(now + RetryDelay,
                "{0}: a retry instant is absolute and owes nothing to a zone's rules", store.Name);

            store.Clock.Advance(RetryDelay);
            IOperableTrigger retryFiring = await Fire(store, triggerKey);
            await Complete(store, retryFiring, job, result: null);

            (await store.Store.GetTrigger(triggerKey)).NextFireTimeUtc.Should().Be(regularNext,
                "{0}: unwinding the retry puts back the occurrence the zone's rules chose, not one the retry "
                + "instant's arithmetic invented", store.Name);
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // A node that predates retries
    //////////////////////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// What a 3.x node in a mixed cluster does with a row that is waiting to retry. It knows nothing of
    /// the retry columns, so it reads the row as a trigger due at the retry instant, fires it as a
    /// scheduled occurrence, and advances the schedule from there.
    /// </summary>
    /// <remarks>
    /// Modelled by firing the stored trigger through the store with the attempt stripped, which is
    /// exactly what a node that never selects <c>RETRY_ATTEMPT</c> materializes. The degradation is one
    /// extra fire, and the schedule is intact afterwards — no lost occurrence, no duplicated one.
    /// </remarks>
    [Test]
    public async Task ANodeThatCannotSeeTheRetryColumnsFiresOnceMoreAndLeavesTheScheduleIntact()
    {
        DateTimeOffset anchor = Anchor();
        RetryMatrixCase testCase = RetryMatrixCases.Row(RetryTriggerShape.Cron);

        foreach (MisfireStoreUnderTest store in await BothStores(anchor))
        {
            DateTimeOffset now = Now(store);

            (IJobDetail job, IOperableTrigger trigger) = await Given(store, testCase, now, "mixed-cluster");
            DateTimeOffset? regularNext = RegularNextAfterOneFire(testCase, now, store.Clock);

            await Complete(store, await Fire(store, trigger.Key), job, Failure());
            store.Clock.Advance(RetryDelay);

            IOperableTrigger firing = await Fire(store, trigger.Key);

            // The old node's view: a trigger with a fire time, and no notion of an attempt.
            ((TriggerBase) firing).RetryAttempt = 0;
            firing.RetryPolicy = null;

            SchedulerInstruction instruction = firing.ExecutionComplete(Context(), result: null);
            instruction.Should().Be(SchedulerInstruction.NoInstruction,
                "{0}: an old node completes the firing the only way it knows", store.Name);

            await store.Store.TriggeredJobComplete(firing, job, instruction);

            IOperableTrigger stored = await store.Store.GetTrigger(trigger.Key);
            stored.NextFireTimeUtc.Should().Be(regularNext,
                "{0}: the schedule survives a node that fired the retry instant as though it were an "
                + "occurrence — the degradation is one extra fire, not a lost or duplicated occurrence",
                store.Name);
        }
    }

    /// <summary>A cron expression that fires once a day, at the UTC time of <paramref name="when" />.</summary>
    private static string DailyCronAt(DateTimeOffset when)
    {
        DateTime utc = when.UtcDateTime;
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{utc.Second} {utc.Minute} {utc.Hour} * * ?");
    }
}
