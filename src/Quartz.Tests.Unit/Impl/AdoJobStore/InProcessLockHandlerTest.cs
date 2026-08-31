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

using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

using Quartz.Impl.AdoJobStore;
using Quartz.Tests.Unit.Plugin.History;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// What <see cref="InProcessLockHandler" /> promises the job store, which until now was described only
/// by a benchmark. It is the handler a single-node ADO store uses, so everything the store serializes —
/// every write to a job, a trigger or a calendar, and every acquisition — is serialized by this and
/// nothing else.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here waits on wall time. A lock that is held makes the next request's task incomplete
/// synchronously, so "the second caller is queued" is asserted by looking at the task rather than by
/// sleeping and hoping; the deadlines that do appear are give-up deadlines on an awaited handover, not
/// timing assertions.
/// </para>
/// <para>
/// <b>Ordering is deliberately not asserted.</b> The handler queues on
/// <see cref="SemaphoreSlim" />, whose documentation promises no order among waiters, so a test that
/// pinned first-in-first-out would be pinning an implementation detail of the BCL. What is asserted
/// instead is the property the store actually needs: every waiter is eventually served, and never two
/// at once.
/// </para>
/// </remarks>
public class InProcessLockHandlerTest
{
    /// <summary>
    /// How long an awaited handover may take before the test gives up. Not a timing assertion — the
    /// handover is a semaphore release on the same thread pool, so this only decides when a hang is
    /// reported as a failure instead of hanging the run.
    /// </summary>
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The warning <see cref="InProcessLockHandler.ReleaseLock" /> raises for a caller returning a lock
    /// it does not hold, spelled out rather than read from <c>LockHandlerLog</c>: an id is what an
    /// operator filters on, so a test reading the same constant the product logs from would let a
    /// renumbering through.
    /// </summary>
    private const int ReturnedByNonOwnerEvent = 3704;

    [Test]
    public async Task OnlyOneOwnerHoldsALockAtATime()
    {
        InProcessLockHandler lockHandler = new();
        Guid holder = Guid.NewGuid();
        Guid waiter = Guid.NewGuid();

        bool held = await lockHandler.AcquireLock(holder, conn: null, SchedulerLock.TriggerAccess);
        held.Should().BeTrue("nothing held the lock, so the first caller takes it");

        Task<bool> queued = lockHandler.AcquireLock(waiter, conn: null, SchedulerLock.TriggerAccess).AsTask();

        queued.IsCompleted.Should().BeFalse(
            "the lock is held, so the second caller has to wait for it — a handler that answered here "
            + "would be letting two callers write the same trigger rows at once");

        await lockHandler.ReleaseLock(holder, SchedulerLock.TriggerAccess);

        (await queued.WaitAsync(GiveUpAfter)).Should().BeTrue(
            "releasing hands the lock to whoever was waiting for it");
    }

    /// <summary>
    /// Re-entrancy, which is the reason the handler asks who owns a lock before waiting for it: the job
    /// store takes <c>TRIGGER_ACCESS</c> around an operation that may itself take <c>TRIGGER_ACCESS</c>,
    /// and a handler that queued would deadlock against itself.
    /// </summary>
    /// <remarks>
    /// The <see langword="false" /> is not a refusal. It is the store's release protocol: the answer
    /// means "you now hold this and are the one who must give it back", so the inner caller releases
    /// nothing and the outer one's single release is what frees the lock.
    /// </remarks>
    [Test]
    public async Task AnOwnerAskingAgainIsAnsweredWithoutWaitingAndWithoutTakingASecondHold()
    {
        InProcessLockHandler lockHandler = new();
        Guid owner = Guid.NewGuid();
        Guid other = Guid.NewGuid();

        (await lockHandler.AcquireLock(owner, conn: null, SchedulerLock.TriggerAccess)).Should().BeTrue();

        Task<bool> again = lockHandler.AcquireLock(owner, conn: null, SchedulerLock.TriggerAccess).AsTask();

        again.IsCompleted.Should().BeTrue("an owner that had to wait for its own lock would never be released");
        (await again).Should().BeFalse(
            "the answer says who has to release, and it is the outer caller; a nested caller that "
            + "released on a true would drop a lock its outer caller still needs");

        await lockHandler.ReleaseLock(owner, SchedulerLock.TriggerAccess);

        (await lockHandler.AcquireLock(other, conn: null, SchedulerLock.TriggerAccess)).Should().BeTrue(
            "one release is enough, because the nested acquisition never took a second hold to give back");
    }

    /// <summary>
    /// Returning a lock somebody else holds. It must not free the lock — that would hand the holder's
    /// rows to the next waiter while the holder is still writing them — and it must say so, because a
    /// store that does it is broken and the log is the only place that shows up.
    /// </summary>
    [Test]
    public async Task ReturningALockThisCallerNeverTookLeavesTheHolderHoldingIt()
    {
        RecordingLoggerProvider log = new();
        using LoggerFactory factory = new();
        factory.AddProvider(log);

        InProcessLockHandler lockHandler = new();
        lockHandler.Initialize(Context(factory));

        Guid holder = Guid.NewGuid();
        Guid stranger = Guid.NewGuid();

        (await lockHandler.AcquireLock(holder, conn: null, SchedulerLock.TriggerAccess)).Should().BeTrue();

        await lockHandler.ReleaseLock(stranger, SchedulerLock.TriggerAccess);

        Task<bool> queued = lockHandler.AcquireLock(stranger, conn: null, SchedulerLock.TriggerAccess).AsTask();

        queued.IsCompleted.Should().BeFalse(
            "the lock is still the holder's; a release by a non-owner that freed it would let the next "
            + "caller in while the holder was still working");

        log.Entries.Should().ContainSingle(entry => entry.EventId.Id == ReturnedByNonOwnerEvent)
            .Which.Level.Should().Be(LogLevel.Warning,
                "a caller returning a lock it never took is a bug in the caller, and nothing else reports it");

        await lockHandler.ReleaseLock(holder, SchedulerLock.TriggerAccess);
        (await queued.WaitAsync(GiveUpAfter)).Should().BeTrue();
    }

    /// <summary>
    /// A waiter whose token is cancelled while it is queued. It reports the cancellation, and it must
    /// leave the queue without consuming the handover, or the lock would be lost to a caller that has
    /// already given up.
    /// </summary>
    /// <remarks>
    /// It used to answer <see langword="false" />, which reads as "I took nothing" but is not what
    /// <see langword="false" /> means to the store: the store's word for that is a re-entrant acquire —
    /// "you already hold this, do not release it" — and a caller told that goes on to run its guarded
    /// operation with no lock at all. #3583.
    /// </remarks>
    [Test]
    public async Task AWaiterCancelledWhileQueuedReportsTheCancellationAndLeavesTheLockWhereItWas()
    {
        InProcessLockHandler lockHandler = new();
        Guid holder = Guid.NewGuid();
        Guid abandoning = Guid.NewGuid();
        Guid patient = Guid.NewGuid();

        (await lockHandler.AcquireLock(holder, conn: null, SchedulerLock.TriggerAccess)).Should().BeTrue();

        using CancellationTokenSource cancellation = new();
        Task<bool> abandoned = lockHandler.AcquireLock(abandoning, conn: null, SchedulerLock.TriggerAccess, cancellation.Token).AsTask();
        abandoned.IsCompleted.Should().BeFalse();

        await cancellation.CancelAsync();

        // Deadlined rather than awaited bare, so a handler that swallowed the cancellation and went back
        // to waiting fails this test instead of hanging the run.
        Func<Task> awaitAbandoned = () => abandoned.WaitAsync(GiveUpAfter);
        await awaitAbandoned.Should().ThrowAsync<OperationCanceledException>(
            "answering false would tell the store the caller already held the lock, and the guarded "
            + "operation would then run with nothing holding it");

        abandoned.IsCanceled.Should().BeTrue(
            "the caller asked to stop, so this is a cancelled task rather than a faulted one — which is "
            + "what lets a caller matching on cancellation tell it from the store falling over");

        Task<bool> queued = lockHandler.AcquireLock(patient, conn: null, SchedulerLock.TriggerAccess).AsTask();
        queued.IsCompleted.Should().BeFalse("the lock is still the holder's, cancellation notwithstanding");

        await lockHandler.ReleaseLock(holder, SchedulerLock.TriggerAccess);

        (await queued.WaitAsync(GiveUpAfter)).Should().BeTrue(
            "the abandoned waiter left the queue without consuming the handover; had it taken the lock on "
            + "its way out, this release would have gone to a caller that no longer exists and the store "
            + "would be stuck");
    }

    /// <summary>
    /// The same rule when the token has already fired before the call: still an exception, and still no
    /// mark on the lock — the requestor that gave up is not recorded as an owner, so nothing has to be
    /// released on its behalf and the next caller is served immediately.
    /// </summary>
    [Test]
    public async Task AnAcquireWhoseTokenHasAlreadyFiredTakesNothingAtAll()
    {
        InProcessLockHandler lockHandler = new();
        Guid abandoning = Guid.NewGuid();
        Guid next = Guid.NewGuid();

        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Func<Task> act = async () => await lockHandler.AcquireLock(abandoning, conn: null, SchedulerLock.TriggerAccess, cancellation.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        Task<bool> unclaimed = lockHandler.AcquireLock(next, conn: null, SchedulerLock.TriggerAccess).AsTask();

        unclaimed.IsCompleted.Should().BeTrue(
            "the cancelled call never took the lock, so there is nothing for this one to queue behind");
        (await unclaimed).Should().BeTrue();
    }

    [Test]
    public async Task TheTwoLocksAreHeldIndependently()
    {
        InProcessLockHandler lockHandler = new();
        Guid owner = Guid.NewGuid();

        (await lockHandler.AcquireLock(owner, conn: null, SchedulerLock.TriggerAccess)).Should().BeTrue();

        Task<bool> stateAccess = lockHandler.AcquireLock(owner, conn: null, SchedulerLock.StateAccess).AsTask();

        stateAccess.IsCompleted.Should().BeTrue(
            "the cluster check-in runs on a transaction of its own so that it cannot queue behind trigger "
            + "work, and one handler holding both locks as one would put it right back in that queue");
        (await stateAccess).Should().BeTrue("STATE_ACCESS was free; this caller now holds it too");
    }

    /// <summary>
    /// Sixteen callers, one lock: every one of them is served, and no two are inside it together. The
    /// order they are served in is <see cref="SemaphoreSlim" />'s business and is not asserted — see the
    /// note on the fixture.
    /// </summary>
    [Test]
    public async Task EveryContenderIsServedAndNoTwoAreInsideTheLockTogether()
    {
        const int Contenders = 16;

        InProcessLockHandler lockHandler = new();
        ConcurrentBag<int> occupancyOnEntry = [];
        int inside = 0;
        int served = 0;

        async Task Contend()
        {
            Guid requestorId = Guid.NewGuid();

            (await lockHandler.AcquireLock(requestorId, conn: null, SchedulerLock.TriggerAccess)).Should().BeTrue();

            occupancyOnEntry.Add(Interlocked.Increment(ref inside));
            Interlocked.Increment(ref served);

            // Hands the thread back while holding, so that a handler which let two callers in would be
            // caught with both of them counted rather than passing on how fast the body is.
            await Task.Yield();

            Interlocked.Decrement(ref inside);
            await lockHandler.ReleaseLock(requestorId, SchedulerLock.TriggerAccess);
        }

        await Task.WhenAll(Enumerable.Range(0, Contenders).Select(_ => Task.Run(Contend)))
            .WaitAsync(GiveUpAfter);

        served.Should().Be(Contenders,
            "a waiter that is never handed the lock is a scheduler that stops writing, and it is the "
            + "queue rather than the order that has to hold");
        occupancyOnEntry.Should().OnlyContain(count => count == 1,
            "the lock is what serializes every write the store makes; two callers counted inside it at "
            + "once is that serialization gone");
    }

    private static LockHandlerContext Context(ILoggerFactory loggerFactory) => new()
    {
        SchedulerName = "TESTSCHED",
        InstanceId = "node-1",
        TablePrefix = AdoConstants.DefaultTablePrefix,
        LoggerFactory = loggerFactory,
    };
}
