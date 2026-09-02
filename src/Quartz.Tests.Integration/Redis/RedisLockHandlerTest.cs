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

        // The lock is held, so this request never completes on its own; the token is what ends it, and
        // that end is reported as cancellation rather than as the false that means re-entry (#3583).
        Func<Task> second = async () => await lockHandler.AcquireLock(
            requestor2, null, SchedulerLock.TriggerAccess, cts.Token);

        await second.Should().ThrowAsync<OperationCanceledException>(
            "false would tell the store requestor2 already held the lock, and it would go on to write "
            + "trigger rows while requestor1 was still inside");

        await lockHandler.ReleaseLock(requestor1, SchedulerLock.TriggerAccess);
    }

    /// <summary>
    /// A waiter that gives up leaves the lock where it was and takes nothing with it, so the requestor
    /// that waits properly is still served.
    /// </summary>
    [Test]
    public async Task AcquireLock_Cancelled_ShouldLeaveTheLockWithItsOwner()
    {
        var requestor1 = Guid.NewGuid();
        var requestor2 = Guid.NewGuid();

        await lockHandler.AcquireLock(requestor1, null, SchedulerLock.TriggerAccess);

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

            Func<Task> cancelled = async () => await lockHandler.AcquireLock(
                requestor2, null, SchedulerLock.TriggerAccess, cts.Token);

            await cancelled.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            await lockHandler.ReleaseLock(requestor1, SchedulerLock.TriggerAccess);
        }

        (await lockHandler.AcquireLock(requestor2, null, SchedulerLock.TriggerAccess)).Should().BeTrue(
            "the abandoned waiter released nothing and kept nothing, so the lock is free once its owner "
            + "gives it back");

        await lockHandler.ReleaseLock(requestor2, SchedulerLock.TriggerAccess);
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

        // The second handler has a local gate of its own, so this request gets past it and is cancelled
        // inside the SET NX poll loop - the one cancellation path that needs a live server to reach. It
        // is reported as cancellation, and the gate it had taken is given back before the exception
        // escapes, which is what the third acquire below proves (#3583).
        Func<Task> second = async () => await lockHandler2.AcquireLock(
            requestor2, null, SchedulerLock.TriggerAccess, cts.Token);
        await second.Should().ThrowAsync<OperationCanceledException>();

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

    /// <summary>
    /// Against a real server: the connection the handler opened on its first lock is live, and the
    /// shutdown the job store now calls closes it. #3639 — before that hook existed nothing could, so
    /// every scheduler that ever took a Redis lock left a connection and its heartbeat behind.
    /// </summary>
    /// <remarks>
    /// A handler of its own rather than the fixture's, because closing the shared one would take the
    /// connection out from under every test that runs after this.
    /// </remarks>
    [Test]
    public async Task ShuttingDownClosesTheConnectionItOpened()
    {
        RedisLockHandler ownHandler = new()
        {
            RedisConfiguration = RedisTestEnvironment.ConnectionString,
            KeyPrefix = "quartz:test:shutdown:",
        };
        ownHandler.Initialize(TestLockHandlerContext);

        Guid requestorId = Guid.NewGuid();
        (await ownHandler.AcquireLock(requestorId, null, SchedulerLock.TriggerAccess)).Should().BeTrue();
        await ownHandler.ReleaseLock(requestorId, SchedulerLock.TriggerAccess);

        ownHandler.Connection.Should().NotBeNull("taking a lock is what opens the connection");

        IConnectionMultiplexer opened = ownHandler.Connection!;
        opened.IsConnected.Should().BeTrue("the handler talked to this server a moment ago");

        await ownHandler.Shutdown();

        opened.IsConnected.Should().BeFalse(
            "the multiplexer belongs to the handler, and a scheduler that has shut down owns nothing");
        ownHandler.Connection.Should().BeNull("the handler has let go of what it closed");
    }
}
