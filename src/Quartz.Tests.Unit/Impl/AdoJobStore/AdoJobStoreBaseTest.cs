using Quartz.Tests;
using System.Data.Common;
using System.Reflection;

using FakeItEasy;


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

        A.CallTo(() => driverDelegate.SelectTriggerState(conn, trigger.Key, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<StoredTriggerState>(StoredTriggerState.Acquired));
        A.CallTo(() => driverDelegate.SelectJobDetail(conn, trigger.JobKey, A<Extensibility.ITypeLoadHelper>.Ignored, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<IJobDetail>(job));
        A.CallTo(() => driverDelegate.IsJobCurrentlyExecuting(conn, trigger.JobKey, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<bool>(true));

        TriggerFiredBundle result = await jobStoreSupport.CallTriggerFired(conn, trigger);

        result.Should().BeNull();
        A.CallTo(() => driverDelegate.UpdateFiredTrigger(
            A<ConnectionAndTransactionHolder>.Ignored,
            A<IOperableTrigger>.Ignored,
            A<StoredTriggerState>.Ignored,
            A<IJobDetail>.Ignored,
            A<CancellationToken>.Ignored)).MustNotHaveHappened();
    }

    [Test]
    public async Task TriggerFired_Proceeds_WhenDisallowConcurrentJobNotExecuting()
    {
        ConnectionAndTransactionHolder conn = new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);
        IOperableTrigger trigger = CreateTestTrigger();
        IJobDetail job = CreateDisallowConcurrentJob();

        A.CallTo(() => driverDelegate.SelectTriggerState(conn, trigger.Key, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<StoredTriggerState>(StoredTriggerState.Acquired));
        A.CallTo(() => driverDelegate.SelectJobDetail(conn, trigger.JobKey, A<Extensibility.ITypeLoadHelper>.Ignored, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<IJobDetail>(job));
        A.CallTo(() => driverDelegate.IsJobCurrentlyExecuting(conn, trigger.JobKey, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<bool>(false));

        TriggerFiredBundle result = await jobStoreSupport.CallTriggerFired(conn, trigger);

        A.CallTo(() => driverDelegate.UpdateFiredTrigger(conn, trigger, StoredTriggerState.Executing, job, A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task TriggerFired_SkipsConcurrencyCheck_WhenConcurrentExecutionAllowed()
    {
        ConnectionAndTransactionHolder conn = new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);
        IOperableTrigger trigger = CreateTestTrigger();
        IJobDetail job = CreateConcurrentJob();

        A.CallTo(() => driverDelegate.SelectTriggerState(conn, trigger.Key, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<StoredTriggerState>(StoredTriggerState.Acquired));
        A.CallTo(() => driverDelegate.SelectJobDetail(conn, trigger.JobKey, A<Extensibility.ITypeLoadHelper>.Ignored, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<IJobDetail>(job));

        await jobStoreSupport.CallTriggerFired(conn, trigger);

        A.CallTo(() => driverDelegate.IsJobCurrentlyExecuting(
            A<ConnectionAndTransactionHolder>.Ignored,
            A<JobKey>.Ignored,
            A<CancellationToken>.Ignored)).MustNotHaveHappened();
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
        A.CallTo(() => driverDelegate.SelectJobDetail(conn, jobKey, A<ITypeLoadHelper>.Ignored, A<CancellationToken>.Ignored))
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
        A.CallTo(() => driverDelegate.SelectJobDetail(conn, jobKey, A<ITypeLoadHelper>.Ignored, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<IJobDetail>(job));

        // Act
        await jobStoreSupport.CallAddCalendar(conn, calendarName, calendar, replace: true, updateTriggers: true);

        // Assert: each trigger should be stored with its own original state
        A.CallTo(() => driverDelegate.UpdateTrigger(conn, pausedTrigger, StoredTriggerState.Paused, job, A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => driverDelegate.UpdateTrigger(conn, waitingTrigger, StoredTriggerState.Waiting, job, A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();
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

    public TestAdoJobStoreBase(bool clustered = false)
        : base(TestJobStores.Signaler(), TestJobStores.TypeLoader(), TimeProvider.System, TestJobStores.SchedulerOptions(), TestJobStores.StoreOptions(), TestJobStores.ClusteringOptions(configure: options => options.Enabled = clustered), TestJobStores.Serializer(), TestJobStores.ConnectionManager(), TestJobStores.DbProvider(), TestJobStores.DriverDelegate(), TestJobStores.LockHandler())
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
            }), TestJobStores.ClusteringOptions(), TestJobStores.Serializer(), TestJobStores.ConnectionManager(), TestJobStores.DbProvider(), TestJobStores.DriverDelegate(), TestJobStores.LockHandler())
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
        A.CallTo(() => del.SelectTriggerState(A<ConnectionAndTransactionHolder>.Ignored, trigger.Key, A<CancellationToken>.Ignored))
            .ReturnsLazily(call =>
            {
                selectStateCallCount++;
                if (selectStateCallCount == 1)
                {
                    throw new TransientTestException();
                }
                return new ValueTask<StoredTriggerState>(StoredTriggerState.Acquired);
            });
        A.CallTo(() => del.SelectJobDetail(A<ConnectionAndTransactionHolder>.Ignored, trigger.JobKey, A<ITypeLoadHelper>.Ignored, A<CancellationToken>.Ignored))
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
        A.CallTo(() => del.SelectTriggerState(A<ConnectionAndTransactionHolder>.Ignored, trigger.Key, A<CancellationToken>.Ignored))
            .ThrowsAsync(new InvalidOperationException("permanent error"));

        List<TriggerFiredResult> results = await store.TriggersFired(new[] { trigger });

        // Non-transient exception should be wrapped in result, not retried
        results.Should().HaveCount(1);
        results[0].TriggerFiredBundle.Should().BeNull();
        results[0].Exception.Should().NotBeNull();
        A.CallTo(() => del.SelectTriggerState(A<ConnectionAndTransactionHolder>.Ignored, trigger.Key, A<CancellationToken>.Ignored))
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
        A.CallTo(() => del.SelectTriggerState(A<ConnectionAndTransactionHolder>.Ignored, trigger.Key, A<CancellationToken>.Ignored))
            .ThrowsAsync(new TransientTestException());

        Func<Task> act = async () => await store.TriggersFired(new[] { trigger });

        await act.Should().ThrowAsync<JobPersistenceException>();
        // Initial attempt + 1 retry = 2 total
        A.CallTo(() => del.SelectTriggerState(A<ConnectionAndTransactionHolder>.Ignored, trigger.Key, A<CancellationToken>.Ignored))
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
        A.CallTo(() => del.SelectTriggerState(A<ConnectionAndTransactionHolder>.Ignored, triggerA.Key, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<StoredTriggerState>(StoredTriggerState.Acquired));

        // Trigger B throws transient on first call, succeeds on retry
        A.CallTo(() => del.SelectTriggerState(A<ConnectionAndTransactionHolder>.Ignored, triggerB.Key, A<CancellationToken>.Ignored))
            .ReturnsLazily(call =>
            {
                triggerBCallCount++;
                if (triggerBCallCount == 1)
                {
                    throw new TransientTestException();
                }
                return new ValueTask<StoredTriggerState>(StoredTriggerState.Acquired);
            });

        A.CallTo(() => del.SelectJobDetail(A<ConnectionAndTransactionHolder>.Ignored, A<JobKey>.Ignored, A<ITypeLoadHelper>.Ignored, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<IJobDetail>(job));

        List<TriggerFiredResult> results = await store.TriggersFired(new[] { triggerA, triggerB });

        // Both triggers should succeed after retry
        results.Should().HaveCount(2);
        results.Should().OnlyContain(r => r.TriggerFiredBundle != null && r.Exception == null);
        // Trigger A was called twice: first attempt (rolled back) + successful retry
        A.CallTo(() => del.SelectTriggerState(A<ConnectionAndTransactionHolder>.Ignored, triggerA.Key, A<CancellationToken>.Ignored))
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
        }), TestJobStores.ClusteringOptions(), TestJobStores.Serializer(), TestJobStores.ConnectionManager(), TestJobStores.DbProvider(), TestJobStores.DriverDelegate(), TestJobStores.LockHandler())
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
        }), TestJobStores.ClusteringOptions(), TestJobStores.Serializer(), TestJobStores.ConnectionManager(), TestJobStores.DbProvider(), TestJobStores.DriverDelegate(), TestJobStores.LockHandler())
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
}
