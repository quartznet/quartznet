using Quartz.Impl.AdoJobStore;

using StackExchange.Redis;

namespace Quartz.Tests.Integration.Impl.AdoJobStore.Redis;

[NonParallelizable]
[Category("db-redis")]
public class RedisSemaphoreTest
{
    private RedisSemaphore semaphore = null!;
    private IConnectionMultiplexer redis = null!;

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
        var db = redis.GetDatabase();
        await db.KeyDeleteAsync("quartz:test:lock:TestScheduler:TRIGGER_ACCESS");
        await db.KeyDeleteAsync("quartz:test:lock:TestScheduler:STATE_ACCESS");
    }

    [TearDown]
    public async Task TearDown()
    {
        var db = redis.GetDatabase();
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
        var requestorId = Guid.NewGuid();

        var obtained = await semaphore.ObtainLock(
            requestorId, null, JobStoreSupport.LockTriggerAccess);

        Assert.That(obtained, Is.True);

        var db = redis.GetDatabase();
        var value = await db.StringGetAsync("quartz:test:lock:TestScheduler:TRIGGER_ACCESS");
        Assert.That(value.HasValue, Is.True);
        Assert.That(value.ToString(), Is.EqualTo(requestorId.ToString("N")));

        await semaphore.ReleaseLock(requestorId, JobStoreSupport.LockTriggerAccess);

        value = await db.StringGetAsync("quartz:test:lock:TestScheduler:TRIGGER_ACCESS");
        Assert.That(value.HasValue, Is.False);
    }

    [Test]
    public async Task ObtainLock_SameRequestor_ShouldReturnFalse()
    {
        var requestorId = Guid.NewGuid();

        var first = await semaphore.ObtainLock(
            requestorId, null, JobStoreSupport.LockTriggerAccess);
        Assert.That(first, Is.True);

        try
        {
            var second = await semaphore.ObtainLock(
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
        var requestor1 = Guid.NewGuid();
        var requestor2 = Guid.NewGuid();

        var first = await semaphore.ObtainLock(
            requestor1, null, JobStoreSupport.LockTriggerAccess);
        Assert.That(first, Is.True);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var second = await semaphore.ObtainLock(
            requestor2, null, JobStoreSupport.LockTriggerAccess, cts.Token);

        Assert.That(second, Is.False);

        await semaphore.ReleaseLock(requestor1, JobStoreSupport.LockTriggerAccess);
    }

    [Test]
    public async Task ObtainLock_Cancelled_ShouldReturnFalse()
    {
        var requestor1 = Guid.NewGuid();
        var requestor2 = Guid.NewGuid();

        await semaphore.ObtainLock(requestor1, null, JobStoreSupport.LockTriggerAccess);

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

            var result = await semaphore.ObtainLock(
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
        var owner = Guid.NewGuid();
        var notOwner = Guid.NewGuid();

        await semaphore.ObtainLock(owner, null, JobStoreSupport.LockTriggerAccess);

        try
        {
            await semaphore.ReleaseLock(notOwner, JobStoreSupport.LockTriggerAccess);

            var db = redis.GetDatabase();
            var value = await db.StringGetAsync("quartz:test:lock:TestScheduler:TRIGGER_ACCESS");
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
            LockTtlMilliseconds = 2000
        };

        var requestorId = Guid.NewGuid();

        await shortTtlSemaphore.ObtainLock(
            requestorId, null, JobStoreSupport.LockTriggerAccess);

        var db = redis.GetDatabase();
        var redisKey = "quartz:test:lock:TestScheduler:TRIGGER_ACCESS";
        var expired = false;
        for (var i = 0; i < 30; i++)
        {
            await Task.Delay(200);
            if (!await db.KeyExistsAsync(redisKey))
            {
                expired = true;
                break;
            }
        }

        Assert.That(expired, Is.True, "Redis lock key should have expired after TTL");

        await shortTtlSemaphore.ReleaseLock(requestorId, JobStoreSupport.LockTriggerAccess);
    }

    [Test]
    public async Task KeyFormat_ShouldIncludeSchedulerName()
    {
        var requestorId = Guid.NewGuid();

        await semaphore.ObtainLock(requestorId, null, JobStoreSupport.LockTriggerAccess);

        try
        {
            var db = redis.GetDatabase();
            var exists = await db.KeyExistsAsync("quartz:test:lock:TestScheduler:TRIGGER_ACCESS");
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
        var requestor1 = Guid.NewGuid();
        var requestor2 = Guid.NewGuid();

        var trigger = await semaphore.ObtainLock(
            requestor1, null, JobStoreSupport.LockTriggerAccess);
        var state = await semaphore.ObtainLock(
            requestor2, null, JobStoreSupport.LockStateAccess);

        Assert.That(trigger, Is.True);
        Assert.That(state, Is.True);

        await semaphore.ReleaseLock(requestor1, JobStoreSupport.LockTriggerAccess);
        await semaphore.ReleaseLock(requestor2, JobStoreSupport.LockStateAccess);
    }

    [Test]
    public async Task TwoInstances_ShouldMutuallyExclude()
    {
        var semaphore2 = new RedisSemaphore
        {
            RedisConfiguration = RedisTestEnvironment.ConnectionString,
            SchedName = "TestScheduler",
            KeyPrefix = "quartz:test:lock:"
        };

        var requestor1 = Guid.NewGuid();
        var requestor2 = Guid.NewGuid();

        var first = await semaphore.ObtainLock(
            requestor1, null, JobStoreSupport.LockTriggerAccess);
        Assert.That(first, Is.True);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var second = await semaphore2.ObtainLock(
            requestor2, null, JobStoreSupport.LockTriggerAccess, cts.Token);
        Assert.That(second, Is.False);

        await semaphore.ReleaseLock(requestor1, JobStoreSupport.LockTriggerAccess);

        var third = await semaphore2.ObtainLock(
            requestor2, null, JobStoreSupport.LockTriggerAccess);
        Assert.That(third, Is.True);

        await semaphore2.ReleaseLock(requestor2, JobStoreSupport.LockTriggerAccess);
    }

    [Test]
    public async Task ObtainAndRelease_MultipleTimes_ShouldWork()
    {
        var requestorId = Guid.NewGuid();

        for (var i = 0; i < 5; i++)
        {
            var obtained = await semaphore.ObtainLock(
                requestorId, null, JobStoreSupport.LockTriggerAccess);
            Assert.That(obtained, Is.True, $"iteration {i}");

            await semaphore.ReleaseLock(requestorId, JobStoreSupport.LockTriggerAccess);
        }
    }
}
