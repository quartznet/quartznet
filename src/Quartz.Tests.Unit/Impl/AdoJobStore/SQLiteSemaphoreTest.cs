using FluentAssertions;

using NUnit.Framework;

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

public class SQLiteSemaphoreTest
{
    private SQLiteSemaphore semaphore = null!;

    [SetUp]
    public void SetUp()
    {
        semaphore = new SQLiteSemaphore();
    }

    [Test]
    public void RequiresConnection_ShouldReturnFalse()
    {
        semaphore.RequiresConnection.Should().BeFalse();
    }

    [Test]
    public async Task ObtainLock_ShouldAcquireAndRelease()
    {
        Guid requestorId = Guid.NewGuid();

        bool obtained = await semaphore.ObtainLock(requestorId, null, JobStoreSupport.LockTriggerAccess);
        obtained.Should().BeTrue();

        await semaphore.ReleaseLock(requestorId, JobStoreSupport.LockTriggerAccess);
    }

    [Test]
    public async Task ObtainLock_DifferentLockNames_ShouldShareSameGlobalGate()
    {
        Guid requestor1 = Guid.NewGuid();
        Guid requestor2 = Guid.NewGuid();

        // First requestor acquires TRIGGER_ACCESS
        bool obtained = await semaphore.ObtainLock(requestor1, null, JobStoreSupport.LockTriggerAccess);
        obtained.Should().BeTrue();

        // Second requestor tries STATE_ACCESS — should block because it's the same global lock
        using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(200));
        bool obtained2 = await semaphore.ObtainLock(requestor2, null, JobStoreSupport.LockStateAccess, cts.Token);
        obtained2.Should().BeFalse("the global lock is held by another requestor");

        await semaphore.ReleaseLock(requestor1, JobStoreSupport.LockTriggerAccess);
    }

    [Test]
    public async Task ObtainLock_TwoRequestors_ShouldSerialize()
    {
        Guid requestor1 = Guid.NewGuid();
        Guid requestor2 = Guid.NewGuid();

        bool obtained1 = await semaphore.ObtainLock(requestor1, null, JobStoreSupport.LockTriggerAccess);
        obtained1.Should().BeTrue();

        // Second requestor should block and fail to acquire
        using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(200));
        bool blocked = await semaphore.ObtainLock(requestor2, null, JobStoreSupport.LockTriggerAccess, cts.Token);
        blocked.Should().BeFalse("the lock is held by another requestor");

        // Release first, then second should succeed
        await semaphore.ReleaseLock(requestor1, JobStoreSupport.LockTriggerAccess);

        bool obtained2 = await semaphore.ObtainLock(requestor2, null, JobStoreSupport.LockTriggerAccess);
        obtained2.Should().BeTrue();

        await semaphore.ReleaseLock(requestor2, JobStoreSupport.LockTriggerAccess);
    }

    [Test]
    public async Task ObtainLock_SameRequestor_ShouldBeReentrant()
    {
        Guid requestorId = Guid.NewGuid();

        bool obtained1 = await semaphore.ObtainLock(requestorId, null, JobStoreSupport.LockTriggerAccess);
        obtained1.Should().BeTrue();

        // Same requestor acquires again with different lock name — should succeed (reentrant)
        bool obtained2 = await semaphore.ObtainLock(requestorId, null, JobStoreSupport.LockStateAccess);
        obtained2.Should().BeTrue();

        // Release one — semaphore should still be held
        await semaphore.ReleaseLock(requestorId, JobStoreSupport.LockStateAccess);

        // Another requestor should still be blocked
        Guid otherRequestor = Guid.NewGuid();
        using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(200));
        bool blocked = await semaphore.ObtainLock(otherRequestor, null, JobStoreSupport.LockTriggerAccess, cts.Token);
        blocked.Should().BeFalse("the semaphore is still held after partial release");

        // Release the remaining lock — semaphore should now be free
        await semaphore.ReleaseLock(requestorId, JobStoreSupport.LockTriggerAccess);

        bool obtained3 = await semaphore.ObtainLock(otherRequestor, null, JobStoreSupport.LockTriggerAccess);
        obtained3.Should().BeTrue();

        await semaphore.ReleaseLock(otherRequestor, JobStoreSupport.LockTriggerAccess);
    }

    [Test]
    public async Task ObtainLock_SameRequestorSameLockName_ShouldBeReentrant()
    {
        Guid requestorId = Guid.NewGuid();

        bool obtained1 = await semaphore.ObtainLock(requestorId, null, JobStoreSupport.LockTriggerAccess);
        obtained1.Should().BeTrue();

        // Same requestor acquires same lock name again — should succeed (reentrant)
        bool obtained2 = await semaphore.ObtainLock(requestorId, null, JobStoreSupport.LockTriggerAccess);
        obtained2.Should().BeTrue();

        // Release once — semaphore should still be held (lockCount > 0)
        await semaphore.ReleaseLock(requestorId, JobStoreSupport.LockTriggerAccess);

        Guid otherRequestor = Guid.NewGuid();
        using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(200));
        bool blocked = await semaphore.ObtainLock(otherRequestor, null, JobStoreSupport.LockTriggerAccess, cts.Token);
        blocked.Should().BeFalse("the semaphore is still held after one of two releases");

        // Release second time — semaphore should now be free
        await semaphore.ReleaseLock(requestorId, JobStoreSupport.LockTriggerAccess);

        bool obtained3 = await semaphore.ObtainLock(otherRequestor, null, JobStoreSupport.LockTriggerAccess);
        obtained3.Should().BeTrue();

        await semaphore.ReleaseLock(otherRequestor, JobStoreSupport.LockTriggerAccess);
    }

    [Test]
    public async Task ReleaseLock_NotOwner_ShouldNotThrow()
    {
        Guid requestorId = Guid.NewGuid();
        Guid wrongRequestorId = Guid.NewGuid();

        await semaphore.ObtainLock(requestorId, null, JobStoreSupport.LockTriggerAccess);

        // Releasing with wrong requestor should not throw
        Func<Task> act = async () => await semaphore.ReleaseLock(wrongRequestorId, JobStoreSupport.LockTriggerAccess);
        await act.Should().NotThrowAsync();

        // Original owner can still release
        await semaphore.ReleaseLock(requestorId, JobStoreSupport.LockTriggerAccess);
    }

    [Test]
    public async Task ObtainLock_Cancelled_ShouldReturnFalse()
    {
        Guid requestorId = Guid.NewGuid();

        using CancellationTokenSource cts = new();
        cts.Cancel();

        bool obtained = await semaphore.ObtainLock(requestorId, null, JobStoreSupport.LockTriggerAccess, cts.Token);
        obtained.Should().BeFalse();
    }
}
