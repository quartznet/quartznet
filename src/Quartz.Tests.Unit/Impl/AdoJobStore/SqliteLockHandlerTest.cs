using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

public class SqliteLockHandlerTest
{
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
        using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(200));
        bool obtained2 = await lockHandler.AcquireLock(requestor2, null, SchedulerLock.StateAccess, cts.Token);
        obtained2.Should().BeFalse("the global lock is held by another requestor");

        await lockHandler.ReleaseLock(requestor1, SchedulerLock.TriggerAccess);
    }

    [Test]
    public async Task AcquireLock_TwoRequestors_ShouldSerialize()
    {
        Guid requestor1 = Guid.NewGuid();
        Guid requestor2 = Guid.NewGuid();

        bool obtained1 = await lockHandler.AcquireLock(requestor1, null, SchedulerLock.TriggerAccess);
        obtained1.Should().BeTrue();

        // Second requestor should block and fail to acquire
        using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(200));
        bool blocked = await lockHandler.AcquireLock(requestor2, null, SchedulerLock.TriggerAccess, cts.Token);
        blocked.Should().BeFalse("the lock is held by another requestor");

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
        using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(200));
        bool blocked = await lockHandler.AcquireLock(otherRequestor, null, SchedulerLock.TriggerAccess, cts.Token);
        blocked.Should().BeFalse("the lock is still held after partial release");

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
        using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(200));
        bool blocked = await lockHandler.AcquireLock(otherRequestor, null, SchedulerLock.TriggerAccess, cts.Token);
        blocked.Should().BeFalse("the lock is still held after one of two releases");

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

    [Test]
    public async Task AcquireLock_Cancelled_ShouldReturnFalse()
    {
        Guid requestorId = Guid.NewGuid();

        using CancellationTokenSource cts = new();
        cts.Cancel();

        bool obtained = await lockHandler.AcquireLock(requestorId, null, SchedulerLock.TriggerAccess, cts.Token);
        obtained.Should().BeFalse();
    }
}
