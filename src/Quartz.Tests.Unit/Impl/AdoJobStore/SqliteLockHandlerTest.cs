using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

public class SqliteLockHandlerTest
{
    /// <summary>
    /// How long an awaited handover may take before the test gives up. Not a timing assertion — it only
    /// decides whether a handler that never answers is reported as a failure or hangs the run.
    /// </summary>
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(30);

    private SqliteLockHandler lockHandler = null!;

    [SetUp]
    public void SetUp()
    {
        lockHandler = new SqliteLockHandler();
    }

    [Test]
    public void RequiresConnection_ShouldReturnFalse()
    {
        lockHandler.RequiresConnection.Should().BeFalse();
    }

    [Test]
    public async Task AcquireLock_ShouldAcquireAndRelease()
    {
        Guid requestorId = Guid.NewGuid();

        bool obtained = await lockHandler.AcquireLock(requestorId, null, SchedulerLock.TriggerAccess);
        obtained.Should().BeTrue();

        await lockHandler.ReleaseLock(requestorId, SchedulerLock.TriggerAccess);
    }

    [Test]
    public async Task AcquireLock_DifferentLockNames_ShouldShareSameGlobalGate()
    {
        Guid requestor1 = Guid.NewGuid();
        Guid requestor2 = Guid.NewGuid();

        // First requestor acquires TRIGGER_ACCESS
        bool obtained = await lockHandler.AcquireLock(requestor1, null, SchedulerLock.TriggerAccess);
        obtained.Should().BeTrue();

        // Second requestor tries STATE_ACCESS — should block because it's the same global lock
        await ShouldQueueBehindTheGate(requestor2, SchedulerLock.StateAccess,
            "the global lock is held by another requestor");

        await lockHandler.ReleaseLock(requestor1, SchedulerLock.TriggerAccess);
    }

    [Test]
    public async Task AcquireLock_TwoRequestors_ShouldSerialize()
    {
        Guid requestor1 = Guid.NewGuid();
        Guid requestor2 = Guid.NewGuid();

        bool obtained1 = await lockHandler.AcquireLock(requestor1, null, SchedulerLock.TriggerAccess);
        obtained1.Should().BeTrue();

        // Second requestor should block and never be granted the lock
        await ShouldQueueBehindTheGate(requestor2, SchedulerLock.TriggerAccess,
            "the lock is held by another requestor");

        // Release first, then second should succeed
        await lockHandler.ReleaseLock(requestor1, SchedulerLock.TriggerAccess);

        bool obtained2 = await lockHandler.AcquireLock(requestor2, null, SchedulerLock.TriggerAccess);
        obtained2.Should().BeTrue();

        await lockHandler.ReleaseLock(requestor2, SchedulerLock.TriggerAccess);
    }

    [Test]
    public async Task AcquireLock_SameRequestor_ShouldBeReentrant()
    {
        Guid requestorId = Guid.NewGuid();

        bool obtained1 = await lockHandler.AcquireLock(requestorId, null, SchedulerLock.TriggerAccess);
        obtained1.Should().BeTrue();

        // Same requestor acquires again with different lock name — should succeed (reentrant)
        bool obtained2 = await lockHandler.AcquireLock(requestorId, null, SchedulerLock.StateAccess);
        obtained2.Should().BeTrue();

        // Release one — the lock should still be held
        await lockHandler.ReleaseLock(requestorId, SchedulerLock.StateAccess);

        // Another requestor should still be blocked
        Guid otherRequestor = Guid.NewGuid();
        await ShouldQueueBehindTheGate(otherRequestor, SchedulerLock.TriggerAccess,
            "the lock is still held after partial release");

        // Release the remaining lock — the lock should now be free
        await lockHandler.ReleaseLock(requestorId, SchedulerLock.TriggerAccess);

        bool obtained3 = await lockHandler.AcquireLock(otherRequestor, null, SchedulerLock.TriggerAccess);
        obtained3.Should().BeTrue();

        await lockHandler.ReleaseLock(otherRequestor, SchedulerLock.TriggerAccess);
    }

    [Test]
    public async Task AcquireLock_SameRequestorSameLockName_ShouldBeReentrant()
    {
        Guid requestorId = Guid.NewGuid();

        bool obtained1 = await lockHandler.AcquireLock(requestorId, null, SchedulerLock.TriggerAccess);
        obtained1.Should().BeTrue();

        // Same requestor acquires same lock name again — should succeed (reentrant)
        bool obtained2 = await lockHandler.AcquireLock(requestorId, null, SchedulerLock.TriggerAccess);
        obtained2.Should().BeTrue();

        // Release once — the lock should still be held (lockCount > 0)
        await lockHandler.ReleaseLock(requestorId, SchedulerLock.TriggerAccess);

        Guid otherRequestor = Guid.NewGuid();
        await ShouldQueueBehindTheGate(otherRequestor, SchedulerLock.TriggerAccess,
            "the lock is still held after one of two releases");

        // Release second time — the lock should now be free
        await lockHandler.ReleaseLock(requestorId, SchedulerLock.TriggerAccess);

        bool obtained3 = await lockHandler.AcquireLock(otherRequestor, null, SchedulerLock.TriggerAccess);
        obtained3.Should().BeTrue();

        await lockHandler.ReleaseLock(otherRequestor, SchedulerLock.TriggerAccess);
    }

    [Test]
    public async Task ReleaseLock_NotOwner_ShouldNotThrow()
    {
        Guid requestorId = Guid.NewGuid();
        Guid wrongRequestorId = Guid.NewGuid();

        await lockHandler.AcquireLock(requestorId, null, SchedulerLock.TriggerAccess);

        // Releasing with wrong requestor should not throw
        Func<Task> act = async () => await lockHandler.ReleaseLock(wrongRequestorId, SchedulerLock.TriggerAccess);
        await act.Should().NotThrowAsync();

        // Original owner can still release
        await lockHandler.ReleaseLock(requestorId, SchedulerLock.TriggerAccess);
    }

    /// <summary>
    /// A cancelled acquire reports the cancellation. It used to answer <see langword="false" />, which
    /// the job store reads as "this context already holds the lock, do not release it" — so the caller
    /// would have gone on to run the operation the lock exists to guard with nothing holding it. #3583.
    /// </summary>
    [Test]
    public async Task AcquireLock_Cancelled_ShouldThrowRatherThanAnswerFalse()
    {
        Guid requestorId = Guid.NewGuid();

        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Task<bool> cancelled = lockHandler.AcquireLock(requestorId, null, SchedulerLock.TriggerAccess, cancellation.Token).AsTask();

        Func<Task> act = () => cancelled;
        await act.Should().ThrowAsync<OperationCanceledException>(
            "false is reserved for a re-entrant acquire, and a caller told that runs unlocked");

        cancelled.IsCanceled.Should().BeTrue(
            "the caller asked to stop, so this is a cancelled task rather than a faulted one");
    }

    /// <summary>
    /// And it leaves the gate as it found it, whether it was free or held: a cancelled wait takes no
    /// hold and consumes no handover, so the next requestor is served exactly as it would have been.
    /// </summary>
    [Test]
    public async Task AcquireLock_Cancelled_ShouldLeaveTheGateFreeForTheNextRequestor()
    {
        Guid abandoning = Guid.NewGuid();
        Guid next = Guid.NewGuid();

        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Func<Task> act = async () => await lockHandler.AcquireLock(abandoning, null, SchedulerLock.TriggerAccess, cancellation.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        Task<bool> unclaimed = lockHandler.AcquireLock(next, null, SchedulerLock.TriggerAccess).AsTask();

        unclaimed.IsCompleted.Should().BeTrue(
            "the cancelled call took no hold on the gate, so there is nothing for this one to queue behind");
        (await unclaimed).Should().BeTrue();

        await lockHandler.ReleaseLock(next, SchedulerLock.TriggerAccess);
    }

    /// <summary>
    /// And when the gate was held, a waiter that gives up leaves it with its owner rather than stealing
    /// the handover on its way out.
    /// </summary>
    [Test]
    public async Task AcquireLock_CancelledWhileQueued_ShouldLeaveTheGateWithItsOwner()
    {
        Guid owner = Guid.NewGuid();
        Guid abandoning = Guid.NewGuid();
        Guid patient = Guid.NewGuid();

        (await lockHandler.AcquireLock(owner, null, SchedulerLock.TriggerAccess)).Should().BeTrue();

        await ShouldQueueBehindTheGate(abandoning, SchedulerLock.TriggerAccess, "the gate is the owner's");

        Task<bool> queued = lockHandler.AcquireLock(patient, null, SchedulerLock.TriggerAccess).AsTask();
        queued.IsCompleted.Should().BeFalse("the gate is still the owner's, cancellation notwithstanding");

        await lockHandler.ReleaseLock(owner, SchedulerLock.TriggerAccess);

        (await queued.WaitAsync(GiveUpAfter)).Should().BeTrue(
            "the abandoned waiter left without consuming the handover; had it taken the gate on its way "
            + "out, this release would have gone to a caller that no longer exists");

        await lockHandler.ReleaseLock(patient, SchedulerLock.TriggerAccess);
    }

    /// <summary>
    /// Waits for the gate on <paramref name="requestorId" />'s behalf, and asserts the wait never
    /// completes: it is still queued when the test cancels it, and the cancellation is what ends it.
    /// </summary>
    /// <remarks>
    /// The handler's answer is never <see langword="false" />, so "blocked" cannot be asserted by
    /// reading a return value — it is asserted by the request still being outstanding.
    /// </remarks>
    private async Task ShouldQueueBehindTheGate(Guid requestorId, SchedulerLock lockKind, string because)
    {
        using CancellationTokenSource cancellation = new();

        Task<bool> queued = lockHandler.AcquireLock(requestorId, null, lockKind, cancellation.Token).AsTask();
        queued.IsCompleted.Should().BeFalse(because);

        await cancellation.CancelAsync();

        Func<Task> act = () => queued.WaitAsync(GiveUpAfter);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
