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

using Microsoft.Extensions.Time.Testing;

using Quartz.Core;
using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.Triggers;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// How long the scheduling loop waits after a round that acquired nothing, while due work is blocked
/// behind a running firing whose end it may not be told of (#3988).
/// </summary>
/// <remarks>
/// <para>
/// The store is scripted, round by round, and the clock is fake: every wait the loop parks in is an
/// arming of its scheduling timer, which the test reads and then advances the clock past. Nothing else
/// arms that timer, because nothing here is ever acquired to wait for.
/// </para>
/// <para>
/// The idle wait is ten seconds, so a randomized one is eight to ten, and every shorter wait is the
/// loop looking again early.
/// </para>
/// </remarks>
[NonParallelizable]
public sealed class SchedulerLoopBlockedRetryTest
{
    private static readonly TimeSpan idleWaitTime = TimeSpan.FromSeconds(10);

    /// <summary>The shortest a randomized idle wait can be: the idle wait less a fifth.</summary>
    private static readonly TimeSpan shortestIdleWait = idleWaitTime - idleWaitTime / 5;

    private static readonly TimeSpan observationDeadline = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The waits from the first look after a blocked trigger until the doubling reaches the idle wait:
    /// 100 ms, then twice as long each time.
    /// </summary>
    private static readonly TimeSpan[] backoff =
    [
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(200),
        TimeSpan.FromMilliseconds(400),
        TimeSpan.FromMilliseconds(800),
        TimeSpan.FromMilliseconds(1600),
        TimeSpan.FromMilliseconds(3200),
        TimeSpan.FromMilliseconds(6400),
    ];

    /// <summary>
    /// The same waits while the store keeps saying triggers are held: they stop growing at five seconds,
    /// and stay there.
    /// </summary>
    private static readonly TimeSpan[] heldBackoff =
    [
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(200),
        TimeSpan.FromMilliseconds(400),
        TimeSpan.FromMilliseconds(800),
        TimeSpan.FromMilliseconds(1600),
        TimeSpan.FromMilliseconds(3200),
        TimeSpan.FromSeconds(5),
    ];

    private ArmingRecordingTimeProvider clock;
    private ScriptedAcquisitionJobStore store;
    private QuartzSchedulerThread thread;

    /// <summary>How many waits of the loop the test has read so far.</summary>
    private int parked;

    [SetUp]
    public async Task SetUp()
    {
        parked = 0;
        clock = new ArmingRecordingTimeProvider(new FakeTimeProvider(new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero)));
        store = new ScriptedAcquisitionJobStore();
        await store.Initialize(TestJobStores.Identity());
    }

    [TearDown]
    public async Task TearDown()
    {
        if (thread is not null)
        {
            await thread.Halt(wait: true);
            await thread.Shutdown();
        }

        await store.Shutdown();
    }

    [Test]
    public void TheFirstLookAfterABlockedTriggerIsATenthOfASecondAwayAndAHeldOneAtMostFiveSeconds()
    {
        QuartzSchedulerThread.BlockedRetryStart.Should().Be(backoff[0],
            "the waits this fixture expects start where the loop's do");
        QuartzSchedulerThread.HeldRetryLimit.Should().Be(heldBackoff[^1],
            "and stop growing where the loop's do while something is held");
    }

    /// <summary>
    /// The case the issue is about: the store could not fire a trigger because a firing of its job, which
    /// disallows concurrent execution, is running — on another node, which will not tell this one when it
    /// ends. The loop looks again after a tenth of a second, and then twice as long each time it still
    /// finds nothing, until the wait is its idle wait again.
    /// </summary>
    [Test]
    public async Task ATriggerBlockedBehindARunningFiringIsLookedForAgainSoonThenLessAndLessOften()
    {
        store.Script(FiredBlocked("blocked"));
        StartLoop();

        List<TimeSpan> waits = await WaitsOfTheNextRounds(backoff.Length + 2);

        store.Releases.Entries.Should().Equal([new TriggerKey("blocked", "g")],
            "a trigger the store could not fire is still released, as one it did not fire is");
        waits.Take(backoff.Length).Should().Equal(backoff,
            "a firing elsewhere ends without telling this loop, so it looks again soon and then less and less often");
        waits.Skip(backoff.Length).Should().OnlyContain(x => x >= shortestIdleWait && x <= idleWaitTime,
            "once the doubled wait would be the idle wait, the loop is back to idle waits: a job that runs for hours "
            + "elsewhere costs a handful of looks, not a poll");
    }

    /// <summary>
    /// Nothing blocked, nothing changes: a loop that finds nothing waits out its idle wait every round, as
    /// it always did.
    /// </summary>
    [Test]
    public async Task WithNothingBlockedEveryEmptyRoundWaitsTheIdleWait()
    {
        StartLoop();

        List<TimeSpan> waits = await WaitsOfTheNextRounds(4);

        waits.Should().OnlyContain(x => x >= shortestIdleWait && x <= idleWaitTime,
            "an idle scheduler must not poll its store any more often than before");
    }

    /// <summary>
    /// The store passing due triggers over behind an executing job says as much as a trigger it could
    /// not fire. Unlike that, the same triggers are passed over every round until the job ends, so the
    /// count is not started again by each: it runs up to five seconds and stays there, which bounds how
    /// late a held trigger is found after its holder ends without polling for it.
    /// </summary>
    [Test]
    public async Task WhileTriggersStayHeldTheWaitStopsGrowingAtFiveSeconds()
    {
        store.PassOverEveryRound = 1;
        StartLoop();

        List<TimeSpan> waits = await WaitsOfTheNextRounds(heldBackoff.Length + 3);

        waits.Take(heldBackoff.Length).Should().Equal(heldBackoff,
            "the first round to pass triggers over starts the count, and each one after it doubles the wait");
        waits.Skip(heldBackoff.Length).Should().OnlyContain(x => x == QuartzSchedulerThread.HeldRetryLimit,
            "triggers held for as long as the job runs are looked for every five seconds: not polled for, and not left "
            + "an idle wait behind the end of the run");
    }

    /// <summary>
    /// A trigger the store could not fire is fresh news, wherever the count had got to: the loop starts
    /// looking soon again.
    /// </summary>
    [Test]
    public async Task ATriggerBlockedAgainStartsTheCountAgain()
    {
        store.Script(FiredBlocked("first"));
        StartLoop();

        List<TimeSpan> before = await WaitsOfTheNextRounds(2);
        before.Add(await NextWait());
        store.Script(FiredBlocked("second"));
        Pass(before[^1]);
        List<TimeSpan> after = await WaitsOfTheNextRounds(2);

        before.Should().Equal(backoff[0], backoff[1], backoff[2]);
        after.Should().Equal([backoff[0], backoff[1]],
            "the second blocked trigger may be held by a firing that ends at once, so the loop looks soon again");
    }

    /// <summary>
    /// A round that passed triggers over and then a round that did not: the doubling carries on to the
    /// idle wait and ends there, since nothing is known to be blocked any more.
    /// </summary>
    [Test]
    public async Task TheCountEndsAtTheIdleWaitOnceNothingIsPassedOverAnyMore()
    {
        store.PassOverEveryRound = 1;
        StartLoop();

        List<TimeSpan> blocked = await WaitsOfTheNextRounds(heldBackoff.Length);
        blocked.Add(await NextWait());
        store.PassOverEveryRound = 0;
        Pass(blocked[^1]);
        List<TimeSpan> after = await WaitsOfTheNextRounds(2);
        after.Add(await NextWait());

        blocked.Take(heldBackoff.Length).Should().Equal(heldBackoff);
        after[0].Should().Be(QuartzSchedulerThread.HeldRetryLimit, "the count runs on from where it was");
        after.Skip(1).Should().OnlyContain(x => x >= shortestIdleWait && x <= idleWaitTime,
            "and runs out at the idle wait once nothing is held");

        store.PassOverEveryRound = 1;
        Pass(after[^1]);
        List<TimeSpan> again = await WaitsOfTheNextRounds(2);
        again.Should().Equal([backoff[0], backoff[1]],
            "triggers passed over after the count ran out are news again");
    }

    /// <summary>
    /// Triggers held by a later firing than the last round's start the count again: the job ended and
    /// was taken again in between, and a job changing hands that often frees up often too. Held by the
    /// same firing, the count runs on.
    /// </summary>
    [Test]
    public async Task TriggersHeldByALaterFiringThanBeforeStartTheCountAgain()
    {
        DateTimeOffset firstRun = new(2026, 10, 5, 7, 59, 0, TimeSpan.Zero);
        store.PassOverEveryRound = 1;
        store.BlockingFiredUtc = firstRun;
        StartLoop();

        List<TimeSpan> sameRun = await WaitsOfTheNextRounds(2);
        sameRun.Add(await NextWait());
        store.BlockingFiredUtc = firstRun.AddSeconds(1);
        Pass(sameRun[^1]);
        List<TimeSpan> laterRun = await WaitsOfTheNextRounds(2);

        sameRun.Should().Equal([backoff[0], backoff[1], backoff[2]], "one firing holding the triggers throughout");
        laterRun.Should().Equal([backoff[0], backoff[1]],
            "the triggers are held by another firing than before, so the job was free in between and may be again soon");
    }

    /// <summary>
    /// A job that changes hands every round starts the count again only once it has grown to four times its
    /// start, so the loop does not look every tenth of a second for as long as the job keeps changing hands.
    /// </summary>
    [Test]
    public async Task AJobChangingHandsEveryRoundIsNotLookedForEveryTenthOfASecond()
    {
        store.PassOverEveryRound = 1;
        store.BlockingFiredUtc = new DateTimeOffset(2026, 10, 5, 7, 59, 0, TimeSpan.Zero);
        store.AdvanceBlockingFiredUtcEveryRound = true;
        StartLoop();

        List<TimeSpan> waits = await WaitsOfTheNextRounds(6);

        waits.Should().Equal([backoff[0], backoff[1], backoff[0], backoff[1], backoff[0], backoff[1]],
            "a later firing every round is news, but restarting on each would be a look every tenth of a second");
    }

    /// <summary>
    /// An idle wait shorter than the first early look leaves every wait the idle wait: the loop never
    /// waits longer because something is blocked.
    /// </summary>
    [Test]
    public async Task AnIdleWaitShorterThanTheFirstEarlyLookIsNeverExceeded()
    {
        TimeSpan shortIdleWait = TimeSpan.FromMilliseconds(50);
        store.Script(FiredBlocked("blocked"));
        StartLoop(shortIdleWait);

        List<TimeSpan> waits = await WaitsOfTheNextRounds(3);

        waits.Should().OnlyContain(x => x <= shortIdleWait, "the early look is a shortcut, never a delay");
    }

    private static TriggerAcquisitionResult FiredBlocked(string triggerName)
    {
        return new TriggerAcquisitionResult
        {
            Due = [new SimpleTriggerImpl { Key = new TriggerKey(triggerName, "g"), JobKey = new JobKey("serial", "g") }],
            Fired = [TriggerFiredResult.Blocked],
        };
    }

    /// <summary>
    /// The waits the loop parks in for the next <paramref name="rounds" /> rounds that acquire nothing,
    /// advancing the clock past each so that the loop goes round again.
    /// </summary>
    private async Task<List<TimeSpan>> WaitsOfTheNextRounds(int rounds)
    {
        List<TimeSpan> waits = [];
        for (int i = 0; i < rounds; i++)
        {
            TimeSpan wait = await NextWait();
            waits.Add(wait);
            Pass(wait);
        }

        return waits;
    }

    /// <summary>
    /// The wait the loop parks in next, once it is parked in it. The clock stands still until
    /// <see cref="Pass" />, so whatever the test changes meanwhile is what the next round sees.
    /// </summary>
    private async Task<TimeSpan> NextWait()
    {
        int seen = parked;
        await clock.Armed(IsSchedulingWake, seen + 1).WaitAsync(observationDeadline);
        parked = seen + 1;
        return clock.Armings.Where(IsSchedulingWake).ElementAt(seen).DueTime;
    }

    /// <summary>Advances the clock past the wait the loop is parked in, which sends it round again.</summary>
    private void Pass(TimeSpan wait) => clock.Clock.Advance(wait);

    /// <summary>
    /// The scheduling wake is the first timer the loop creates; the pause wake, the second, is never
    /// armed here.
    /// </summary>
    private static bool IsSchedulingWake(TimerArming arming) => arming.Timer == 0;

    private void StartLoop(TimeSpan? idleWait = null)
    {
        IThreadPool threadPool = A.Fake<IThreadPool>();
        A.CallTo(() => threadPool.PoolSize).Returns(4);
        A.CallTo(() => threadPool.WaitForAvailableThreads(A<CancellationToken>.Ignored)).Returns(new ValueTask<int>(4));

        QuartzSchedulerResources resources = new()
        {
            Name = "blockedRetryTest",
            InstanceId = "blockedRetryTestInstance",
            IdleWaitTime = idleWait ?? idleWaitTime,
            MaxBatchSize = 1,
            JobStore = store,
            ThreadPool = threadPool,
            JobRunShellFactory = new ScriptedJobRunShellFactory(),
            TimeProvider = clock,
        };

        thread = new QuartzSchedulerThread(new QuartzScheduler(resources), resources);
        thread.TogglePause(pause: false);
        thread.Start();
    }

    /// <summary>
    /// Answers each acquisition with the next scripted round, or with nothing — passing over
    /// <see cref="PassOverEveryRound" /> blocked triggers, held by a firing fired at
    /// <see cref="BlockingFiredUtc" /> — once the script has run out.
    /// </summary>
    private sealed class ScriptedAcquisitionJobStore : DelegatingJobStore
    {
        private readonly Queue<TriggerAcquisitionResult> script = new();
        private readonly Lock gate = new();
        private volatile int passOverEveryRound;

        public ScriptedAcquisitionJobStore() : base(TestJobStores.Ram())
        {
        }

        /// <summary>How many blocked triggers each unscripted round passes over.</summary>
        public int PassOverEveryRound
        {
            get => passOverEveryRound;
            set => passOverEveryRound = value;
        }

        /// <summary>Whether each unscripted round says a firing a second later holds them than the last.</summary>
        public bool AdvanceBlockingFiredUtcEveryRound { get; set; }

        /// <summary>When the firing holding them was fired, as each unscripted round says.</summary>
        public DateTimeOffset? BlockingFiredUtc
        {
            get
            {
                lock (gate)
                {
                    return field;
                }
            }
            set
            {
                lock (gate)
                {
                    field = value;
                }
            }
        }

        public CallLog<TriggerKey> Releases { get; } = new();

        public void Script(TriggerAcquisitionResult round)
        {
            lock (gate)
            {
                script.Enqueue(round);
            }
        }

        public override ValueTask<TriggerAcquisitionResult> AcquireNextTriggersAndFireDue(TriggerAcquisitionRequest request, CancellationToken cancellationToken = default)
        {
            lock (gate)
            {
                if (script.TryDequeue(out TriggerAcquisitionResult round))
                {
                    return new ValueTask<TriggerAcquisitionResult>(round);
                }
            }

            DateTimeOffset? blockingFiredUtc = BlockingFiredUtc;
            if (AdvanceBlockingFiredUtcEveryRound && blockingFiredUtc is { } fired)
            {
                BlockingFiredUtc = fired.AddSeconds(1);
            }

            return new ValueTask<TriggerAcquisitionResult>(new TriggerAcquisitionResult
            {
                Blocked = PassOverEveryRound,
                LatestBlockingFiredUtc = blockingFiredUtc,
            });
        }

        public override ValueTask ReleaseAcquiredTrigger(IOperableTrigger trigger, CancellationToken cancellationToken = default)
        {
            Releases.Record(trigger.Key);
            return default;
        }
    }
}
