using System;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using Quartz.Impl.AdoJobStore;

using StackExchange.Redis;

namespace Quartz.Tests.Integration.Impl.AdoJobStore.Redis;

[NonParallelizable]
[Category("db-redis")]
public class RedisSemaphoreTest
{
    private RedisSemaphore semaphore;
    private IConnectionMultiplexer redis;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        semaphore = new RedisSemaphore
        {
            RedisConfiguration = RedisTestEnvironment.ConnectionString,
            SchedName = "TestScheduler",
            KeyPrefix = "quartz:test:lock:"
        };

        redis = await ConnectionMultiplexer.ConnectAsync(RedisTestEnvironment.ConnectionString);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        redis?.Dispose();
    }

    [SetUp]
    public async Task SetUp()
    {
        // Clean up any leftover keys from previous tests
        IDatabase db = redis.GetDatabase();
        await db.KeyDeleteAsync("quartz:test:lock:TestScheduler:TRIGGER_ACCESS");
        await db.KeyDeleteAsync("quartz:test:lock:TestScheduler:STATE_ACCESS");
    }

    [TearDown]
    public async Task TearDown()
    {
        IDatabase db = redis.GetDatabase();
        await db.KeyDeleteAsync("quartz:test:lock:TestScheduler:TRIGGER_ACCESS");
        await db.KeyDeleteAsync("quartz:test:lock:TestScheduler:STATE_ACCESS");
    }

    [Test]
    public void RequiresConnection_ShouldReturnFalse()
    {
        Assert.That(semaphore.RequiresConnection, Is.False);
    }

    [Test]
    public void DefaultProperties_ShouldHaveSensibleDefaults()
    {
        var sut = new RedisSemaphore();

        Assert.That(sut.RedisConfiguration, Is.EqualTo("localhost:6379"));
        Assert.That(sut.KeyPrefix, Is.EqualTo("quartz:lock:"));
        Assert.That(sut.LockTtlMilliseconds, Is.EqualTo(30_000));
        Assert.That(sut.LockRetryIntervalMilliseconds, Is.EqualTo(100));
    }

    [Test]
    public async Task ObtainLock_ShouldAcquireAndRelease()
    {
        Guid requestorId = Guid.NewGuid();

        bool obtained = await semaphore.ObtainLock(
            requestorId, null, JobStoreSupport.LockTriggerAccess);

        Assert.That(obtained, Is.True);

        // Verify the key exists in Redis
        IDatabase db = redis.GetDatabase();
        RedisValue value = await db.StringGetAsync("quartz:test:lock:TestScheduler:TRIGGER_ACCESS");
        Assert.That(value.HasValue, Is.True);
        Assert.That(value.ToString(), Is.EqualTo(requestorId.ToString("N")));

        // Release the lock
        await semaphore.ReleaseLock(requestorId, JobStoreSupport.LockTriggerAccess);

        // Verify the key is deleted
        value = await db.StringGetAsync("quartz:test:lock:TestScheduler:TRIGGER_ACCESS");
        Assert.That(value.HasValue, Is.False);
    }

    [Test]
    public async Task ObtainLock_SameRequestor_ShouldReturnFalse()
    {
        Guid requestorId = Guid.NewGuid();

        bool first = await semaphore.ObtainLock(
            requestorId, null, JobStoreSupport.LockTriggerAccess);
        Assert.That(first, Is.True);

        try
        {
            // Same requestor re-acquiring should return false (already held)
            bool second = await semaphore.ObtainLock(
                requestorId, null, JobStoreSupport.LockTriggerAccess);
            Assert.That(second, Is.False);
        }
        finally
        {
            await semaphore.ReleaseLock(requestorId, JobStoreSupport.LockTriggerAccess);
        }
    }

    [Test]
    public async Task ObtainLock_DifferentRequestors_ShouldBlock()
    {
        Guid requestor1 = Guid.NewGuid();
        Guid requestor2 = Guid.NewGuid();

        bool first = await semaphore.ObtainLock(
            requestor1, null, JobStoreSupport.LockTriggerAccess);
        Assert.That(first, Is.True);

        // Second requestor should block; use a timeout to prove it blocks
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        bool second = await semaphore.ObtainLock(
            requestor2, null, JobStoreSupport.LockTriggerAccess, cts.Token);

        // Should have been cancelled because requestor1 still holds the lock
        Assert.That(second, Is.False);

        await semaphore.ReleaseLock(requestor1, JobStoreSupport.LockTriggerAccess);
    }

    [Test]
    public async Task ObtainLock_Cancelled_ShouldReturnFalse()
    {
        Guid requestor1 = Guid.NewGuid();
        Guid requestor2 = Guid.NewGuid();

        await semaphore.ObtainLock(requestor1, null, JobStoreSupport.LockTriggerAccess);

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

            bool result = await semaphore.ObtainLock(
                requestor2, null, JobStoreSupport.LockTriggerAccess, cts.Token);

            Assert.That(result, Is.False);
        }
        finally
        {
            await semaphore.ReleaseLock(requestor1, JobStoreSupport.LockTriggerAccess);
        }
    }

    [Test]
    public async Task ReleaseLock_NotOwner_ShouldNotDeleteKey()
    {
        Guid owner = Guid.NewGuid();
        Guid notOwner = Guid.NewGuid();

        await semaphore.ObtainLock(owner, null, JobStoreSupport.LockTriggerAccess);

        try
        {
            // Attempt release by non-owner should not delete the key
            await semaphore.ReleaseLock(notOwner, JobStoreSupport.LockTriggerAccess);

            // Key should still exist
            IDatabase db = redis.GetDatabase();
            RedisValue value = await db.StringGetAsync("quartz:test:lock:TestScheduler:TRIGGER_ACCESS");
            Assert.That(value.HasValue, Is.True);
        }
        finally
        {
            await semaphore.ReleaseLock(owner, JobStoreSupport.LockTriggerAccess);
        }
    }

    [Test]
    public async Task Lock_ShouldExpireAfterTtl()
    {
        var shortTtlSemaphore = new RedisSemaphore
        {
            RedisConfiguration = RedisTestEnvironment.ConnectionString,
            SchedName = "TestScheduler",
            KeyPrefix = "quartz:test:lock:",
            LockTtlMilliseconds = 2000 // 2 second TTL
        };

        Guid requestorId = Guid.NewGuid();

        await shortTtlSemaphore.ObtainLock(
            requestorId, null, JobStoreSupport.LockTriggerAccess);

        // Poll until the key expires rather than using a fixed delay (avoids flakiness on slow CI)
        IDatabase db = redis.GetDatabase();
        string redisKey = "quartz:test:lock:TestScheduler:TRIGGER_ACCESS";
        bool expired = false;
        for (int i = 0; i < 30; i++)
        {
            await Task.Delay(200);
            if (!await db.KeyExistsAsync(redisKey))
            {
                expired = true;
                break;
            }
        }

        Assert.That(expired, Is.True, "Redis lock key should have expired after TTL");

        // Release local semaphore to clean up
        await shortTtlSemaphore.ReleaseLock(requestorId, JobStoreSupport.LockTriggerAccess);
    }

    [Test]
    public async Task KeyFormat_ShouldIncludeSchedulerName()
    {
        Guid requestorId = Guid.NewGuid();

        await semaphore.ObtainLock(requestorId, null, JobStoreSupport.LockTriggerAccess);

        try
        {
            IDatabase db = redis.GetDatabase();

            // The key should be exactly {prefix}{schedName}:{lockName}
            bool exists = await db.KeyExistsAsync("quartz:test:lock:TestScheduler:TRIGGER_ACCESS");
            Assert.That(exists, Is.True);
        }
        finally
        {
            await semaphore.ReleaseLock(requestorId, JobStoreSupport.LockTriggerAccess);
        }
    }

    [Test]
    public async Task BothLockNames_ShouldWorkIndependently()
    {
        Guid requestor1 = Guid.NewGuid();
        Guid requestor2 = Guid.NewGuid();

        bool trigger = await semaphore.ObtainLock(
            requestor1, null, JobStoreSupport.LockTriggerAccess);
        bool state = await semaphore.ObtainLock(
            requestor2, null, JobStoreSupport.LockStateAccess);

        Assert.That(trigger, Is.True);
        Assert.That(state, Is.True);

        await semaphore.ReleaseLock(requestor1, JobStoreSupport.LockTriggerAccess);
        await semaphore.ReleaseLock(requestor2, JobStoreSupport.LockStateAccess);
    }

    [Test]
    public async Task TwoInstances_ShouldMutuallyExclude()
    {
        // Simulate two cluster nodes with separate RedisSemaphore instances
        var semaphore2 = new RedisSemaphore
        {
            RedisConfiguration = RedisTestEnvironment.ConnectionString,
            SchedName = "TestScheduler",
            KeyPrefix = "quartz:test:lock:"
        };

        Guid requestor1 = Guid.NewGuid();
        Guid requestor2 = Guid.NewGuid();

        // Instance 1 acquires the lock
        bool first = await semaphore.ObtainLock(
            requestor1, null, JobStoreSupport.LockTriggerAccess);
        Assert.That(first, Is.True);

        // Instance 2 should fail to acquire (Redis-level exclusion)
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        bool second = await semaphore2.ObtainLock(
            requestor2, null, JobStoreSupport.LockTriggerAccess, cts.Token);
        Assert.That(second, Is.False);

        // Release from instance 1
        await semaphore.ReleaseLock(requestor1, JobStoreSupport.LockTriggerAccess);

        // Now instance 2 should be able to acquire
        bool third = await semaphore2.ObtainLock(
            requestor2, null, JobStoreSupport.LockTriggerAccess);
        Assert.That(third, Is.True);

        await semaphore2.ReleaseLock(requestor2, JobStoreSupport.LockTriggerAccess);
    }

    [Test]
    public async Task ObtainAndRelease_MultipleTimes_ShouldWork()
    {
        Guid requestorId = Guid.NewGuid();

        for (int i = 0; i < 5; i++)
        {
            bool obtained = await semaphore.ObtainLock(
                requestorId, null, JobStoreSupport.LockTriggerAccess);
            Assert.That(obtained, Is.True, $"iteration {i}");

            await semaphore.ReleaseLock(requestorId, JobStoreSupport.LockTriggerAccess);
        }
    }
}
