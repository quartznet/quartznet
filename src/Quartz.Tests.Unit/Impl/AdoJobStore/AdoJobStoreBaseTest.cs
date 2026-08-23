using Quartz.Tests;
using System.Data.Common;
using System.Globalization;
using System.Reflection;

using FakeItEasy;

using Microsoft.Extensions.Time.Testing;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.Calendar;
using Quartz.Extensibility;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

public class AdoJobStoreBaseTest
{
    private TestAdoJobStoreBase jobStoreSupport;
    private IDriverDelegate driverDelegate;

    [SetUp]
    public void SetUp()
    {
        jobStoreSupport = new TestAdoJobStoreBase();
        driverDelegate = A.Fake<IDriverDelegate>();
        jobStoreSupport.DirectDelegate = driverDelegate;
        jobStoreSupport.DirectSignaler = A.Fake<ISchedulerSignaler>();
    }

    /// <summary>
    /// Arranges the batch read so that <paramref name="triggers" /> come back as one misfire batch.
    /// </summary>
    private void GivenMisfiredTriggers(bool hasMore = false, params IOperableTrigger[] triggers)
    {
        A.CallTo(() => driverDelegate.SelectMisfiredTriggersToRecover(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<StoredTriggerState>.Ignored,
                A<DateTimeOffset>.Ignored,
                A<int>.Ignored,
                A<CancellationToken>.Ignored))
            .Returns(new ValueTask<MisfiredTriggerBatch>(new MisfiredTriggerBatch(triggers.ToList(), hasMore)));
    }

    private static IOperableTrigger CreateMisfiredTrigger(string name, int minutesLate = 10)
    {
        IOperableTrigger trigger = CreateTestTrigger(name);
        trigger.NextFireTimeUtc = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(minutesLate);
        return trigger;
    }

    [Test]
    public async Task TestRecoverMisfiredJobs_ShouldCheckForMisfiredTriggersInStateWaiting()
    {
        GivenMisfiredTriggers();

        await jobStoreSupport.RecoverMisfiredJobs(null, false);

        A.CallTo(() => driverDelegate.SelectMisfiredTriggersToRecover(
            A<ConnectionAndTransactionHolder>.Ignored,
            A<StoredTriggerState>.That.IsEqualTo(StoredTriggerState.Waiting),
            A<DateTimeOffset>.Ignored,
            A<int>.Ignored,
            CancellationToken.None)).MustHaveHappened();
    }

    [Test]
    public async Task RecoverMisfiredJobs_ShouldReadWholeBatchInOneCall()
    {
        GivenMisfiredTriggers(false, CreateMisfiredTrigger("misfired1"), CreateMisfiredTrigger("misfired2"));

        await jobStoreSupport.RecoverMisfiredJobs(null, false);

        // Assert: the batch read replaces the per-trigger reads entirely
        A.CallTo(() => driverDelegate.SelectMisfiredTriggersToRecover(
            A<ConnectionAndTransactionHolder>.Ignored,
            A<StoredTriggerState>.Ignored,
            A<DateTimeOffset>.Ignored,
            A<int>.Ignored,
            A<CancellationToken>.Ignored)).MustHaveHappenedOnceExactly();

        A.CallTo(() => driverDelegate.SelectTrigger(
            A<ConnectionAndTransactionHolder>.Ignored,
            A<TriggerKey>.Ignored,
            A<CancellationToken>.Ignored)).MustNotHaveHappened();

        // Assert: AddTrigger path NOT taken (no TriggerExists check)
        A.CallTo(() => driverDelegate.TriggerExists(
            A<ConnectionAndTransactionHolder>.Ignored,
            A<TriggerKey>.Ignored,
            A<CancellationToken>.Ignored)).MustNotHaveHappened();
    }

    [Test]
    public async Task RecoverMisfiredJobs_ShouldWriteWholeBatchInOneCall()
    {
        GivenMisfiredTriggers(false, CreateMisfiredTrigger("misfired1"), CreateMisfiredTrigger("misfired2"));

        List<MisfiredTriggerUpdate> captured = null;
        A.CallTo(() => driverDelegate.UpdateMisfiredTriggers(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<IReadOnlyList<MisfiredTriggerUpdate>>.Ignored,
                A<CancellationToken>.Ignored))
            .Invokes((ConnectionAndTransactionHolder _, IReadOnlyList<MisfiredTriggerUpdate> updates, CancellationToken _) =>
            {
                captured = updates.ToList();
            });

        await jobStoreSupport.RecoverMisfiredJobs(null, false);

        A.CallTo(() => driverDelegate.UpdateMisfiredTriggers(
            A<ConnectionAndTransactionHolder>.Ignored,
            A<IReadOnlyList<MisfiredTriggerUpdate>>.Ignored,
            A<CancellationToken>.Ignored)).MustHaveHappenedOnceExactly();

        captured.Should().NotBeNull();
        captured.Should().HaveCount(2);
        captured.Select(x => x.Trigger.Key.Name).Should().BeEquivalentTo(["misfired1", "misfired2"]);
        captured.Should().OnlyContain(x => x.NewState == StoredTriggerState.Waiting);

        // Assert: the single-trigger write path is not used for batch recovery
        A.CallTo(() => driverDelegate.UpdateMisfiredTrigger(
            A<ConnectionAndTransactionHolder>.Ignored,
            A<IOperableTrigger>.Ignored,
            A<StoredTriggerState>.Ignored,
            A<DateTimeOffset?>.Ignored,
            A<CancellationToken>.Ignored)).MustNotHaveHappened();
    }

    [Test]
    public async Task RecoverMisfiredJobs_ShouldPropagateHasMoreToResult()
    {
        GivenMisfiredTriggers(true, CreateMisfiredTrigger("misfired1"));

        RecoverMisfiredJobsResult result = await jobStoreSupport.RecoverMisfiredJobs(null, false);

        result.HasMoreMisfiredTriggers.Should().BeTrue();
        result.ProcessedMisfiredTriggerCount.Should().Be(1);
    }

    [Test]
    public async Task RecoverMisfiredJobs_ShouldCacheCalendars_AcrossBatch()
    {
        // Disable field-level calendarCache so the test validates the batch cache,
        // not the existing GetCalendar lazy-cache (which is active when Clustered=false).
        jobStoreSupport = new TestAdoJobStoreBase(clustered: true) { DirectDelegate = driverDelegate };

        string calendarName = "shared-cal";

        IOperableTrigger trigger1 = CreateMisfiredTrigger("misfired1");
        trigger1.CalendarName = calendarName;

        IOperableTrigger trigger2 = CreateMisfiredTrigger("misfired2", minutesLate: 5);
        trigger2.CalendarName = calendarName;

        GivenMisfiredTriggers(false, trigger1, trigger2);

        A.CallTo(() => driverDelegate.SelectCalendar(
            A<ConnectionAndTransactionHolder>.Ignored,
            calendarName,
            A<CancellationToken>.Ignored))
            .Returns(new ValueTask<ICalendar>(new BaseCalendar()));

        await jobStoreSupport.RecoverMisfiredJobs(null, false);

        // Assert: calendar retrieved only once despite two triggers
        A.CallTo(() => driverDelegate.SelectCalendar(
            A<ConnectionAndTransactionHolder>.Ignored,
            calendarName,
            A<CancellationToken>.Ignored)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task TestRemoveJob_ShouldDeleteFiredTriggersForJobKey()
    {
        var jobKey = new JobKey("testJob", "testGroup");
        var conn = new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);

        // No triggers exist in QRTZ_TRIGGERS for this job
        A.CallTo(() => driverDelegate.SelectTriggerKeysForJob(
            A<ConnectionAndTransactionHolder>.Ignored,
            jobKey,
            A<CancellationToken>.Ignored)).Returns(new ValueTask<List<TriggerKey>>(new List<TriggerKey>()));

        A.CallTo(() => driverDelegate.DeleteFiredTriggers(
            A<ConnectionAndTransactionHolder>.Ignored,
            A<FiredTriggerQuery>.That.Matches(x => x.Job == jobKey),
            A<CancellationToken>.Ignored)).Returns(new ValueTask<int>(0));

        A.CallTo(() => driverDelegate.DeleteJobDetail(
            A<ConnectionAndTransactionHolder>.Ignored,
            jobKey,
            A<CancellationToken>.Ignored)).Returns(new ValueTask<int>(1));

        // Act
        await jobStoreSupport.CallRemoveJob(conn, jobKey);

        // Assert: fired triggers for this job key should be cleaned up
        A.CallTo(() => driverDelegate.DeleteFiredTriggers(
            A<ConnectionAndTransactionHolder>.Ignored,
            A<FiredTriggerQuery>.That.Matches(x => x.Job == jobKey && x.Trigger == null && x.InstanceId == null),
            A<CancellationToken>.Ignored)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task TestExecuteInLocalTransactionLock_RetriesOnTransientException()
    {
        int callCount = 0;
        var store = CreateRetryTestStore();

        // Callback fails with transient exception on first call, succeeds on second
        string result = await store.CallExecuteInLocalTransactionLock<string>(conn =>
        {
            callCount++;
            if (callCount == 1)
            {
                throw new JobPersistenceException("transient", new TransientTestException());
            }
            return new ValueTask<string>("success");
        }, CancellationToken.None);

        result.Should().Be("success");
        callCount.Should().Be(2);
    }

    [Test]
    public async Task TestExecuteInLocalTransactionLock_StopsRetryingAfterMaxRetries()
    {
        int callCount = 0;
        var store = CreateRetryTestStore(maxTransientRetries: 2);

        // Callback always throws transient exception
        Func<Task> act = async () => await store.CallExecuteInLocalTransactionLock<string>(conn =>
        {
            callCount++;
            throw new JobPersistenceException("transient", new TransientTestException());
        }, CancellationToken.None);

        await act.Should().ThrowAsync<JobPersistenceException>();
        // Initial attempt + 2 retries = 3 total
        callCount.Should().Be(3);
    }

    [Test]
    public async Task TestExecuteInLocalTransactionLock_DoesNotRetryNonTransientException()
    {
        int callCount = 0;
        var store = CreateRetryTestStore();

        // Non-transient exception should not be retried
        Func<Task> act = async () => await store.CallExecuteInLocalTransactionLock<string>(conn =>
        {
            callCount++;
            throw new JobPersistenceException("non-transient");
        }, CancellationToken.None);

        await act.Should().ThrowAsync<JobPersistenceException>().WithMessage("non-transient");
        callCount.Should().Be(1);
    }

    [Test]
    public async Task TestExecuteInLocalTransactionLock_NoRetryWhenMaxTransientRetriesIsZero()
    {
        int callCount = 0;
        var store = CreateRetryTestStore(maxTransientRetries: 0);

        // With MaxTransientRetries = 0, transient exceptions should not be retried
        Func<Task> act = async () => await store.CallExecuteInLocalTransactionLock<string>(conn =>
        {
            callCount++;
            throw new JobPersistenceException("transient", new TransientTestException());
        }, CancellationToken.None);

        await act.Should().ThrowAsync<JobPersistenceException>();
        callCount.Should().Be(1);
    }

    [Test]
    public async Task TriggerFired_ReturnsNull_WhenDisallowConcurrentJobAlreadyExecuting()
    {
        ConnectionAndTransactionHolder conn = new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);
        IOperableTrigger trigger = CreateTestTrigger();
        IJobDetail job = CreateDisallowConcurrentJob();

        GivenAcquiredTrigger(conn, trigger);
        A.CallTo(() => driverDelegate.SelectJobDetail(conn, trigger.JobKey, A<Extensibility.ITypeLoader>.Ignored, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<IJobDetail>(job));
        A.CallTo(() => driverDelegate.IsJobCurrentlyExecuting(conn, trigger.JobKey, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<bool>(true));

        TriggerFiredBundle result = await jobStoreSupport.CallTriggerFired(conn, trigger);

        result.Should().BeNull();
        A.CallTo(() => driverDelegate.ApplyTriggerFired(
            A<ConnectionAndTransactionHolder>.Ignored,
            A<TriggerFiredUpdate>.Ignored,
            A<CancellationToken>.Ignored)).MustNotHaveHappened();
    }

    [Test]
    public async Task TriggerFired_Proceeds_WhenDisallowConcurrentJobNotExecuting()
    {
        ConnectionAndTransactionHolder conn = new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);
        IOperableTrigger trigger = CreateTestTrigger();
        IJobDetail job = CreateDisallowConcurrentJob();

        GivenAcquiredTrigger(conn, trigger);
        A.CallTo(() => driverDelegate.SelectJobDetail(conn, trigger.JobKey, A<Extensibility.ITypeLoader>.Ignored, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<IJobDetail>(job));
        A.CallTo(() => driverDelegate.IsJobCurrentlyExecuting(conn, trigger.JobKey, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<bool>(false));

        TriggerFiredBundle result = await jobStoreSupport.CallTriggerFired(conn, trigger);

        A.CallTo(() => driverDelegate.ApplyTriggerFired(
                conn,
                A<TriggerFiredUpdate>.That.Matches(x => ReferenceEquals(x.Trigger, trigger) && ReferenceEquals(x.JobDetail, job) && x.BlockJobTriggers),
                A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task TriggerFired_SkipsConcurrencyCheck_WhenConcurrentExecutionAllowed()
    {
        ConnectionAndTransactionHolder conn = new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);
        IOperableTrigger trigger = CreateTestTrigger();
        IJobDetail job = CreateConcurrentJob();

        GivenAcquiredTrigger(conn, trigger);
        A.CallTo(() => driverDelegate.SelectJobDetail(conn, trigger.JobKey, A<Extensibility.ITypeLoader>.Ignored, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<IJobDetail>(job));

        await jobStoreSupport.CallTriggerFired(conn, trigger);

        A.CallTo(() => driverDelegate.IsJobCurrentlyExecuting(
            A<ConnectionAndTransactionHolder>.Ignored,
            A<JobKey>.Ignored,
            A<CancellationToken>.Ignored)).MustNotHaveHappened();
    }

    /// <summary>
    /// Everything a plain fire reads, in the calls it takes to read it. The header carries the state, the
    /// existence and the type discriminator, so the existence probe and the type lookup that used to
    /// follow it are gone, and the write is one call rather than the store's general storing path.
    /// </summary>
    [Test]
    public async Task TriggerFired_ReadsTheTriggerRowOnceAndWritesInOneCall()
    {
        ConnectionAndTransactionHolder conn = new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);
        IOperableTrigger trigger = CreateTestTrigger();
        IJobDetail job = CreateConcurrentJob();

        GivenAcquiredTrigger(conn, trigger);
        A.CallTo(() => driverDelegate.SelectJobDetail(conn, trigger.JobKey, A<Extensibility.ITypeLoader>.Ignored, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<IJobDetail>(job));

        await jobStoreSupport.CallTriggerFired(conn, trigger);

        A.CallTo(() => driverDelegate.SelectTriggerHeader(conn, trigger.Key, A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => driverDelegate.SelectJobDetail(conn, trigger.JobKey, A<Extensibility.ITypeLoader>.Ignored, A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => driverDelegate.ApplyTriggerFired(conn, A<TriggerFiredUpdate>.Ignored, A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();

        A.CallTo(() => driverDelegate.SelectTriggerState(A<ConnectionAndTransactionHolder>.Ignored, A<TriggerKey>.Ignored, A<CancellationToken>.Ignored))
            .MustNotHaveHappened();
        A.CallTo(() => driverDelegate.TriggerExists(A<ConnectionAndTransactionHolder>.Ignored, A<TriggerKey>.Ignored, A<CancellationToken>.Ignored))
            .MustNotHaveHappened();
        // A fire that forces the state it stores cannot have that state rewritten by a pause, so the
        // paused-group probe is not consulted at all.
        A.CallTo(() => driverDelegate.IsTriggerGroupPaused(A<ConnectionAndTransactionHolder>.Ignored, A<string>.Ignored, A<CancellationToken>.Ignored))
            .MustNotHaveHappened();
        A.CallTo(() => driverDelegate.UpdateFiredTrigger(A<ConnectionAndTransactionHolder>.Ignored, A<IOperableTrigger>.Ignored, A<StoredTriggerState>.Ignored, A<IJobDetail>.Ignored, A<CancellationToken>.Ignored))
            .MustNotHaveHappened();
        A.CallTo(() => driverDelegate.UpdateTrigger(A<ConnectionAndTransactionHolder>.Ignored, A<IOperableTrigger>.Ignored, A<StoredTriggerState>.Ignored, A<IJobDetail>.Ignored, A<CancellationToken>.Ignored))
            .MustNotHaveHappened();
    }

    /// <summary>
    /// The trigger's next fire time as it stood before the fire — not the one the fire advanced it to —
    /// is what the fired-trigger row has to say the fire was scheduled for.
    /// </summary>
    [Test]
    public async Task TriggerFired_CarriesTheScheduledFireTimeFromBeforeTheTriggerAdvanced()
    {
        ConnectionAndTransactionHolder conn = new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);
        IOperableTrigger trigger = CreateTestTrigger();
        IJobDetail job = CreateConcurrentJob();
        DateTimeOffset? scheduled = trigger.NextFireTimeUtc;

        GivenAcquiredTrigger(conn, trigger);
        A.CallTo(() => driverDelegate.SelectJobDetail(conn, trigger.JobKey, A<Extensibility.ITypeLoader>.Ignored, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<IJobDetail>(job));

        await jobStoreSupport.CallTriggerFired(conn, trigger);

        A.CallTo(() => driverDelegate.ApplyTriggerFired(
                conn,
                A<TriggerFiredUpdate>.That.Matches(x => x.ScheduledFireTimeUtc == scheduled && x.StoredTriggerType == AdoConstants.TriggerTypeSimple),
                A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();

        trigger.NextFireTimeUtc.Should().NotBe(scheduled,
            "the trigger has moved on to its next fire by the time the write goes out, which is exactly why the scheduled time travels separately");
    }

    [TestCase(StoredTriggerState.Paused)]
    [TestCase(StoredTriggerState.Blocked)]
    [TestCase(StoredTriggerState.PausedBlocked)]
    [TestCase(StoredTriggerState.Waiting)]
    [TestCase(StoredTriggerState.Complete)]
    [TestCase(StoredTriggerState.Error)]
    public async Task StoreCalendar_PreservesTriggerState_WhenUpdatingTriggers(StoredTriggerState originalState)
    {
        // Arrange
        var conn = new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);
        var calendarName = "testCal";
        ICalendar calendar = new BaseCalendar();
        var triggerKey = new TriggerKey("t1", "g1");
        var jobKey = new JobKey("j1", "jg1");

        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .StartNow()
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build();

        IJobDetail job = JobBuilder.Create<ConcurrentTestJob>()
            .WithIdentity(jobKey)
            .Build();

        A.CallTo(() => driverDelegate.CalendarExists(conn, calendarName, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<bool>(true));
        A.CallTo(() => driverDelegate.UpdateCalendar(conn, calendarName, calendar, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<int>(1));
        A.CallTo(() => driverDelegate.SelectTriggersForCalendar(conn, calendarName, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<List<IOperableTrigger>>(new List<IOperableTrigger> { trigger }));
        A.CallTo(() => driverDelegate.SelectTriggerState(conn, triggerKey, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<StoredTriggerState>(originalState));
        A.CallTo(() => driverDelegate.TriggerExists(conn, triggerKey, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<bool>(true));
        A.CallTo(() => driverDelegate.SelectJobDetail(conn, jobKey, A<ITypeLoader>.Ignored, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<IJobDetail>(job));

        // Act
        await jobStoreSupport.CallAddCalendar(conn, calendarName, calendar, replace: true, updateTriggers: true);

        // Assert: UpdateTrigger should be called with the original state preserved
        A.CallTo(() => driverDelegate.UpdateTrigger(conn, trigger, originalState, job, A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task StoreCalendar_SkipsDeletedTriggers_WhenUpdatingTriggers()
    {
        // Arrange
        var conn = new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);
        var calendarName = "testCal";
        ICalendar calendar = new BaseCalendar();
        var triggerKey = new TriggerKey("t1", "g1");
        var jobKey = new JobKey("j1", "jg1");

        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .StartNow()
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build();

        A.CallTo(() => driverDelegate.CalendarExists(conn, calendarName, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<bool>(true));
        A.CallTo(() => driverDelegate.UpdateCalendar(conn, calendarName, calendar, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<int>(1));
        A.CallTo(() => driverDelegate.SelectTriggersForCalendar(conn, calendarName, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<List<IOperableTrigger>>(new List<IOperableTrigger> { trigger }));
        A.CallTo(() => driverDelegate.SelectTriggerState(conn, triggerKey, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<StoredTriggerState>(StoredTriggerState.Deleted));

        // Act
        await jobStoreSupport.CallAddCalendar(conn, calendarName, calendar, replace: true, updateTriggers: true);

        // Assert: trigger in DELETED state should be skipped entirely
        A.CallTo(() => driverDelegate.UpdateTrigger(
            A<ConnectionAndTransactionHolder>.Ignored,
            A<IOperableTrigger>.Ignored,
            A<StoredTriggerState>.Ignored,
            A<IJobDetail>.Ignored,
            A<CancellationToken>.Ignored)).MustNotHaveHappened();
        A.CallTo(() => driverDelegate.TriggerExists(
            A<ConnectionAndTransactionHolder>.Ignored,
            A<TriggerKey>.Ignored,
            A<CancellationToken>.Ignored)).MustNotHaveHappened();
    }

    [Test]
    public async Task StoreCalendar_PreservesMixedTriggerStates_WhenUpdatingTriggers()
    {
        // Arrange: two triggers on same calendar, one paused and one waiting
        var conn = new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);
        var calendarName = "testCal";
        ICalendar calendar = new BaseCalendar();
        var jobKey = new JobKey("j1", "jg1");

        var pausedTriggerKey = new TriggerKey("paused", "g1");
        IOperableTrigger pausedTrigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(pausedTriggerKey)
            .ForJob(jobKey)
            .StartNow()
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build();

        var waitingTriggerKey = new TriggerKey("waiting", "g1");
        IOperableTrigger waitingTrigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(waitingTriggerKey)
            .ForJob(jobKey)
            .StartNow()
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build();

        IJobDetail job = JobBuilder.Create<ConcurrentTestJob>()
            .WithIdentity(jobKey)
            .Build();

        A.CallTo(() => driverDelegate.CalendarExists(conn, calendarName, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<bool>(true));
        A.CallTo(() => driverDelegate.UpdateCalendar(conn, calendarName, calendar, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<int>(1));
        A.CallTo(() => driverDelegate.SelectTriggersForCalendar(conn, calendarName, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<List<IOperableTrigger>>(new List<IOperableTrigger> { pausedTrigger, waitingTrigger }));

        A.CallTo(() => driverDelegate.SelectTriggerState(conn, pausedTriggerKey, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<StoredTriggerState>(StoredTriggerState.Paused));
        A.CallTo(() => driverDelegate.SelectTriggerState(conn, waitingTriggerKey, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<StoredTriggerState>(StoredTriggerState.Waiting));

        A.CallTo(() => driverDelegate.TriggerExists(conn, A<TriggerKey>.Ignored, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<bool>(true));
        A.CallTo(() => driverDelegate.SelectJobDetail(conn, jobKey, A<ITypeLoader>.Ignored, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<IJobDetail>(job));

        // Act
        await jobStoreSupport.CallAddCalendar(conn, calendarName, calendar, replace: true, updateTriggers: true);

        // Assert: each trigger should be stored with its own original state
        A.CallTo(() => driverDelegate.UpdateTrigger(conn, pausedTrigger, StoredTriggerState.Paused, job, A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => driverDelegate.UpdateTrigger(conn, waitingTrigger, StoredTriggerState.Waiting, job, A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();
    }

    /// <summary>
    /// Arranges the one row read the fire path makes before it decides anything. It carries the state,
    /// the trigger's existence and its type discriminator, which used to be three separate reads.
    /// </summary>
    private void GivenAcquiredTrigger(ConnectionAndTransactionHolder conn, IOperableTrigger trigger)
    {
        A.CallTo(() => driverDelegate.SelectTriggerHeader(conn, trigger.Key, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<StoredTriggerHeader>(StoredHeader(trigger, StoredTriggerState.Acquired)));
    }

    private static StoredTriggerHeader StoredHeader(IOperableTrigger trigger, StoredTriggerState state)
    {
        return new StoredTriggerHeader(trigger.Key, trigger.JobKey, state, trigger.NextFireTimeUtc, AdoConstants.TriggerTypeSimple);
    }

    private static IOperableTrigger CreateTestTrigger(string name = "t1", string fireInstanceId = "test-fire-id")
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(name, "g1")
            .ForJob("j1", "jg1")
            .StartNow()
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build();
        trigger.FireInstanceId = fireInstanceId;
        return trigger;
    }

    private static IJobDetail CreateDisallowConcurrentJob()
    {
        return JobBuilder.Create<DisallowConcurrentTestJob>()
            .WithIdentity("j1", "jg1")
            .Build();
    }

    private static IJobDetail CreateConcurrentJob()
    {
        return JobBuilder.Create<ConcurrentTestJob>()
            .WithIdentity("j1", "jg1")
            .Build();
    }

    [Test]
    public async Task StoreTrigger_PreservesPreviousFireTimeUtc_WhenReplacingExistingTrigger()
    {
        var triggerKey = new TriggerKey("t1", "g1");
        var jobKey = new JobKey("j1", "jg1");
        var conn = new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);
        DateTimeOffset previousFireTime = new DateTimeOffset(2024, 6, 15, 10, 0, 0, TimeSpan.Zero);

        IJobDetail job = JobBuilder.Create<ConcurrentTestJob>()
            .WithIdentity(jobKey)
            .Build();

        IOperableTrigger newTrigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .StartNow()
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build();

        IOperableTrigger existingTrigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .StartNow()
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build();
        existingTrigger.PreviousFireTimeUtc = previousFireTime;

        A.CallTo(() => driverDelegate.TriggerExists(conn, triggerKey, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<bool>(true));

        A.CallTo(() => driverDelegate.IsTriggerGroupPaused(conn, A<string>.Ignored, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<bool>(false));

        A.CallTo(() => driverDelegate.SelectTrigger(conn, triggerKey, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<IOperableTrigger>(existingTrigger));

        await jobStoreSupport.CallAddTrigger(conn, newTrigger, job, replace: true);

        newTrigger.PreviousFireTimeUtc.Should().Be(previousFireTime,
            "PreviousFireTimeUtc should be preserved from the existing trigger when replacing (#1834)");

        A.CallTo(() => driverDelegate.UpdateTrigger(conn, newTrigger, A<StoredTriggerState>.Ignored, job, A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task StoreTrigger_DoesNotOverridePreviousFireTimeUtc_WhenNewTriggerAlreadyHasIt()
    {
        var triggerKey = new TriggerKey("t1", "g1");
        var jobKey = new JobKey("j1", "jg1");
        var conn = new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);
        DateTimeOffset newPreviousFireTime = new DateTimeOffset(2024, 7, 1, 12, 0, 0, TimeSpan.Zero);

        IJobDetail job = JobBuilder.Create<ConcurrentTestJob>()
            .WithIdentity(jobKey)
            .Build();

        IOperableTrigger newTrigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .StartNow()
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build();
        newTrigger.PreviousFireTimeUtc = newPreviousFireTime;

        A.CallTo(() => driverDelegate.TriggerExists(conn, triggerKey, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<bool>(true));

        A.CallTo(() => driverDelegate.IsTriggerGroupPaused(conn, A<string>.Ignored, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<bool>(false));

        await jobStoreSupport.CallAddTrigger(conn, newTrigger, job, replace: true);

        newTrigger.PreviousFireTimeUtc.Should().Be(newPreviousFireTime);

        A.CallTo(() => driverDelegate.SelectTrigger(conn, triggerKey, A<CancellationToken>.Ignored))
            .MustNotHaveHappened();
    }

    [DisallowConcurrentExecution]
    private sealed class DisallowConcurrentTestJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    private sealed class ConcurrentTestJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    [Test]
    public async Task RecoverStaleAcquiredTriggers_ShouldRecoverTriggersStuckInAcquiredState()
    {
        var conn = new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);
        var triggerKey = new TriggerKey("staleTrigger", "group");

        var staleRecord = new FiredTriggerRecord
        {
            FireInstanceId = "entry_stale_1",
            FireInstanceState = StoredTriggerState.Acquired,
            FireTimestamp = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10),
            TriggerKey = triggerKey,
            SchedulerInstanceId = "TestInstanceId"
        };

        A.CallTo(() => driverDelegate.SelectFiredTriggerRecords(
            A<ConnectionAndTransactionHolder>.Ignored,
            A<FiredTriggerQuery>.Ignored,
            A<CancellationToken>.Ignored))
            .Returns(new ValueTask<List<FiredTriggerRecord>>(new List<FiredTriggerRecord> { staleRecord }));

        int recovered = await jobStoreSupport.CallRecoverStaleAcquiredTriggers(conn);

        recovered.Should().Be(1);

        A.CallTo(() => driverDelegate.UpdateTriggerStateFromOtherState(
            A<ConnectionAndTransactionHolder>.Ignored,
            triggerKey,
            StoredTriggerState.Waiting,
            StoredTriggerState.Acquired,
            A<CancellationToken>.Ignored)).MustHaveHappenedOnceExactly();

        // Should also update from BLOCKED→WAITING to mirror ReleaseAcquiredTrigger
        A.CallTo(() => driverDelegate.UpdateTriggerStateFromOtherState(
            A<ConnectionAndTransactionHolder>.Ignored,
            triggerKey,
            StoredTriggerState.Waiting,
            StoredTriggerState.Blocked,
            A<CancellationToken>.Ignored)).MustHaveHappenedOnceExactly();

        A.CallTo(() => driverDelegate.DeleteFiredTrigger(
            A<ConnectionAndTransactionHolder>.Ignored,
            "entry_stale_1",
            A<CancellationToken>.Ignored)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task RecoverStaleAcquiredTriggers_ShouldMixRecoverableAndNonRecoverableRecords()
    {
        var conn = new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);
        var staleTrigger = new TriggerKey("staleTrigger", "group");
        var executingTrigger = new TriggerKey("executingTrigger", "group");
        var recentTrigger = new TriggerKey("recentTrigger", "group");

        var records = new[]
        {
            new FiredTriggerRecord
            {
                FireInstanceId = "entry_stale",
                FireInstanceState = StoredTriggerState.Acquired,
                FireTimestamp = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10),
                TriggerKey = staleTrigger,
                SchedulerInstanceId = "TestInstanceId",
            },
            new FiredTriggerRecord
            {
                FireInstanceId = "entry_executing",
                FireInstanceState = StoredTriggerState.Executing,
                FireTimestamp = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10),
                TriggerKey = executingTrigger,
                SchedulerInstanceId = "TestInstanceId",
            },
            new FiredTriggerRecord
            {
                FireInstanceId = "entry_recent",
                FireInstanceState = StoredTriggerState.Acquired,
                FireTimestamp = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(10),
                TriggerKey = recentTrigger,
                SchedulerInstanceId = "TestInstanceId",
            },
        };

        A.CallTo(() => driverDelegate.SelectFiredTriggerRecords(
            A<ConnectionAndTransactionHolder>.Ignored,
            A<FiredTriggerQuery>.Ignored,
            A<CancellationToken>.Ignored))
            .Returns(new ValueTask<List<FiredTriggerRecord>>(new List<FiredTriggerRecord>(records)));

        int recovered = await jobStoreSupport.CallRecoverStaleAcquiredTriggers(conn);

        recovered.Should().Be(1, "only the stale ACQUIRED trigger should be recovered");

        A.CallTo(() => driverDelegate.UpdateTriggerStateFromOtherState(
            A<ConnectionAndTransactionHolder>.Ignored,
            staleTrigger,
            StoredTriggerState.Waiting,
            StoredTriggerState.Acquired,
            A<CancellationToken>.Ignored)).MustHaveHappenedOnceExactly();

        // Should also update from BLOCKED→WAITING to mirror ReleaseAcquiredTrigger
        A.CallTo(() => driverDelegate.UpdateTriggerStateFromOtherState(
            A<ConnectionAndTransactionHolder>.Ignored,
            staleTrigger,
            StoredTriggerState.Waiting,
            StoredTriggerState.Blocked,
            A<CancellationToken>.Ignored)).MustHaveHappenedOnceExactly();

        A.CallTo(() => driverDelegate.DeleteFiredTrigger(
            A<ConnectionAndTransactionHolder>.Ignored,
            "entry_stale",
            A<CancellationToken>.Ignored)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task RecoverStaleAcquiredTriggers_ShouldNotRecoverRecentlyAcquiredTriggers()
    {
        var conn = new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);

        var recentRecord = new FiredTriggerRecord
        {
            FireInstanceId = "entry_recent_1",
            FireInstanceState = StoredTriggerState.Acquired,
            FireTimestamp = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(10),
            TriggerKey = new TriggerKey("recentTrigger", "group"),
            SchedulerInstanceId = "TestInstanceId"
        };

        A.CallTo(() => driverDelegate.SelectFiredTriggerRecords(
            A<ConnectionAndTransactionHolder>.Ignored,
            A<FiredTriggerQuery>.Ignored,
            A<CancellationToken>.Ignored))
            .Returns(new ValueTask<List<FiredTriggerRecord>>(new List<FiredTriggerRecord> { recentRecord }));

        int recovered = await jobStoreSupport.CallRecoverStaleAcquiredTriggers(conn);

        recovered.Should().Be(0);

        A.CallTo(() => driverDelegate.UpdateTriggerStateFromOtherState(
            A<ConnectionAndTransactionHolder>.Ignored,
            A<TriggerKey>.Ignored,
            A<StoredTriggerState>.Ignored,
            A<StoredTriggerState>.Ignored,
            A<CancellationToken>.Ignored)).MustNotHaveHappened();
    }

    [Test]
    public async Task RecoverStaleAcquiredTriggers_ShouldNotRecoverExecutingTriggers()
    {
        var conn = new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);

        var executingRecord = new FiredTriggerRecord
        {
            FireInstanceId = "entry_exec_1",
            FireInstanceState = StoredTriggerState.Executing,
            FireTimestamp = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10),
            TriggerKey = new TriggerKey("executingTrigger", "group"),
            SchedulerInstanceId = "TestInstanceId"
        };

        A.CallTo(() => driverDelegate.SelectFiredTriggerRecords(
            A<ConnectionAndTransactionHolder>.Ignored,
            A<FiredTriggerQuery>.Ignored,
            A<CancellationToken>.Ignored))
            .Returns(new ValueTask<List<FiredTriggerRecord>>(new List<FiredTriggerRecord> { executingRecord }));

        int recovered = await jobStoreSupport.CallRecoverStaleAcquiredTriggers(conn);

        recovered.Should().Be(0);
    }

    [Test]
    public async Task QueryTriggerGroups_ShouldDelegateToDriverDelegate()
    {
        var conn = new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);
        TriggerGroupQuery query = new() { Paused = true };

        A.CallTo(() => driverDelegate.SelectTriggerGroups(conn, query, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<PagedResult<TriggerGroup>>(new PagedResult<TriggerGroup>([new TriggerGroup("pausedGroup", true)], false)));

        PagedResult<TriggerGroup> result = await jobStoreSupport.CallQueryTriggerGroups(conn, query);

        result.Items.Select(x => x.Name).Should().Equal(["pausedGroup"]);
    }

    [Test]
    public async Task QueryTriggerGroups_ShouldReportNoGroups_WhenNoneArePaused()
    {
        var conn = new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);
        TriggerGroupQuery query = new() { Paused = true };

        A.CallTo(() => driverDelegate.SelectTriggerGroups(conn, query, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<PagedResult<TriggerGroup>>(new PagedResult<TriggerGroup>([], false)));

        PagedResult<TriggerGroup> result = await jobStoreSupport.CallQueryTriggerGroups(conn, query);

        result.Items.Should().BeEmpty();
    }

    [Test]
    public async Task QueryTriggerGroups_ShouldWrapException_InJobPersistenceException()
    {
        var conn = new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);
        TriggerGroupQuery query = new() { Paused = true };

        A.CallTo(() => driverDelegate.SelectTriggerGroups(conn, query, A<CancellationToken>.Ignored))
            .Throws(new Exception("db error"));

        Func<Task> act = async () => await jobStoreSupport.CallQueryTriggerGroups(conn, query);

        await act.Should().ThrowAsync<JobPersistenceException>()
            .WithMessage("*trigger groups*")
            .WithInnerException<JobPersistenceException, Exception>()
            .WithMessage("db error");
    }

    private static RetryTestAdoJobStoreBase CreateRetryTestStore(int maxTransientRetries = 3)
    {
        return new RetryTestAdoJobStoreBase(maxTransientRetries);
    }

    public class TestAdoJobStoreBase : AdoJobStoreBase
    {

    public TestAdoJobStoreBase(bool clustered = false, TimeProvider timeProvider = null)
        : base(TestJobStores.Signaler(), TestJobStores.TypeLoader(), timeProvider ?? TimeProvider.System, TestJobStores.SchedulerOptions(), TestJobStores.StoreOptions(), TestJobStores.ClusteringOptions(configure: options => options.Enabled = clustered), TestJobStores.Serializer(), TestJobStores.DbProvider(), TestJobStores.DriverDelegate(), TestJobStores.LockHandler())
    {
    }
        protected override ValueTask<ConnectionAndTransactionHolder> GetLocalTransactionConnection(CancellationToken cancellationToken = default)
        {
            return new ValueTask<ConnectionAndTransactionHolder>(new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null));
        }

        protected override ValueTask<T> ExecuteInLock<T>(
            SchedulerLock? lockKind,
            Func<ConnectionAndTransactionHolder, ValueTask<T>> txCallback,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<T>(default(T));
        }

        /// <summary>
        /// sets delegate directly
        /// </summary>
        internal IDriverDelegate DirectDelegate
        {
            set
            {
                FieldInfo fieldInfo = typeof(AdoJobStoreBase).GetField("driverDelegate", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(fieldInfo, Is.Not.Null);
                fieldInfo.SetValue(this, value);
            }
        }

        internal ISchedulerSignaler DirectSignaler
        {
            set
            {
                FieldInfo fieldInfo = typeof(AdoJobStoreBase).GetField("schedSignaler", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(fieldInfo, Is.Not.Null);
                fieldInfo.SetValue(this, value);
            }
        }

        internal ValueTask<bool> CallRemoveJob(ConnectionAndTransactionHolder conn, JobKey jobKey)
        {
            return DeleteJob(conn, jobKey, true, CancellationToken.None);
        }

        internal ValueTask<TriggerFiredBundle> CallTriggerFired(ConnectionAndTransactionHolder conn, IOperableTrigger trigger)
        {
            return TriggerFired(conn, trigger, CancellationToken.None);
        }

        internal ValueTask CallAddTrigger(
            ConnectionAndTransactionHolder conn,
            IOperableTrigger newTrigger,
            IJobDetail job,
            bool replace)
        {
            return AddTrigger(conn, newTrigger, job, replace, StoredTriggerState.Waiting, false, false, CancellationToken.None);
        }

        internal ValueTask<int> CallRecoverStaleAcquiredTriggers(ConnectionAndTransactionHolder conn)
        {
            return RecoverStaleAcquiredTriggers(conn, CancellationToken.None);
        }

        internal ValueTask CallAddCalendar(
            ConnectionAndTransactionHolder conn,
            string calendarName,
            ICalendar calendar,
            bool replace,
            bool updateTriggers)
        {
            return AddCalendar(
                conn,
                calendarName,
                calendar,
                new AddCalendarOptions { Replace = replace, UpdateTriggers = updateTriggers },
                CancellationToken.None);
        }

        internal ValueTask<PagedResult<TriggerGroup>> CallQueryTriggerGroups(ConnectionAndTransactionHolder conn, TriggerGroupQuery query)
        {
            return QueryTriggerGroups(conn, query, CancellationToken.None);
        }

        /// <summary>
        /// Writes the private flag that tells the check-in path this is the node's first pass, which is
        /// what gates self-recovery and the orphan sweep in <see cref="AdoJobStoreBase.FindFailedInstances" />.
        /// </summary>
        internal void SetFirstCheckIn(bool value)
        {
            FieldInfo fieldInfo = typeof(AdoJobStoreBase).GetField("firstCheckIn", BindingFlags.Instance | BindingFlags.NonPublic);
            fieldInfo.Should().NotBeNull("the first-check-in branches of FindFailedInstances are gated on that field");
            fieldInfo.SetValue(this, value);
        }

        internal ValueTask<List<SchedulerStateRecord>> CallFindFailedInstances(ConnectionAndTransactionHolder conn)
        {
            return FindFailedInstances(conn, CancellationToken.None);
        }

        internal DateTimeOffset CallCalcFailedIfAfter(SchedulerStateRecord rec)
        {
            return CalcFailedIfAfter(rec);
        }

        internal ValueTask<List<SchedulerStateRecord>> CallClusterCheckIn(ConnectionAndTransactionHolder conn)
        {
            return ClusterCheckIn(conn, CancellationToken.None);
        }

        internal ValueTask CallClusterRecover(ConnectionAndTransactionHolder conn, IReadOnlyCollection<SchedulerStateRecord> failedInstances)
        {
            return ClusterRecover(conn, failedInstances, CancellationToken.None);
        }
    }

    /// <summary>
    /// A <see cref="AdoJobStoreBase"/> subclass used to test retry logic in
    /// <see cref="AdoJobStoreBase.ExecuteInLocalTransactionLock{T}"/>.
    /// </summary>
    public sealed class RetryTestAdoJobStoreBase : AdoJobStoreBase
    {
        public RetryTestAdoJobStoreBase(int maxTransientRetries = 3)
            : base(TestJobStores.Signaler(), TestJobStores.TypeLoader(), TimeProvider.System, TestJobStores.SchedulerOptions(), TestJobStores.StoreOptions(configure: options =>
            {
                options.MaxTransientRetries = maxTransientRetries;
                options.TransientRetryInterval = TimeSpan.Zero;
            }), TestJobStores.ClusteringOptions(), TestJobStores.Serializer(), TestJobStores.DbProvider(), TestJobStores.DriverDelegate(), TestJobStores.LockHandler())
        {
        }

        protected override ValueTask<ConnectionAndTransactionHolder> GetLocalTransactionConnection(CancellationToken cancellationToken = default)
        {
            // Return a holder with a mock connection and no transaction
            return new ValueTask<ConnectionAndTransactionHolder>(
                new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null));
        }

        protected override ValueTask<T> ExecuteInLock<T>(
            SchedulerLock? lockKind,
            Func<ConnectionAndTransactionHolder, ValueTask<T>> txCallback,
            CancellationToken cancellationToken = default)
        {
            return ExecuteInLocalTransactionLock(lockKind, txCallback, cancellationToken: cancellationToken);
        }

        protected override bool IsTransient(Exception ex)
        {
            // Mark JobPersistenceException wrapping TransientTestException as transient
            return ex is JobPersistenceException { InnerException: TransientTestException };
        }

        public ValueTask<T> CallExecuteInLocalTransactionLock<T>(
            Func<ConnectionAndTransactionHolder, ValueTask<T>> txCallback,
            CancellationToken cancellationToken)
        {
            return ExecuteInLocalTransactionLock(null, txCallback, cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// A test exception that will be recognized as transient by <see cref="RetryTestAdoJobStoreBase"/>.
    /// </summary>
    public sealed class TransientTestException : Exception
    {
        public TransientTestException() : base("Simulated transient database error (e.g. deadlock)")
        {
        }
    }

    #region GetTriggerState precedence

    /// <summary>
    /// Pins the whole stored-state/executing matrix, including the cases that only differ once a trigger
    /// is executing. The reported precedence is None &gt; Error &gt; Paused &gt; Executing &gt; Blocked &gt;
    /// Complete &gt; Normal.
    /// </summary>
    [TestCase(StoredTriggerState.Waiting, false, TriggerState.Normal)]
    [TestCase(StoredTriggerState.Waiting, true, TriggerState.Executing)]
    [TestCase(StoredTriggerState.Acquired, false, TriggerState.Normal)]
    [TestCase(StoredTriggerState.Acquired, true, TriggerState.Executing)]
    [TestCase(StoredTriggerState.Complete, false, TriggerState.Complete)]
    [TestCase(StoredTriggerState.Complete, true, TriggerState.Executing)]
    [TestCase(StoredTriggerState.Blocked, false, TriggerState.Blocked)]
    [TestCase(StoredTriggerState.Blocked, true, TriggerState.Executing)]
    [TestCase(StoredTriggerState.Paused, false, TriggerState.Paused)]
    [TestCase(StoredTriggerState.Paused, true, TriggerState.Paused)]
    [TestCase(StoredTriggerState.PausedBlocked, false, TriggerState.Paused)]
    [TestCase(StoredTriggerState.PausedBlocked, true, TriggerState.Paused)]
    [TestCase(StoredTriggerState.Error, false, TriggerState.Error)]
    [TestCase(StoredTriggerState.Error, true, TriggerState.Error)]
    [TestCase(StoredTriggerState.Deleted, false, TriggerState.None)]
    [TestCase(StoredTriggerState.Deleted, true, TriggerState.None)]
    public async Task GetTriggerState_MapsStoredStateAndExecutionToReportedState(
        StoredTriggerState storedState,
        bool isExecuting,
        TriggerState expected)
    {
        TransientTriggersFiredTestStore store = CreateTransientTriggersFiredTestStore();
        IDriverDelegate del = A.Fake<IDriverDelegate>();
        store.DirectDelegate = del;

        var triggerKey = new TriggerKey("trigger1", "group1");
        A.CallTo(() => del.SelectTriggerStateWithExecuting(A<ConnectionAndTransactionHolder>.Ignored, triggerKey, A<CancellationToken>.Ignored))
            .Returns(new TriggerExecutionState(storedState, isExecuting));

        TriggerState state = await store.GetTriggerState(triggerKey);

        state.Should().Be(expected);
    }

    #endregion

    #region TriggersFired transient retry tests

    [Test]
    public async Task TriggersFired_RetriesOnTransientException()
    {
        int selectStateCallCount = 0;
        TransientTriggersFiredTestStore store = CreateTransientTriggersFiredTestStore();
        IDriverDelegate del = A.Fake<IDriverDelegate>();
        store.DirectDelegate = del;

        IOperableTrigger trigger = CreateTestTrigger();
        IJobDetail job = CreateConcurrentJob();

        // Throw raw TransientTestException (simulates a raw DB exception like SqlException).
        // TriggerFired wraps it as JobPersistenceException(inner: TransientTestException),
        // which IsTransient recognizes, enabling the retry in ExecuteInLocalTransactionLock.
        A.CallTo(() => del.SelectTriggerHeader(A<ConnectionAndTransactionHolder>.Ignored, trigger.Key, A<CancellationToken>.Ignored))
            .ReturnsLazily(call =>
            {
                selectStateCallCount++;
                if (selectStateCallCount == 1)
                {
                    throw new TransientTestException();
                }
                return new ValueTask<StoredTriggerHeader>(StoredHeader(trigger, StoredTriggerState.Acquired));
            });
        A.CallTo(() => del.SelectJobDetail(A<ConnectionAndTransactionHolder>.Ignored, trigger.JobKey, A<ITypeLoader>.Ignored, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<IJobDetail>(job));

        List<TriggerFiredResult> results = await store.TriggersFired(new[] { trigger });

        results.Should().HaveCount(1);
        results[0].TriggerFiredBundle.Should().NotBeNull();
        results[0].Exception.Should().BeNull();
        selectStateCallCount.Should().Be(2, "first call throws transient, second succeeds after retry");
    }

    [Test]
    public async Task TriggersFired_DoesNotRetryNonTransientException()
    {
        TransientTriggersFiredTestStore store = CreateTransientTriggersFiredTestStore();
        IDriverDelegate del = A.Fake<IDriverDelegate>();
        store.DirectDelegate = del;

        IOperableTrigger trigger = CreateTestTrigger();

        // A non-transient exception (no TransientTestException in the chain)
        A.CallTo(() => del.SelectTriggerHeader(A<ConnectionAndTransactionHolder>.Ignored, trigger.Key, A<CancellationToken>.Ignored))
            .ThrowsAsync(new InvalidOperationException("permanent error"));

        List<TriggerFiredResult> results = await store.TriggersFired(new[] { trigger });

        // Non-transient exception should be wrapped in result, not retried
        results.Should().HaveCount(1);
        results[0].TriggerFiredBundle.Should().BeNull();
        results[0].Exception.Should().NotBeNull();
        A.CallTo(() => del.SelectTriggerHeader(A<ConnectionAndTransactionHolder>.Ignored, trigger.Key, A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task TriggersFired_TransientExceptionPropagatesAfterMaxRetries()
    {
        TransientTriggersFiredTestStore store = CreateTransientTriggersFiredTestStore(maxTransientRetries: 1);
        IDriverDelegate del = A.Fake<IDriverDelegate>();
        store.DirectDelegate = del;

        IOperableTrigger trigger = CreateTestTrigger();

        // Always throw transient — after retries are exhausted, the exception must propagate
        A.CallTo(() => del.SelectTriggerHeader(A<ConnectionAndTransactionHolder>.Ignored, trigger.Key, A<CancellationToken>.Ignored))
            .ThrowsAsync(new TransientTestException());

        Func<Task> act = async () => await store.TriggersFired(new[] { trigger });

        await act.Should().ThrowAsync<JobPersistenceException>();
        // Initial attempt + 1 retry = 2 total
        A.CallTo(() => del.SelectTriggerHeader(A<ConnectionAndTransactionHolder>.Ignored, trigger.Key, A<CancellationToken>.Ignored))
            .MustHaveHappened(2, Times.Exactly);
    }

    [Test]
    public async Task TriggersFired_BatchTransientErrorRollsBackAndRetriesAllTriggers()
    {
        int triggerBCallCount = 0;
        TransientTriggersFiredTestStore store = CreateTransientTriggersFiredTestStore();
        IDriverDelegate del = A.Fake<IDriverDelegate>();
        store.DirectDelegate = del;

        IOperableTrigger triggerA = CreateTestTrigger("tA", "fire-A");
        IOperableTrigger triggerB = CreateTestTrigger("tB", "fire-B");
        IJobDetail job = CreateConcurrentJob();

        // Capture trigger A's original fire times to verify cloning protects against double-mutation
        DateTimeOffset? originalNextFireTime = triggerA.NextFireTimeUtc;
        DateTimeOffset? originalPrevFireTime = triggerA.PreviousFireTimeUtc;

        // Trigger A always succeeds — but its work is rolled back when B fails
        A.CallTo(() => del.SelectTriggerHeader(A<ConnectionAndTransactionHolder>.Ignored, triggerA.Key, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<StoredTriggerHeader>(StoredHeader(triggerA, StoredTriggerState.Acquired)));

        // Trigger B throws transient on first call, succeeds on retry
        A.CallTo(() => del.SelectTriggerHeader(A<ConnectionAndTransactionHolder>.Ignored, triggerB.Key, A<CancellationToken>.Ignored))
            .ReturnsLazily(call =>
            {
                triggerBCallCount++;
                if (triggerBCallCount == 1)
                {
                    throw new TransientTestException();
                }
                return new ValueTask<StoredTriggerHeader>(StoredHeader(triggerB, StoredTriggerState.Acquired));
            });

        A.CallTo(() => del.SelectJobDetail(A<ConnectionAndTransactionHolder>.Ignored, A<JobKey>.Ignored, A<ITypeLoader>.Ignored, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<IJobDetail>(job));

        List<TriggerFiredResult> results = await store.TriggersFired(new[] { triggerA, triggerB });

        // Both triggers should succeed after retry
        results.Should().HaveCount(2);
        results.Should().OnlyContain(r => r.TriggerFiredBundle != null && r.Exception == null);
        // Trigger A was called twice: first attempt (rolled back) + successful retry
        A.CallTo(() => del.SelectTriggerHeader(A<ConnectionAndTransactionHolder>.Ignored, triggerA.Key, A<CancellationToken>.Ignored))
            .MustHaveHappened(2, Times.Exactly);
        triggerBCallCount.Should().Be(2);

        // Verify original trigger objects were NOT mutated (clone protects against
        // double-mutation from trigger.Triggered() across retry attempts)
        triggerA.NextFireTimeUtc.Should().Be(originalNextFireTime,
            "original trigger must not be mutated by TriggersFired — clones should be used");
        triggerA.PreviousFireTimeUtc.Should().Be(originalPrevFireTime,
            "original trigger must not be mutated by TriggersFired — clones should be used");
    }

    private static TransientTriggersFiredTestStore CreateTransientTriggersFiredTestStore(int maxTransientRetries = 3)
    {
        return new TransientTriggersFiredTestStore(maxTransientRetries);
    }

    /// <summary>
    /// A <see cref="AdoJobStoreBase"/> subclass used to test transient retry logic
    /// in the <see cref="AdoJobStoreBase.TriggersFired"/> method.
    /// </summary>
    public sealed class TransientTriggersFiredTestStore : AdoJobStoreBase
    {
        public TransientTriggersFiredTestStore(int maxTransientRetries = 3)
        : base(TestJobStores.Signaler(), TestJobStores.TypeLoader(), TimeProvider.System, TestJobStores.SchedulerOptions(), TestJobStores.StoreOptions(configure: options =>
        {
            options.MaxTransientRetries = maxTransientRetries;
            options.TransientRetryInterval = TimeSpan.Zero;
        }), TestJobStores.ClusteringOptions(), TestJobStores.Serializer(), TestJobStores.DbProvider(), TestJobStores.DriverDelegate(), TestJobStores.LockHandler())
        {
            LockHandler = new SimpleSemaphore();
        }

        protected override ValueTask<ConnectionAndTransactionHolder> GetLocalTransactionConnection(CancellationToken cancellationToken = default)
        {
            return new ValueTask<ConnectionAndTransactionHolder>(
                new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null));
        }

        protected override ValueTask<T> ExecuteInLock<T>(
            SchedulerLock? lockKind,
            Func<ConnectionAndTransactionHolder, ValueTask<T>> txCallback,
            CancellationToken cancellationToken = default)
        {
            return ExecuteInLocalTransactionLock(lockKind, txCallback, cancellationToken: cancellationToken);
        }

        protected override bool IsTransient(Exception ex)
        {
            return ex is JobPersistenceException { InnerException: TransientTestException };
        }

        internal IDriverDelegate DirectDelegate
        {
            set
            {
                FieldInfo fieldInfo = typeof(AdoJobStoreBase).GetField("driverDelegate", BindingFlags.Instance | BindingFlags.NonPublic)!;
                fieldInfo.SetValue(this, value);
            }
        }
    }

    #endregion

    #region DoCheckin transient retry tests

    [Test]
    public async Task DoCheckin_RetriesOnTransientException()
    {
        int updateCallCount = 0;
        TransientDoCheckinTestStore store = CreateTransientDoCheckinTestStore();
        IDriverDelegate del = A.Fake<IDriverDelegate>();
        store.DirectDelegate = del;

        // Not first check-in: ClusterCheckIn path calls SelectSchedulerStateRecords then UpdateSchedulerState
        store.SetFirstCheckIn(false);

        A.CallTo(() => del.SelectSchedulerStateRecords(A<ConnectionAndTransactionHolder>.Ignored, A<string>.Ignored, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<List<SchedulerStateRecord>>(new List<SchedulerStateRecord>
            {
                new SchedulerStateRecord(store.InstanceId, DateTimeOffset.UtcNow, TimeSpan.FromSeconds(15))
            }));

        A.CallTo(() => del.UpdateSchedulerState(A<ConnectionAndTransactionHolder>.Ignored, A<string>.Ignored, A<DateTimeOffset>.Ignored, A<CancellationToken>.Ignored))
            .ReturnsLazily(call =>
            {
                updateCallCount++;
                if (updateCallCount == 1)
                {
                    throw new TransientTestException();
                }
                return new ValueTask<int>(1);
            });

        bool result = await store.DoCheckin(Guid.NewGuid());

        result.Should().BeFalse("no recovery needed");
        updateCallCount.Should().Be(2, "first call throws transient, second succeeds after retry");
    }

    [Test]
    public async Task DoCheckin_DoesNotRetryNonTransientException()
    {
        TransientDoCheckinTestStore store = CreateTransientDoCheckinTestStore();
        IDriverDelegate del = A.Fake<IDriverDelegate>();
        store.DirectDelegate = del;

        store.SetFirstCheckIn(false);

        A.CallTo(() => del.SelectSchedulerStateRecords(A<ConnectionAndTransactionHolder>.Ignored, A<string>.Ignored, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<List<SchedulerStateRecord>>(new List<SchedulerStateRecord>
            {
                new SchedulerStateRecord(store.InstanceId, DateTimeOffset.UtcNow, TimeSpan.FromSeconds(15))
            }));

        A.CallTo(() => del.UpdateSchedulerState(A<ConnectionAndTransactionHolder>.Ignored, A<string>.Ignored, A<DateTimeOffset>.Ignored, A<CancellationToken>.Ignored))
            .ThrowsAsync(new InvalidOperationException("permanent error"));

        Func<Task> act = async () => await store.DoCheckin(Guid.NewGuid());

        await act.Should().ThrowAsync<JobPersistenceException>();
        A.CallTo(() => del.UpdateSchedulerState(A<ConnectionAndTransactionHolder>.Ignored, A<string>.Ignored, A<DateTimeOffset>.Ignored, A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task DoCheckin_TransientExceptionPropagatesAfterMaxRetries()
    {
        TransientDoCheckinTestStore store = CreateTransientDoCheckinTestStore(maxTransientRetries: 1);
        IDriverDelegate del = A.Fake<IDriverDelegate>();
        store.DirectDelegate = del;

        store.SetFirstCheckIn(false);

        A.CallTo(() => del.SelectSchedulerStateRecords(A<ConnectionAndTransactionHolder>.Ignored, A<string>.Ignored, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<List<SchedulerStateRecord>>(new List<SchedulerStateRecord>
            {
                new SchedulerStateRecord(store.InstanceId, DateTimeOffset.UtcNow, TimeSpan.FromSeconds(15))
            }));

        A.CallTo(() => del.UpdateSchedulerState(A<ConnectionAndTransactionHolder>.Ignored, A<string>.Ignored, A<DateTimeOffset>.Ignored, A<CancellationToken>.Ignored))
            .ThrowsAsync(new TransientTestException());

        Func<Task> act = async () => await store.DoCheckin(Guid.NewGuid());

        await act.Should().ThrowAsync<JobPersistenceException>();
        // Initial attempt + 1 retry = 2 total
        A.CallTo(() => del.UpdateSchedulerState(A<ConnectionAndTransactionHolder>.Ignored, A<string>.Ignored, A<DateTimeOffset>.Ignored, A<CancellationToken>.Ignored))
            .MustHaveHappened(2, Times.Exactly);
    }

    [Test]
    public async Task DoCheckin_LastCheckinNotAdvancedOnFailure()
    {
        TransientDoCheckinTestStore store = CreateTransientDoCheckinTestStore();
        IDriverDelegate del = A.Fake<IDriverDelegate>();
        store.DirectDelegate = del;

        store.SetFirstCheckIn(false);

        DateTimeOffset initialCheckin = store.LastCheckin;

        int updateCallCount = 0;
        A.CallTo(() => del.SelectSchedulerStateRecords(A<ConnectionAndTransactionHolder>.Ignored, A<string>.Ignored, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<List<SchedulerStateRecord>>(new List<SchedulerStateRecord>
            {
                new SchedulerStateRecord(store.InstanceId, DateTimeOffset.UtcNow, TimeSpan.FromSeconds(15))
            }));

        A.CallTo(() => del.UpdateSchedulerState(A<ConnectionAndTransactionHolder>.Ignored, A<string>.Ignored, A<DateTimeOffset>.Ignored, A<CancellationToken>.Ignored))
            .ReturnsLazily(call =>
            {
                updateCallCount++;
                if (updateCallCount == 1)
                {
                    throw new TransientTestException();
                }
                return new ValueTask<int>(1);
            });

        await store.DoCheckin(Guid.NewGuid());

        store.LastCheckin.Should().BeAfter(initialCheckin, "LastCheckin should advance after successful check-in");
    }

    private static TransientDoCheckinTestStore CreateTransientDoCheckinTestStore(int maxTransientRetries = 3)
    {
        return new TransientDoCheckinTestStore(maxTransientRetries);
    }

    /// <summary>
    /// A <see cref="AdoJobStoreBase"/> subclass used to test transient retry logic
    /// in the <see cref="AdoJobStoreBase.DoCheckin"/> method.
    /// </summary>
    public sealed class TransientDoCheckinTestStore : AdoJobStoreBase
    {
        public TransientDoCheckinTestStore(int maxTransientRetries = 3)
        : base(TestJobStores.Signaler(), TestJobStores.TypeLoader(), TimeProvider.System, TestJobStores.SchedulerOptions("test-scheduler", "test-instance"), TestJobStores.StoreOptions(configure: options =>
        {
            options.MaxTransientRetries = maxTransientRetries;
            options.TransientRetryInterval = TimeSpan.Zero;
        }), TestJobStores.ClusteringOptions(), TestJobStores.Serializer(), TestJobStores.DbProvider(), TestJobStores.DriverDelegate(), TestJobStores.LockHandler())
        {
            LockHandler = new SimpleSemaphore();
        }

        public void SetFirstCheckIn(bool value)
        {
            FieldInfo fieldInfo = typeof(AdoJobStoreBase).GetField("firstCheckIn", BindingFlags.Instance | BindingFlags.NonPublic)!;
            fieldInfo.SetValue(this, value);
        }

        protected override ValueTask<ConnectionAndTransactionHolder> GetLocalTransactionConnection(CancellationToken cancellationToken = default)
        {
            return new ValueTask<ConnectionAndTransactionHolder>(
                new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null));
        }

        protected override ValueTask<T> ExecuteInLock<T>(
            SchedulerLock? lockKind,
            Func<ConnectionAndTransactionHolder, ValueTask<T>> txCallback,
            CancellationToken cancellationToken = default)
        {
            return ExecuteInLocalTransactionLock(lockKind, txCallback, cancellationToken: cancellationToken);
        }

        protected override bool IsTransient(Exception ex)
        {
            return ex is JobPersistenceException { InnerException: TransientTestException };
        }

        internal IDriverDelegate DirectDelegate
        {
            set
            {
                FieldInfo fieldInfo = typeof(AdoJobStoreBase).GetField("driverDelegate", BindingFlags.Instance | BindingFlags.NonPublic)!;
                fieldInfo.SetValue(this, value);
            }
        }
    }

    #endregion

    #region Acquisition criteria

    /// <summary>
    /// Arranges the acquisition read to find nothing, so the acquisition loop exits on its first pass,
    /// and hands back the criteria the delegate was called with — one entry per attempt.
    /// </summary>
    private static List<TriggerAcquisitionCriteria> GivenNoTriggersToAcquire(IDriverDelegate del)
    {
        List<TriggerAcquisitionCriteria> received = [];
        A.CallTo(() => del.SelectTriggersToAcquire(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<TriggerAcquisitionCriteria>.Ignored,
                A<CancellationToken>.Ignored))
            .ReturnsLazily((ConnectionAndTransactionHolder _, TriggerAcquisitionCriteria criteria, CancellationToken _) =>
            {
                received.Add(criteria);
                return new ValueTask<List<TriggerAcquireResult>>(new List<TriggerAcquireResult>());
            });
        return received;
    }

    [Test]
    public async Task AcquireNextTriggers_ShouldBuildCriteriaFromTheRequest()
    {
        FakeTimeProvider clock = GivenStoppedClock(ClusterNow);
        List<TriggerAcquisitionCriteria> received = GivenNoTriggersToAcquire(driverDelegate);

        ExecutionLimits limits = ExecutionLimitsBuilder.Create().ForGroup("batch", 2).Build();
        TriggerAcquisitionRequest request = new()
        {
            NoLaterThan = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero),
            TimeWindow = TimeSpan.FromSeconds(30),
            MaxCount = 5,
            ExecutionLimits = limits,
        };

        await jobStoreSupport.AcquireNextTriggers(request);

        TriggerAcquisitionCriteria criteria = received.Should().ContainSingle(
            "one acquisition attempt asks the delegate once").Subject;

        criteria.NoLaterThan.Should().Be(request.NoLaterThan + request.TimeWindow,
            "the batch window widens how far ahead the store is willing to look");
        criteria.MaxCount.Should().Be(request.MaxCount, "the request caps the batch size");
        criteria.ExecutionLimits.Should().BeSameAs(limits,
            "the caller's snapshot is handed through untouched, so a delegate counting slots down works on a copy");
        criteria.LiveNodeCutoff.Should().Be(clock.GetUtcNow() - jobStoreSupport.ClusterCheckinMisfireThreshold,
            "the cutoff is exactly now less the check-in misfire threshold, so a node that checked in more recently than that keeps its pinned triggers");
    }

    [Test]
    public async Task CreateAcquisitionCriteria_ShouldLetADerivedStoreNarrowWhatThisNodeAcquires()
    {
        CappedAcquisitionTestStore store = new();
        IDriverDelegate del = A.Fake<IDriverDelegate>();
        store.DirectDelegate = del;
        store.DirectSignaler = A.Fake<ISchedulerSignaler>();
        List<TriggerAcquisitionCriteria> received = GivenNoTriggersToAcquire(del);

        await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = DateTimeOffset.UtcNow,
            MaxCount = 5,
        });

        received.Should().ContainSingle().Which.MaxCount.Should().Be(1,
            "the override, not the request, has the last word on the criteria the delegate is called with");
    }

    [Test]
    public async Task AcquireNextTriggers_ShouldNotCountTheClusterWhenNoLimitIsClusterScoped()
    {
        GivenNoTriggersToAcquire(driverDelegate);

        await jobStoreSupport.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = DateTimeOffset.UtcNow,
            ExecutionLimits = ExecutionLimitsBuilder.Create().ForGroup("batch", 2).Build(),
        });

        A.CallTo(() => driverDelegate.SelectExecutionGroupsInFlight(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<CancellationToken>.Ignored))
            .MustNotHaveHappened();
    }

    [Test]
    public async Task AcquireNextTriggers_ShouldCountTheClusterOncePerAttemptForAClusterScopedLimit()
    {
        List<TriggerAcquisitionCriteria> received = GivenNoTriggersToAcquire(driverDelegate);
        List<ExecutionGroupInFlight> inFlight = [new ExecutionGroupInFlight("tenant", "nightly", 2)];
        A.CallTo(() => driverDelegate.SelectExecutionGroupsInFlight(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<CancellationToken>.Ignored))
            .Returns(new ValueTask<List<ExecutionGroupInFlight>>(inFlight));

        await jobStoreSupport.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = DateTimeOffset.UtcNow,
            ExecutionLimits = ExecutionLimitsBuilder.Create()
                .ForGroup("tenant", 3, ExecutionLimitScope.Cluster)
                .Build(),
        });

        A.CallTo(() => driverDelegate.SelectExecutionGroupsInFlight(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();

        received.Should().ContainSingle().Which.ClusterInFlight.Should().BeEquivalentTo(inFlight,
            "the count is what the delegate filters against, so it has to reach the criteria the delegate is called with");
    }

    /// <summary>
    /// A store that keeps the cluster count somewhere other than the fired-triggers table says so by
    /// answering the question in its own override, and the base must not ask again.
    /// </summary>
    [Test]
    public async Task CreateAcquisitionCriteria_ShouldLeaveAnOverridesOwnClusterCountAlone()
    {
        OwnClusterCountTestStore store = new();
        IDriverDelegate del = A.Fake<IDriverDelegate>();
        store.DirectDelegate = del;
        store.DirectSignaler = A.Fake<ISchedulerSignaler>();
        List<TriggerAcquisitionCriteria> received = GivenNoTriggersToAcquire(del);

        await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = DateTimeOffset.UtcNow,
            ExecutionLimits = ExecutionLimitsBuilder.Create()
                .ForGroup("tenant", 3, ExecutionLimitScope.Cluster)
                .Build(),
        });

        A.CallTo(() => del.SelectExecutionGroupsInFlight(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<CancellationToken>.Ignored))
            .MustNotHaveHappened();

        received.Should().ContainSingle().Which.ClusterInFlight.Should().ContainSingle()
            .Which.Count.Should().Be(7, "the override, not the base store, had the last word on the count");
    }

    /// <summary>
    /// A store that narrows acquisition through <see cref="AdoJobStoreBase.CreateAcquisitionCriteria" />,
    /// the way a store filtering on job type (issue #2238) would.
    /// </summary>
    private sealed class CappedAcquisitionTestStore : TestAdoJobStoreBase
    {
        protected override TriggerAcquisitionCriteria CreateAcquisitionCriteria(TriggerAcquisitionRequest request)
        {
            return base.CreateAcquisitionCriteria(request) with { MaxCount = 1 };
        }
    }

    /// <summary>
    /// A store that answers the cluster in-flight question itself rather than letting the base read it
    /// off the fired-triggers table.
    /// </summary>
    private sealed class OwnClusterCountTestStore : TestAdoJobStoreBase
    {
        protected override TriggerAcquisitionCriteria CreateAcquisitionCriteria(TriggerAcquisitionRequest request)
        {
            return base.CreateAcquisitionCriteria(request) with
            {
                ClusterInFlight = [new ExecutionGroupInFlight("tenant", "nightly", 7)],
            };
        }
    }

    #endregion

    #region Cluster check-in and recovery

    /// <summary>
    /// The instant the clock stands still at for every cluster test. Failure detection, the deferred
    /// recovery grace period and the check-in stamp are all arithmetic on "now", so the clock is stopped
    /// and the expectations are written out in full rather than approximated.
    /// </summary>
    private static readonly DateTimeOffset ClusterNow = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The instance id <see cref="TestJobStores.SchedulerOptions" /> gives the store under test: what it
    /// recognises as its own scheduler state row.
    /// </summary>
    private const string OwnInstanceId = "TestInstance";

    private const string DeadInstanceId = "dead-node";

    /// <summary>
    /// A check-in interval short enough that the grace period arithmetic fits in a comment: two intervals
    /// plus the misfire threshold, so 10s + 10s + the default 7.5s = 27.5s.
    /// </summary>
    private static readonly TimeSpan CheckinInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Repoints <see cref="jobStoreSupport" /> at a store whose clock has stopped at
    /// <paramref name="now" />, keeping the faked delegate <see cref="SetUp" /> arranged.
    /// </summary>
    private FakeTimeProvider GivenStoppedClock(DateTimeOffset now)
    {
        FakeTimeProvider clock = new(now);
        jobStoreSupport = new TestAdoJobStoreBase(timeProvider: clock);
        jobStoreSupport.DirectDelegate = driverDelegate;
        jobStoreSupport.DirectSignaler = A.Fake<ISchedulerSignaler>();
        return clock;
    }

    private static ConnectionAndTransactionHolder FakeConnection() => new(A.Fake<DbConnection>(), null);

    /// <summary>
    /// Arranges the scheduler state read the check-in path makes, which asks for every instance at once.
    /// </summary>
    private void GivenSchedulerStates(params SchedulerStateRecord[] states)
    {
        A.CallTo(() => driverDelegate.SelectSchedulerStateRecords(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<string>.Ignored,
                A<CancellationToken>.Ignored))
            .Returns(new ValueTask<List<SchedulerStateRecord>>(states.ToList()));
    }

    /// <summary>
    /// Arranges the distinct instance names the orphan sweep reads out of the fired-triggers table.
    /// </summary>
    private void GivenFiredTriggerInstanceNames(params string[] names)
    {
        A.CallTo(() => driverDelegate.SelectFiredTriggerInstanceNames(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<CancellationToken>.Ignored))
            .Returns(new ValueTask<List<string>>(names.ToList()));
    }

    /// <summary>
    /// Arranges the two fired-trigger reads recovery makes: <paramref name="records" /> come back for the
    /// failed instance, and the per-trigger read of the COMPLETE sweep finds nothing left behind.
    /// </summary>
    private void GivenFiredTriggersForInstance(string instanceId, params FiredTriggerRecord[] records)
    {
        A.CallTo(() => driverDelegate.SelectFiredTriggerRecords(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<FiredTriggerQuery>.That.Matches(query => query.InstanceId == instanceId),
                A<CancellationToken>.Ignored))
            .Returns(new ValueTask<List<FiredTriggerRecord>>(records.ToList()));

        A.CallTo(() => driverDelegate.SelectFiredTriggerRecords(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<FiredTriggerQuery>.That.Matches(query => query.Trigger != null),
                A<CancellationToken>.Ignored))
            .Returns(new ValueTask<List<FiredTriggerRecord>>([]));
    }

    private static FiredTriggerRecord FiredTrigger(
        string fireInstanceId,
        StoredTriggerState state,
        TriggerKey triggerKey,
        JobKey jobKey = null,
        bool disallowsConcurrentExecution = false,
        bool requestsRecovery = false,
        int priority = 5,
        DateTimeOffset firedAt = default,
        string instanceId = DeadInstanceId)
    {
        return new FiredTriggerRecord
        {
            FireInstanceId = fireInstanceId,
            FireInstanceState = state,
            TriggerKey = triggerKey,
            JobKey = jobKey,
            SchedulerInstanceId = instanceId,
            JobDisallowsConcurrentExecution = disallowsConcurrentExecution,
            JobRequestsRecovery = requestsRecovery,
            Priority = priority,
            FireTimestamp = firedAt == default ? ClusterNow - TimeSpan.FromMinutes(1) : firedAt,
        };
    }

    #region FindFailedInstances

    [Test]
    public async Task FindFailedInstances_ShouldRecoverItsOwnRecordOnTheFirstCheckIn()
    {
        ConnectionAndTransactionHolder conn = FakeConnection();
        GivenStoppedClock(ClusterNow);
        jobStoreSupport.SetFirstCheckIn(true);

        SchedulerStateRecord own = new(OwnInstanceId, ClusterNow - TimeSpan.FromSeconds(1), CheckinInterval);
        GivenSchedulerStates(own);

        List<SchedulerStateRecord> failed = await jobStoreSupport.CallFindFailedInstances(conn);

        failed.Should().ContainSingle(
                "a node starting up recovers whatever its own previous run left in flight, however recent that run's last check-in was")
            .Which.Should().BeSameAs(own);

        A.CallTo(() => driverDelegate.SelectSchedulerStateRecords(conn, null, A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task FindFailedInstances_ShouldLeaveItsOwnRecordAloneAfterTheFirstCheckIn()
    {
        ConnectionAndTransactionHolder conn = FakeConnection();
        GivenStoppedClock(ClusterNow);
        jobStoreSupport.SetFirstCheckIn(false);
        jobStoreSupport.LastCheckin = ClusterNow;

        GivenSchedulerStates(new SchedulerStateRecord(OwnInstanceId, ClusterNow - TimeSpan.FromSeconds(1), CheckinInterval));

        List<SchedulerStateRecord> failed = await jobStoreSupport.CallFindFailedInstances(conn);

        failed.Should().BeEmpty("a running node never recovers itself; only the first pass does, on behalf of the run before it");

        A.CallTo(() => driverDelegate.SelectFiredTriggerInstanceNames(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<CancellationToken>.Ignored))
            .MustNotHaveHappened();
    }

    [Test]
    public async Task FindFailedInstances_ShouldReportOtherInstancesWhoseCheckInExpired()
    {
        ConnectionAndTransactionHolder conn = FakeConnection();
        GivenStoppedClock(ClusterNow);
        jobStoreSupport.SetFirstCheckIn(false);

        // This node is itself healthy, so CalcFailedIfAfter allows each record its own check-in
        // interval plus the misfire threshold: 17.5s of silence before it is declared dead.
        jobStoreSupport.LastCheckin = ClusterNow;

        SchedulerStateRecord live = new("live-node", ClusterNow - TimeSpan.FromSeconds(5), CheckinInterval);
        SchedulerStateRecord dead = new(DeadInstanceId, ClusterNow - TimeSpan.FromSeconds(60), CheckinInterval);
        GivenSchedulerStates(new SchedulerStateRecord(OwnInstanceId, ClusterNow, CheckinInterval), live, dead);

        List<SchedulerStateRecord> failed = await jobStoreSupport.CallFindFailedInstances(conn);

        failed.Should().ContainSingle("only the node that stopped checking in is dead")
            .Which.Should().BeSameAs(dead);
    }

    [Test]
    public async Task FindFailedInstances_ShouldSynthesizeOrphansForFiredTriggersWithNoStateRow()
    {
        ConnectionAndTransactionHolder conn = FakeConnection();
        GivenStoppedClock(ClusterNow);
        jobStoreSupport.SetFirstCheckIn(true);

        GivenSchedulerStates(new SchedulerStateRecord(OwnInstanceId, ClusterNow, CheckinInterval));
        GivenFiredTriggerInstanceNames(OwnInstanceId, "ghost-node");

        List<SchedulerStateRecord> failed = await jobStoreSupport.CallFindFailedInstances(conn);

        failed.Should().HaveCount(2, "the start-up pass returns this node's own record plus the orphan it found");

        SchedulerStateRecord orphan = failed.Should().ContainSingle(rec => rec.SchedulerInstanceId == "ghost-node",
            "a fired trigger whose instance has no scheduler state row belongs to a node that died without cleaning up").Subject;

        orphan.CheckinTimestamp.Should().Be(default,
            "an orphan has no check-in history to read, and ClusterRecover reads that zero back as 'never defer recovery'");
        orphan.CheckinInterval.Should().Be(default,
            "an orphan has no check-in history to read, and ClusterRecover reads that zero back as 'never defer recovery'");
    }

    [Test]
    public async Task FindFailedInstances_ShouldTolerateItsOwnRecordHavingBeenRecoveredByAnotherNode()
    {
        ConnectionAndTransactionHolder conn = FakeConnection();
        GivenStoppedClock(ClusterNow);
        jobStoreSupport.SetFirstCheckIn(false);
        jobStoreSupport.LastCheckin = ClusterNow;

        // No row for this node: another node decided it was dead and deleted its state.
        GivenSchedulerStates(new SchedulerStateRecord("live-node", ClusterNow, CheckinInterval));

        List<SchedulerStateRecord> failed = await jobStoreSupport.CallFindFailedInstances(conn);

        failed.Should().BeEmpty(
            "being recovered out from under itself is a warning, not a failure: the check-in that follows writes the row back");
    }

    [Test]
    public async Task FindFailedInstances_ShouldWrapDelegateFailures()
    {
        ConnectionAndTransactionHolder conn = FakeConnection();
        GivenStoppedClock(ClusterNow);

        A.CallTo(() => driverDelegate.SelectSchedulerStateRecords(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<string>.Ignored,
                A<CancellationToken>.Ignored))
            .Throws(new InvalidOperationException("state table unavailable"));

        Func<Task> act = async () => await jobStoreSupport.CallFindFailedInstances(conn);

        await act.Should().ThrowAsync<JobPersistenceException>()
            .WithMessage("*identifying failed instances*")
            .WithInnerException<JobPersistenceException, InvalidOperationException>()
            .WithMessage("state table unavailable");

        jobStoreSupport.LastCheckin.Should().Be(ClusterNow,
            "a failed scan still stamps the check-in, so the next CalcFailedIfAfter does not treat this node's own silence as elapsed time");
    }

    #endregion

    #region CalcFailedIfAfter

    [Test]
    public void CalcFailedIfAfter_ShouldUseTheRecordsCheckInIntervalWhileThisNodeIsHealthy()
    {
        GivenStoppedClock(ClusterNow);
        jobStoreSupport.LastCheckin = ClusterNow - TimeSpan.FromSeconds(1);

        SchedulerStateRecord rec = new("other-node", ClusterNow - TimeSpan.FromSeconds(30), CheckinInterval);

        DateTimeOffset failedIfAfter = jobStoreSupport.CallCalcFailedIfAfter(rec);

        failedIfAfter.Should().Be(rec.CheckinTimestamp + CheckinInterval + jobStoreSupport.ClusterCheckinMisfireThreshold,
            "this node checked in a second ago, so the record's own interval is the longer of the two and sets the deadline");
    }

    [Test]
    public void CalcFailedIfAfter_ShouldStretchTheDeadlineWhenThisNodesOwnCheckInsAreLate()
    {
        GivenStoppedClock(ClusterNow);

        // This node has not checked in for a minute -- it was stalled, and every other node looks
        // silent for exactly as long. Judging them on their own interval would declare the whole
        // cluster dead, so the deadline stretches to cover this node's own outage instead.
        TimeSpan ownOutage = TimeSpan.FromSeconds(60);
        jobStoreSupport.LastCheckin = ClusterNow - ownOutage;

        SchedulerStateRecord rec = new("other-node", ClusterNow - TimeSpan.FromSeconds(30), CheckinInterval);

        DateTimeOffset failedIfAfter = jobStoreSupport.CallCalcFailedIfAfter(rec);

        failedIfAfter.Should().Be(rec.CheckinTimestamp + ownOutage + jobStoreSupport.ClusterCheckinMisfireThreshold,
            "the time this node was out is longer than the record's check-in interval, so it replaces it");
    }

    #endregion

    #region ClusterCheckIn

    [Test]
    public async Task ClusterCheckIn_ShouldRecordThisNodesCheckInTime()
    {
        ConnectionAndTransactionHolder conn = FakeConnection();
        GivenStoppedClock(ClusterNow);
        jobStoreSupport.SetFirstCheckIn(false);
        GivenSchedulerStates(new SchedulerStateRecord(OwnInstanceId, ClusterNow - CheckinInterval, CheckinInterval));

        A.CallTo(() => driverDelegate.UpdateSchedulerState(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<string>.Ignored,
                A<DateTimeOffset>.Ignored,
                A<CancellationToken>.Ignored))
            .Returns(new ValueTask<int>(1));

        await jobStoreSupport.CallClusterCheckIn(conn);

        A.CallTo(() => driverDelegate.UpdateSchedulerState(conn, OwnInstanceId, ClusterNow, A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();

        A.CallTo(() => driverDelegate.InsertSchedulerState(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<string>.Ignored,
                A<DateTimeOffset>.Ignored,
                A<TimeSpan>.Ignored,
                A<CancellationToken>.Ignored))
            .MustNotHaveHappened();

        jobStoreSupport.LastCheckin.Should().Be(ClusterNow,
            "the stamp the other nodes will judge this one by is the time the row was written");
    }

    [Test]
    public async Task ClusterCheckIn_ShouldInsertItsStateRowWhenTheUpdateMatchesNothing()
    {
        ConnectionAndTransactionHolder conn = FakeConnection();
        GivenStoppedClock(ClusterNow);
        jobStoreSupport.SetFirstCheckIn(false);
        GivenSchedulerStates();

        A.CallTo(() => driverDelegate.UpdateSchedulerState(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<string>.Ignored,
                A<DateTimeOffset>.Ignored,
                A<CancellationToken>.Ignored))
            .Returns(new ValueTask<int>(0));

        await jobStoreSupport.CallClusterCheckIn(conn);

        A.CallTo(() => driverDelegate.InsertSchedulerState(
                conn, OwnInstanceId, ClusterNow, jobStoreSupport.ClusterCheckinInterval, A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task ClusterCheckIn_ShouldWrapDelegateFailures()
    {
        ConnectionAndTransactionHolder conn = FakeConnection();
        GivenStoppedClock(ClusterNow);
        jobStoreSupport.SetFirstCheckIn(false);
        GivenSchedulerStates();

        A.CallTo(() => driverDelegate.UpdateSchedulerState(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<string>.Ignored,
                A<DateTimeOffset>.Ignored,
                A<CancellationToken>.Ignored))
            .Throws(new InvalidOperationException("state table unavailable"));

        Func<Task> act = async () => await jobStoreSupport.CallClusterCheckIn(conn);

        await act.Should().ThrowAsync<JobPersistenceException>()
            .WithMessage("*updating scheduler state*")
            .WithInnerException<JobPersistenceException, InvalidOperationException>()
            .WithMessage("state table unavailable");

        jobStoreSupport.LastCheckin.Should().NotBe(ClusterNow,
            "a check-in that never reached the database must not claim to have happened, or this node would look alive to itself");
    }

    #endregion

    #region ClusterRecover

    [Test]
    public async Task ClusterRecover_ShouldReleaseBlockedTriggersOfAFailedInstance()
    {
        ConnectionAndTransactionHolder conn = FakeConnection();
        GivenStoppedClock(ClusterNow);

        JobKey blockedJob = new("blocked", "jg");
        JobKey pausedJob = new("paused", "jg");
        GivenFiredTriggersForInstance(DeadInstanceId,
            FiredTrigger("fi-blocked", StoredTriggerState.Blocked, new TriggerKey("t-blocked", "tg"), blockedJob),
            FiredTrigger("fi-paused-blocked", StoredTriggerState.PausedBlocked, new TriggerKey("t-paused", "tg"), pausedJob));

        await jobStoreSupport.CallClusterRecover(conn, [DeadNode()]);

        A.CallTo(() => driverDelegate.UpdateTriggerStatesForJobFromOtherState(
                conn, blockedJob, StoredTriggerState.Waiting, StoredTriggerState.Blocked, A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();

        A.CallTo(() => driverDelegate.UpdateTriggerStatesForJobFromOtherState(
                conn, pausedJob, StoredTriggerState.Paused, StoredTriggerState.PausedBlocked, A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task ClusterRecover_ShouldReleaseAcquiredTriggersOfAFailedInstance()
    {
        ConnectionAndTransactionHolder conn = FakeConnection();
        GivenStoppedClock(ClusterNow);

        TriggerKey acquired = new("t-acquired", "tg");

        // An ACQUIRED row carries no job: acquisition writes the row before the job is loaded.
        GivenFiredTriggersForInstance(DeadInstanceId,
            FiredTrigger("fi-acquired", StoredTriggerState.Acquired, acquired));

        await jobStoreSupport.CallClusterRecover(conn, [DeadNode()]);

        A.CallTo(() => driverDelegate.UpdateTriggerStateFromOtherState(
                conn, acquired, StoredTriggerState.Waiting, StoredTriggerState.Acquired, A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task ClusterRecover_ShouldScheduleARecoveryTriggerForAJobThatAsksForOne()
    {
        ConnectionAndTransactionHolder conn = FakeConnection();
        FakeTimeProvider clock = GivenStoppedClock(ClusterNow);

        TriggerKey original = new("t-original", "tg");
        JobKey jobKey = new("recoverable", "jg");
        DateTimeOffset firedAt = ClusterNow - TimeSpan.FromMinutes(3);

        GivenFiredTriggersForInstance(DeadInstanceId,
            FiredTrigger("fi-executing", StoredTriggerState.Executing, original, jobKey, requestsRecovery: true, priority: 17, firedAt: firedAt));

        A.CallTo(() => driverDelegate.JobExists(conn, jobKey, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<bool>(true));

        // A faked SelectTriggerJobDataMap has to hand back a real map: the recovery trigger writes the
        // failed-job keys straight into whatever comes back, and production never sees a null there.
        A.CallTo(() => driverDelegate.SelectTriggerJobDataMap(conn, original, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<JobDataMap>(new JobDataMap()));

        IJobDetail job = JobBuilder.Create<ConcurrentTestJob>().WithIdentity(jobKey).RequestRecovery().Build();
        A.CallTo(() => driverDelegate.SelectJobDetail(conn, jobKey, A<ITypeLoader>.Ignored, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<IJobDetail>(job));

        IOperableTrigger recovery = null;
        A.CallTo(() => driverDelegate.InsertTrigger(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<IOperableTrigger>.Ignored,
                A<StoredTriggerState>.Ignored,
                A<IJobDetail>.Ignored,
                A<CancellationToken>.Ignored))
            .Invokes((ConnectionAndTransactionHolder _, IOperableTrigger trigger, StoredTriggerState _, IJobDetail _, CancellationToken _) =>
            {
                recovery = trigger;
            });

        long firstRecoverId = clock.GetTimestamp();

        await jobStoreSupport.CallClusterRecover(conn, [DeadNode()]);

        recovery.Should().NotBeNull("a job that requests recovery is rescheduled rather than dropped");
        recovery.Key.Should().Be(new TriggerKey($"recover_{DeadInstanceId}_{firstRecoverId}", SchedulerConstants.DefaultRecoveryGroup),
            "the recovery trigger names the node it is recovering, and the numbering keeps sibling recoveries of one node apart");
        recovery.JobKey.Should().Be(jobKey);
        recovery.Priority.Should().Be(17, "the recovered firing keeps the priority the dying one had");
        recovery.MisfireInstructionCode.Should().Be(MisfireInstruction.SimpleTrigger.FireNow,
            "the recovery trigger's start time is already in the past, so it must fire now rather than be discarded as misfired");
        recovery.StartTimeUtc.Should().Be(firedAt, "recovery resumes from the moment the original firing started");
        recovery.NextFireTimeUtc.Should().NotBeNull("the first fire time is computed before the trigger is stored, or it would never be picked up");

        recovery.JobDataMap.Should().ContainKey(SchedulerConstants.FailedJobOriginalTriggerName)
            .WhoseValue.Should().Be(original.Name);
        recovery.JobDataMap.Should().ContainKey(SchedulerConstants.FailedJobOriginalTriggerGroup)
            .WhoseValue.Should().Be(original.Group);
        recovery.JobDataMap.Should().ContainKey(SchedulerConstants.FailedJobOriginalTriggerFireTime)
            .WhoseValue.Should().Be(Convert.ToString(firedAt, CultureInfo.InvariantCulture),
                "the job reads the original fire time back out as a string, so the format is part of the contract");
    }

    [Test]
    public async Task ClusterRecover_ShouldNotScheduleRecoveryForAJobThatNoLongerExists()
    {
        ConnectionAndTransactionHolder conn = FakeConnection();
        GivenStoppedClock(ClusterNow);

        TriggerKey original = new("t-original", "tg");
        JobKey jobKey = new("deleted", "jg");

        GivenFiredTriggersForInstance(DeadInstanceId,
            FiredTrigger("fi-executing", StoredTriggerState.Executing, original, jobKey, requestsRecovery: true));

        A.CallTo(() => driverDelegate.JobExists(conn, jobKey, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<bool>(false));

        await jobStoreSupport.CallClusterRecover(conn, [DeadNode()]);

        A.CallTo(() => driverDelegate.InsertTrigger(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<IOperableTrigger>.Ignored,
                A<StoredTriggerState>.Ignored,
                A<IJobDetail>.Ignored,
                A<CancellationToken>.Ignored))
            .MustNotHaveHappened();

        // The recovery trigger is never built at all, so its job data is never read.
        A.CallTo(() => driverDelegate.SelectTriggerJobDataMap(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<TriggerKey>.Ignored,
                A<CancellationToken>.Ignored))
            .MustNotHaveHappened();
    }

    [Test]
    public async Task ClusterRecover_ShouldDeferRecoveryOfAnExecutingNonConcurrentJobWithinTheGracePeriod()
    {
        ConnectionAndTransactionHolder conn = FakeConnection();
        GivenStoppedClock(ClusterNow);

        // Grace period is two check-in intervals plus the misfire threshold: 27.5s. This node went
        // quiet 20s ago, so it may simply be slow rather than dead (#2817).
        SchedulerStateRecord rec = new(DeadInstanceId, ClusterNow - TimeSpan.FromSeconds(20), CheckinInterval);

        GivenFiredTriggersForInstance(DeadInstanceId,
            FiredTrigger("fi-executing", StoredTriggerState.Executing, new TriggerKey("t-executing", "tg"), new JobKey("serial", "jg"), disallowsConcurrentExecution: true),
            FiredTrigger("fi-acquired", StoredTriggerState.Acquired, new TriggerKey("t-acquired", "tg")));

        await jobStoreSupport.CallClusterRecover(conn, [rec]);

        A.CallTo(() => driverDelegate.DeleteFiredTrigger(conn, "fi-executing", A<CancellationToken>.Ignored))
            .MustNotHaveHappened();

        A.CallTo(() => driverDelegate.DeleteFiredTrigger(conn, "fi-acquired", A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();

        A.CallTo(() => driverDelegate.DeleteFiredTriggers(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<FiredTriggerQuery>.That.Matches(query => query.InstanceId == DeadInstanceId),
                A<CancellationToken>.Ignored))
            .MustNotHaveHappened();

        A.CallTo(() => driverDelegate.DeleteSchedulerState(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<string>.Ignored,
                A<CancellationToken>.Ignored))
            .MustNotHaveHappened();

        A.CallTo(() => driverDelegate.RepinTriggersFromDeadNode(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<string>.Ignored,
                A<string>.Ignored,
                A<CancellationToken>.Ignored))
            .MustNotHaveHappened();
    }

    [Test]
    public async Task ClusterRecover_ShouldCompleteRecoveryOnceTheGracePeriodHasExpired()
    {
        ConnectionAndTransactionHolder conn = FakeConnection();
        GivenStoppedClock(ClusterNow);

        // A minute of silence is past the 27.5s grace period, so the node really is gone.
        SchedulerStateRecord rec = new(DeadInstanceId, ClusterNow - TimeSpan.FromSeconds(60), CheckinInterval);

        GivenFiredTriggersForInstance(DeadInstanceId,
            FiredTrigger("fi-executing", StoredTriggerState.Executing, new TriggerKey("t-executing", "tg"), new JobKey("serial", "jg"), disallowsConcurrentExecution: true));

        await jobStoreSupport.CallClusterRecover(conn, [rec]);

        A.CallTo(() => driverDelegate.DeleteFiredTriggers(
                conn,
                A<FiredTriggerQuery>.That.Matches(query => query.InstanceId == DeadInstanceId),
                A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();

        // With nothing preserved the whole instance is cleared in one statement.
        A.CallTo(() => driverDelegate.DeleteFiredTrigger(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<string>.Ignored,
                A<CancellationToken>.Ignored))
            .MustNotHaveHappened();

        A.CallTo(() => driverDelegate.RepinTriggersFromDeadNode(
                conn, DeadInstanceId, StdAdoConstants.AutoPinSentinel, A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();

        A.CallTo(() => driverDelegate.DeleteSchedulerState(conn, DeadInstanceId, A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task ClusterRecover_ShouldNeverDeferRecoveryForAnOrphanedInstance()
    {
        ConnectionAndTransactionHolder conn = FakeConnection();
        GivenStoppedClock(ClusterNow);

        // FindOrphanedFailedInstances leaves both fields at zero, and that is what tells recovery there
        // is no check-in history to grant a grace period from.
        SchedulerStateRecord orphan = new("ghost-node", CheckinTimestamp: default, CheckinInterval: default);

        GivenFiredTriggersForInstance("ghost-node",
            FiredTrigger("fi-executing", StoredTriggerState.Executing, new TriggerKey("t-executing", "tg"), new JobKey("serial", "jg"), disallowsConcurrentExecution: true, instanceId: "ghost-node"));

        await jobStoreSupport.CallClusterRecover(conn, [orphan]);

        A.CallTo(() => driverDelegate.DeleteFiredTriggers(
                conn,
                A<FiredTriggerQuery>.That.Matches(query => query.InstanceId == "ghost-node"),
                A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();

        A.CallTo(() => driverDelegate.DeleteFiredTrigger(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<string>.Ignored,
                A<CancellationToken>.Ignored))
            .MustNotHaveHappened();

        A.CallTo(() => driverDelegate.DeleteSchedulerState(conn, "ghost-node", A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task ClusterRecover_ShouldDeleteTriggersLeftComplete()
    {
        ConnectionAndTransactionHolder conn = FakeConnection();
        GivenStoppedClock(ClusterNow);

        TriggerKey completed = new("t-complete", "tg");
        GivenFiredTriggersForInstance(DeadInstanceId,
            FiredTrigger("fi-executing", StoredTriggerState.Executing, completed, new JobKey("j", "jg")));

        A.CallTo(() => driverDelegate.SelectTriggerState(conn, completed, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<StoredTriggerState>(StoredTriggerState.Complete));

        A.CallTo(() => driverDelegate.SelectJobForTrigger(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<TriggerKey>.Ignored,
                A<ITypeLoader>.Ignored,
                A<bool>.Ignored,
                A<CancellationToken>.Ignored))
            .Returns(new ValueTask<IJobDetail>((IJobDetail) null));

        A.CallTo(() => driverDelegate.DeleteTrigger(conn, completed, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<int>(1));

        await jobStoreSupport.CallClusterRecover(conn, [DeadNode()]);

        A.CallTo(() => driverDelegate.DeleteTrigger(conn, completed, A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task ClusterRecover_ShouldLeaveItsOwnSchedulerStateRowInPlace()
    {
        ConnectionAndTransactionHolder conn = FakeConnection();
        GivenStoppedClock(ClusterNow);

        // The first check-in recovers this node's own previous run, and that run's state row is the one
        // this node is about to check in against -- deleting it would erase the live registration.
        SchedulerStateRecord own = new(OwnInstanceId, ClusterNow - TimeSpan.FromSeconds(60), CheckinInterval);

        GivenFiredTriggersForInstance(OwnInstanceId,
            FiredTrigger("fi-acquired", StoredTriggerState.Acquired, new TriggerKey("t-acquired", "tg"), instanceId: OwnInstanceId));

        await jobStoreSupport.CallClusterRecover(conn, [own]);

        // The leftover fired rows of the previous run are still cleaned up.
        A.CallTo(() => driverDelegate.DeleteFiredTriggers(
                conn,
                A<FiredTriggerQuery>.That.Matches(query => query.InstanceId == OwnInstanceId),
                A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();

        A.CallTo(() => driverDelegate.DeleteSchedulerState(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<string>.Ignored,
                A<CancellationToken>.Ignored))
            .MustNotHaveHappened();

        // A node does not release its own pins to itself.
        A.CallTo(() => driverDelegate.RepinTriggersFromDeadNode(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<string>.Ignored,
                A<string>.Ignored,
                A<CancellationToken>.Ignored))
            .MustNotHaveHappened();
    }

    [Test]
    public async Task ClusterRecover_ShouldUnblockNonConcurrentJobsOfAFailedInstance()
    {
        ConnectionAndTransactionHolder conn = FakeConnection();
        GivenStoppedClock(ClusterNow);

        JobKey serialJob = new("serial", "jg");
        SchedulerStateRecord rec = new(DeadInstanceId, ClusterNow - TimeSpan.FromSeconds(60), CheckinInterval);

        GivenFiredTriggersForInstance(DeadInstanceId,
            FiredTrigger("fi-executing", StoredTriggerState.Executing, new TriggerKey("t-executing", "tg"), serialJob, disallowsConcurrentExecution: true));

        await jobStoreSupport.CallClusterRecover(conn, [rec]);

        // The siblings the dead firing blocked have to be let go, or the job never runs again --
        // and paused siblings go back to paused rather than to waiting.
        A.CallTo(() => driverDelegate.UpdateTriggerStatesForJobFromOtherState(
                conn, serialJob, StoredTriggerState.Waiting, StoredTriggerState.Blocked, A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();

        A.CallTo(() => driverDelegate.UpdateTriggerStatesForJobFromOtherState(
                conn, serialJob, StoredTriggerState.Paused, StoredTriggerState.PausedBlocked, A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task ClusterRecover_ShouldWrapDelegateFailures()
    {
        ConnectionAndTransactionHolder conn = FakeConnection();
        GivenStoppedClock(ClusterNow);

        A.CallTo(() => driverDelegate.SelectFiredTriggerRecords(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<FiredTriggerQuery>.Ignored,
                A<CancellationToken>.Ignored))
            .Throws(new InvalidOperationException("fired triggers table unavailable"));

        Func<Task> act = async () => await jobStoreSupport.CallClusterRecover(conn, [DeadNode()]);

        await act.Should().ThrowAsync<JobPersistenceException>()
            .WithMessage("*Failure recovering jobs*")
            .WithInnerException<JobPersistenceException, InvalidOperationException>()
            .WithMessage("fired triggers table unavailable");
    }

    /// <summary>
    /// A node whose last check-in is far enough back that the deferred-recovery grace period has run out,
    /// which is the ordinary case: recovery does everything it is going to do.
    /// </summary>
    private static SchedulerStateRecord DeadNode()
    {
        return new SchedulerStateRecord(DeadInstanceId, ClusterNow - TimeSpan.FromSeconds(60), CheckinInterval);
    }

    #endregion

    #endregion
}
