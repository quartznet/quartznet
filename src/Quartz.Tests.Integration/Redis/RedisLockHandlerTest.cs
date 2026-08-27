using Quartz.Impl.AdoJobStore;
using Quartz.Extensions.Redis;

using StackExchange.Redis;

namespace Quartz.Tests.Integration.Impl.Redis;

[NonParallelizable]
[Category("db-redis")]
public class RedisLockHandlerTest
{
    private static readonly LockHandlerContext TestLockHandlerContext = new()
    {
        SchedulerName = "TestScheduler",
        InstanceId = "TestInstance",
        TablePrefix = AdoConstants.DefaultTablePrefix
    };

    private RedisLockHandler lockHandler = null!;
    private IConnectionMultiplexer redis = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        lockHandler = new RedisLockHandler
        {
            RedisConfiguration = RedisTestEnvironment.ConnectionString,
            KeyPrefix = "quartz:test:lock:"
        };
        lockHandler.Initialize(TestLockHandlerContext);

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
        Assert.That(lockHandler.RequiresConnection, Is.False);
    }

    [Test]
    public void DefaultProperties_ShouldHaveSensibleDefaults()
    {
        var sut = new RedisLockHandler();

        Assert.That(sut.RedisConfiguration, Is.EqualTo("localhost:6379"));
        Assert.That(sut.KeyPrefix, Is.EqualTo("quartz:lock:"));
        sut.LockTimeToLive.Should().Be(TimeSpan.FromSeconds(30), "the default lock TTL must survive the move from milliseconds to TimeSpan");
        sut.LockRetryInterval.Should().Be(TimeSpan.FromMilliseconds(100), "the default retry interval must survive the move from milliseconds to TimeSpan");
    }

    [Test]
    public async Task AcquireLock_ShouldAcquireAndRelease()
    {
        var requestorId = Guid.NewGuid();

        var obtained = await lockHandler.AcquireLock(
            requestorId, null, SchedulerLock.TriggerAccess);

        Assert.That(obtained, Is.True);

        var db = redis.GetDatabase();
        var value = await db.StringGetAsync("quartz:test:lock:TestScheduler:TRIGGER_ACCESS");
        Assert.That(value.HasValue, Is.True);
        Assert.That(value.ToString(), Is.EqualTo(requestorId.ToString("N")));

        await lockHandler.ReleaseLock(requestorId, SchedulerLock.TriggerAccess);

        value = await db.StringGetAsync("quartz:test:lock:TestScheduler:TRIGGER_ACCESS");
        Assert.That(value.HasValue, Is.False);
    }

    [Test]
    public async Task AcquireLock_SameRequestor_ShouldReturnFalse()
    {
        var requestorId = Guid.NewGuid();

        var first = await lockHandler.AcquireLock(
            requestorId, null, SchedulerLock.TriggerAccess);
        Assert.That(first, Is.True);

        try
        {
            var second = await lockHandler.AcquireLock(
                requestorId, null, SchedulerLock.TriggerAccess);
            Assert.That(second, Is.False);
        }
        finally
        {
            await lockHandler.ReleaseLock(requestorId, SchedulerLock.TriggerAccess);
        }
    }

    [Test]
    public async Task AcquireLock_DifferentRequestors_ShouldBlock()
    {
        var requestor1 = Guid.NewGuid();
        var requestor2 = Guid.NewGuid();

        var first = await lockHandler.AcquireLock(
            requestor1, null, SchedulerLock.TriggerAccess);
        Assert.That(first, Is.True);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var second = await lockHandler.AcquireLock(
            requestor2, null, SchedulerLock.TriggerAccess, cts.Token);

        Assert.That(second, Is.False);

        await lockHandler.ReleaseLock(requestor1, SchedulerLock.TriggerAccess);
    }

    [Test]
    public async Task AcquireLock_Cancelled_ShouldReturnFalse()
    {
        var requestor1 = Guid.NewGuid();
        var requestor2 = Guid.NewGuid();

        await lockHandler.AcquireLock(requestor1, null, SchedulerLock.TriggerAccess);

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

            var result = await lockHandler.AcquireLock(
                requestor2, null, SchedulerLock.TriggerAccess, cts.Token);

            Assert.That(result, Is.False);
        }
        finally
        {
            await lockHandler.ReleaseLock(requestor1, SchedulerLock.TriggerAccess);
        }
    }

    [Test]
    public async Task ReleaseLock_NotOwner_ShouldNotDeleteKey()
    {
        var owner = Guid.NewGuid();
        var notOwner = Guid.NewGuid();

        await lockHandler.AcquireLock(owner, null, SchedulerLock.TriggerAccess);

        try
        {
            await lockHandler.ReleaseLock(notOwner, SchedulerLock.TriggerAccess);

            var db = redis.GetDatabase();
            var value = await db.StringGetAsync("quartz:test:lock:TestScheduler:TRIGGER_ACCESS");
            Assert.That(value.HasValue, Is.True);
        }
        finally
        {
            await lockHandler.ReleaseLock(owner, SchedulerLock.TriggerAccess);
        }
    }

    [Test]
    public async Task Lock_ShouldExpireAfterTtl()
    {
        var shortTtlLockHandler = new RedisLockHandler
        {
            RedisConfiguration = RedisTestEnvironment.ConnectionString,
            KeyPrefix = "quartz:test:lock:",
            LockTimeToLive = TimeSpan.FromSeconds(2)
        };
        shortTtlLockHandler.Initialize(TestLockHandlerContext);

        var requestorId = Guid.NewGuid();

        await shortTtlLockHandler.AcquireLock(
            requestorId, null, SchedulerLock.TriggerAccess);

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

        await shortTtlLockHandler.ReleaseLock(requestorId, SchedulerLock.TriggerAccess);
    }

    [Test]
    public async Task KeyFormat_ShouldIncludeSchedulerName()
    {
        var requestorId = Guid.NewGuid();

        await lockHandler.AcquireLock(requestorId, null, SchedulerLock.TriggerAccess);

        try
        {
            var db = redis.GetDatabase();
            var exists = await db.KeyExistsAsync("quartz:test:lock:TestScheduler:TRIGGER_ACCESS");
            Assert.That(exists, Is.True);
        }
        finally
        {
            await lockHandler.ReleaseLock(requestorId, SchedulerLock.TriggerAccess);
        }
    }

    [Test]
    public async Task BothLockNames_ShouldWorkIndependently()
    {
        var requestor1 = Guid.NewGuid();
        var requestor2 = Guid.NewGuid();

        var trigger = await lockHandler.AcquireLock(
            requestor1, null, SchedulerLock.TriggerAccess);
        var state = await lockHandler.AcquireLock(
            requestor2, null, SchedulerLock.StateAccess);

        Assert.That(trigger, Is.True);
        Assert.That(state, Is.True);

        await lockHandler.ReleaseLock(requestor1, SchedulerLock.TriggerAccess);
        await lockHandler.ReleaseLock(requestor2, SchedulerLock.StateAccess);
    }

    [Test]
    public async Task TwoInstances_ShouldMutuallyExclude()
    {
        var lockHandler2 = new RedisLockHandler
        {
            RedisConfiguration = RedisTestEnvironment.ConnectionString,
            KeyPrefix = "quartz:test:lock:"
        };
        lockHandler2.Initialize(TestLockHandlerContext);

        var requestor1 = Guid.NewGuid();
        var requestor2 = Guid.NewGuid();

        var first = await lockHandler.AcquireLock(
            requestor1, null, SchedulerLock.TriggerAccess);
        Assert.That(first, Is.True);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var second = await lockHandler2.AcquireLock(
            requestor2, null, SchedulerLock.TriggerAccess, cts.Token);
        Assert.That(second, Is.False);

        await lockHandler.ReleaseLock(requestor1, SchedulerLock.TriggerAccess);

        var third = await lockHandler2.AcquireLock(
            requestor2, null, SchedulerLock.TriggerAccess);
        Assert.That(third, Is.True);

        await lockHandler2.ReleaseLock(requestor2, SchedulerLock.TriggerAccess);
    }

    [Test]
    public async Task ObtainAndRelease_MultipleTimes_ShouldWork()
    {
        var requestorId = Guid.NewGuid();

        for (var i = 0; i < 5; i++)
        {
            var obtained = await lockHandler.AcquireLock(
                requestorId, null, SchedulerLock.TriggerAccess);
            Assert.That(obtained, Is.True, $"iteration {i}");

            await lockHandler.ReleaseLock(requestorId, SchedulerLock.TriggerAccess);
        }
    }
}
