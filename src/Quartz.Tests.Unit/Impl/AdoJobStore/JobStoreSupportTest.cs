using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using FakeItEasy;

using FluentAssertions;

using NUnit.Framework;

using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.Calendar;
using Quartz.Spi;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

public class JobStoreSupportTest
{
    private TestJobStoreSupport jobStoreSupport;
    private IDriverDelegate driverDelegate;

    [SetUp]
    public void SetUp()
    {
        jobStoreSupport = new TestJobStoreSupport();
        driverDelegate = A.Fake<IDriverDelegate>();
        jobStoreSupport.DirectDelegate = driverDelegate;
    }

    [Test]
    public async Task TestRecoverMisfiredJobs_ShouldCheckForMisfiredTriggersInStateWaiting()
    {
        await jobStoreSupport.RecoverMisfiredJobs(null, false);

        A.CallTo(() => driverDelegate.HasMisfiredTriggersInState(
            A<ConnectionAndTransactionHolder>.Ignored,
            A<string>.That.IsEqualTo(AdoConstants.StateWaiting),
            A<DateTimeOffset>.Ignored,
            A<int>.Ignored,
            A<IList<TriggerKey>>.Ignored,
            CancellationToken.None)).MustHaveHappened();
    }

    [Test]
    public async Task TestRemoveTrigger_ShouldDeleteFiredTriggersForTriggerKey()
    {
        var triggerKey = new TriggerKey("testTrigger", "testGroup");
        var conn = new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);

        var firedRecord = new FiredTriggerRecord
        {
            FireInstanceId = "entry_123"
        };

        A.CallTo(() => driverDelegate.SelectJobForTrigger(
            A<ConnectionAndTransactionHolder>.Ignored,
            triggerKey,
            A<Spi.ITypeLoadHelper>.Ignored,
            A<bool>.Ignored,
            A<CancellationToken>.Ignored)).Returns(Task.FromResult<IJobDetail>(null));

        A.CallTo(() => driverDelegate.SelectFiredTriggerRecords(
            A<ConnectionAndTransactionHolder>.Ignored,
            triggerKey.Name,
            triggerKey.Group,
            A<CancellationToken>.Ignored)).Returns(Task.FromResult<IReadOnlyCollection<FiredTriggerRecord>>(new[] { firedRecord }));

        A.CallTo(() => driverDelegate.DeleteFiredTrigger(
            A<ConnectionAndTransactionHolder>.Ignored,
            "entry_123",
            A<CancellationToken>.Ignored)).Returns(Task.FromResult(1));

        A.CallTo(() => driverDelegate.DeleteTrigger(
            A<ConnectionAndTransactionHolder>.Ignored,
            triggerKey,
            A<CancellationToken>.Ignored)).Returns(Task.FromResult(1));

        await jobStoreSupport.CallRemoveTrigger(conn, triggerKey);

        A.CallTo(() => driverDelegate.SelectFiredTriggerRecords(
            A<ConnectionAndTransactionHolder>.Ignored,
            triggerKey.Name,
            triggerKey.Group,
            A<CancellationToken>.Ignored)).MustHaveHappenedOnceExactly();

        A.CallTo(() => driverDelegate.DeleteFiredTrigger(
            A<ConnectionAndTransactionHolder>.Ignored,
            "entry_123",
            A<CancellationToken>.Ignored)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task TestRemoveJob_ShouldDeleteFiredTriggersForJobKey()
    {
        // Arrange: delegate implements INextVersionDelegate
        IDriverDelegate nvDelegate = A.Fake<IDriverDelegate>(x => x.Implements<INextVersionDelegate>());
        jobStoreSupport.DirectDelegate = nvDelegate;

        var jobKey = new JobKey("testJob", "testGroup");
        var conn = new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);

        // No triggers exist in QRTZ_TRIGGERS for this job
        A.CallTo(() => nvDelegate.SelectTriggerNamesForJob(
            A<ConnectionAndTransactionHolder>.Ignored,
            jobKey,
            A<CancellationToken>.Ignored)).Returns(Task.FromResult<IReadOnlyCollection<TriggerKey>>(Array.Empty<TriggerKey>()));

        A.CallTo(() => nvDelegate.DeleteJobDetail(
            A<ConnectionAndTransactionHolder>.Ignored,
            jobKey,
            A<CancellationToken>.Ignored)).Returns(Task.FromResult(1));

        // Act
        await jobStoreSupport.CallRemoveJob(conn, jobKey);

        // Assert: fired triggers for this job key should be cleaned up
        A.CallTo(() => ((INextVersionDelegate) nvDelegate).DeleteFiredTriggers(
            A<ConnectionAndTransactionHolder>.Ignored,
            jobKey,
            A<CancellationToken>.Ignored)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task TestRemoveJob_FallbackPath_ShouldDeleteFiredTriggersForJobKey()
    {
        // Arrange: plain IDriverDelegate (does NOT implement INextVersionDelegate)
        var jobKey = new JobKey("testJob", "testGroup");
        var conn = new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);

        // No triggers exist in QRTZ_TRIGGERS for this job
        A.CallTo(() => driverDelegate.SelectTriggerNamesForJob(
            A<ConnectionAndTransactionHolder>.Ignored,
            jobKey,
            A<CancellationToken>.Ignored)).Returns(Task.FromResult<IReadOnlyCollection<TriggerKey>>(Array.Empty<TriggerKey>()));

        var firedRecord = new FiredTriggerRecord
        {
            FireInstanceId = "entry_456"
        };

        A.CallTo(() => driverDelegate.SelectFiredTriggerRecordsByJob(
            A<ConnectionAndTransactionHolder>.Ignored,
            jobKey.Name,
            jobKey.Group,
            A<CancellationToken>.Ignored)).Returns(Task.FromResult<IReadOnlyCollection<FiredTriggerRecord>>(new[] { firedRecord }));

        A.CallTo(() => driverDelegate.DeleteFiredTrigger(
            A<ConnectionAndTransactionHolder>.Ignored,
            "entry_456",
            A<CancellationToken>.Ignored)).Returns(Task.FromResult(1));

        A.CallTo(() => driverDelegate.DeleteJobDetail(
            A<ConnectionAndTransactionHolder>.Ignored,
            jobKey,
            A<CancellationToken>.Ignored)).Returns(Task.FromResult(1));

        // Act
        await jobStoreSupport.CallRemoveJob(conn, jobKey);

        // Assert: fallback path should select then delete individual fired triggers
        A.CallTo(() => driverDelegate.SelectFiredTriggerRecordsByJob(
            A<ConnectionAndTransactionHolder>.Ignored,
            jobKey.Name,
            jobKey.Group,
            A<CancellationToken>.Ignored)).MustHaveHappenedOnceExactly();

        A.CallTo(() => driverDelegate.DeleteFiredTrigger(
            A<ConnectionAndTransactionHolder>.Ignored,
            "entry_456",
            A<CancellationToken>.Ignored)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task TestExecuteInNonManagedTXLock_RetriesOnTransientException()
    {
        int callCount = 0;
        var store = CreateRetryTestStore();

        // Callback fails with transient exception on first call, succeeds on second
        string result = await store.CallExecuteInNonManagedTXLock<string>(conn =>
        {
            callCount++;
            if (callCount == 1)
            {
                throw new JobPersistenceException("transient", new TransientTestException());
            }
            return Task.FromResult("success");
        }, CancellationToken.None);

        result.Should().Be("success");
        callCount.Should().Be(2);
    }

    [Test]
    public async Task TestExecuteInNonManagedTXLock_StopsRetryingAfterMaxRetries()
    {
        int callCount = 0;
        var store = CreateRetryTestStore(maxTransientRetries: 2);

        // Callback always throws transient exception
        Func<Task> act = async () => await store.CallExecuteInNonManagedTXLock<string>(conn =>
        {
            callCount++;
            throw new JobPersistenceException("transient", new TransientTestException());
        }, CancellationToken.None);

        await act.Should().ThrowAsync<JobPersistenceException>();
        // Initial attempt + 2 retries = 3 total
        callCount.Should().Be(3);
    }

    [Test]
    public async Task TestExecuteInNonManagedTXLock_DoesNotRetryNonTransientException()
    {
        int callCount = 0;
        var store = CreateRetryTestStore();

        // Non-transient exception should not be retried
        Func<Task> act = async () => await store.CallExecuteInNonManagedTXLock<string>(conn =>
        {
            callCount++;
            throw new JobPersistenceException("non-transient");
        }, CancellationToken.None);

        await act.Should().ThrowAsync<JobPersistenceException>().WithMessage("non-transient");
        callCount.Should().Be(1);
    }

    [Test]
    public async Task TestExecuteInNonManagedTXLock_NoRetryWhenMaxTransientRetriesIsZero()
    {
        int callCount = 0;
        var store = CreateRetryTestStore(maxTransientRetries: 0);

        // With MaxTransientRetries = 0, transient exceptions should not be retried
        Func<Task> act = async () => await store.CallExecuteInNonManagedTXLock<string>(conn =>
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
        // Arrange: delegate implements INextVersionDelegate
        IDriverDelegate nvDelegate = A.Fake<IDriverDelegate>(x => x.Implements<INextVersionDelegate>());
        jobStoreSupport.DirectDelegate = nvDelegate;

        ConnectionAndTransactionHolder conn = new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);
        IOperableTrigger trigger = CreateTestTrigger();
        IJobDetail job = CreateDisallowConcurrentJob();

        A.CallTo(() => nvDelegate.SelectTriggerState(conn, trigger.Key, A<CancellationToken>.Ignored))
            .Returns(Task.FromResult(AdoConstants.StateAcquired));
        A.CallTo(() => nvDelegate.SelectJobDetail(conn, trigger.JobKey, A<ITypeLoadHelper>.Ignored, A<CancellationToken>.Ignored))
            .Returns(Task.FromResult<IJobDetail>(job));
        A.CallTo(() => ((INextVersionDelegate) nvDelegate).IsJobCurrentlyExecuting(conn, trigger.JobKey.Name, trigger.JobKey.Group, A<CancellationToken>.Ignored))
            .Returns(Task.FromResult(true));

        // Act
        TriggerFiredBundle result = await jobStoreSupport.CallTriggerFired(conn, trigger);

        // Assert: should return null and NOT proceed to UpdateFiredTrigger
        result.Should().BeNull();
        A.CallTo(() => nvDelegate.UpdateFiredTrigger(
            A<ConnectionAndTransactionHolder>.Ignored,
            A<IOperableTrigger>.Ignored,
            A<string>.Ignored,
            A<IJobDetail>.Ignored,
            A<CancellationToken>.Ignored)).MustNotHaveHappened();
    }

    [Test]
    public async Task TriggerFired_Proceeds_WhenDisallowConcurrentJobNotExecuting()
    {
        // Arrange: delegate implements INextVersionDelegate
        IDriverDelegate nvDelegate = A.Fake<IDriverDelegate>(x => x.Implements<INextVersionDelegate>());
        jobStoreSupport.DirectDelegate = nvDelegate;

        ConnectionAndTransactionHolder conn = new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);
        IOperableTrigger trigger = CreateTestTrigger();
        IJobDetail job = CreateDisallowConcurrentJob();

        A.CallTo(() => nvDelegate.SelectTriggerState(conn, trigger.Key, A<CancellationToken>.Ignored))
            .Returns(Task.FromResult(AdoConstants.StateAcquired));
        A.CallTo(() => nvDelegate.SelectJobDetail(conn, trigger.JobKey, A<ITypeLoadHelper>.Ignored, A<CancellationToken>.Ignored))
            .Returns(Task.FromResult<IJobDetail>(job));
        A.CallTo(() => ((INextVersionDelegate) nvDelegate).IsJobCurrentlyExecuting(conn, trigger.JobKey.Name, trigger.JobKey.Group, A<CancellationToken>.Ignored))
            .Returns(Task.FromResult(false));

        // Act
        TriggerFiredBundle result = await jobStoreSupport.CallTriggerFired(conn, trigger);

        // Assert: check passed, UpdateFiredTrigger must have been called
        A.CallTo(() => nvDelegate.UpdateFiredTrigger(conn, trigger, AdoConstants.StateExecuting, job, A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task TriggerFired_SkipsConcurrencyCheck_WhenConcurrentExecutionAllowed()
    {
        // Arrange: job allows concurrent execution
        IDriverDelegate nvDelegate = A.Fake<IDriverDelegate>(x => x.Implements<INextVersionDelegate>());
        jobStoreSupport.DirectDelegate = nvDelegate;

        ConnectionAndTransactionHolder conn = new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);
        IOperableTrigger trigger = CreateTestTrigger();
        IJobDetail job = CreateConcurrentJob();

        A.CallTo(() => nvDelegate.SelectTriggerState(conn, trigger.Key, A<CancellationToken>.Ignored))
            .Returns(Task.FromResult(AdoConstants.StateAcquired));
        A.CallTo(() => nvDelegate.SelectJobDetail(conn, trigger.JobKey, A<ITypeLoadHelper>.Ignored, A<CancellationToken>.Ignored))
            .Returns(Task.FromResult<IJobDetail>(job));

        // Act
        await jobStoreSupport.CallTriggerFired(conn, trigger);

        // Assert: IsJobCurrentlyExecuting should NOT be called
        A.CallTo(() => ((INextVersionDelegate) nvDelegate).IsJobCurrentlyExecuting(
            A<ConnectionAndTransactionHolder>.Ignored,
            A<string>.Ignored,
            A<string>.Ignored,
            A<CancellationToken>.Ignored)).MustNotHaveHappened();
    }

    [Test]
    public async Task TriggerFired_FallsBackToSelectFiredTriggers_WhenNotNextVersionDelegate()
    {
        // Arrange: plain IDriverDelegate (does NOT implement INextVersionDelegate)
        ConnectionAndTransactionHolder conn = new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);
        IOperableTrigger trigger = CreateTestTrigger();
        IJobDetail job = CreateDisallowConcurrentJob();

        A.CallTo(() => driverDelegate.SelectTriggerState(conn, trigger.Key, A<CancellationToken>.Ignored))
            .Returns(Task.FromResult(AdoConstants.StateAcquired));
        A.CallTo(() => driverDelegate.SelectJobDetail(conn, trigger.JobKey, A<ITypeLoadHelper>.Ignored, A<CancellationToken>.Ignored))
            .Returns(Task.FromResult<IJobDetail>(job));

        // Fallback path: SelectFiredTriggerRecordsByJob returns an EXECUTING record
        FiredTriggerRecord executingRecord = new FiredTriggerRecord
        {
            FireInstanceId = "other-fire-id",
            FireInstanceState = AdoConstants.StateExecuting,
            SchedulerInstanceId = "other-instance",
        };
        A.CallTo(() => driverDelegate.SelectFiredTriggerRecordsByJob(conn, trigger.JobKey.Name, trigger.JobKey.Group, A<CancellationToken>.Ignored))
            .Returns(Task.FromResult<IReadOnlyCollection<FiredTriggerRecord>>(new[] { executingRecord }));

        // Act
        TriggerFiredBundle result = await jobStoreSupport.CallTriggerFired(conn, trigger);

        // Assert: should use fallback and still block
        result.Should().BeNull();
        A.CallTo(() => driverDelegate.SelectFiredTriggerRecordsByJob(conn, trigger.JobKey.Name, trigger.JobKey.Group, A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => driverDelegate.UpdateFiredTrigger(
            A<ConnectionAndTransactionHolder>.Ignored,
            A<IOperableTrigger>.Ignored,
            A<string>.Ignored,
            A<IJobDetail>.Ignored,
            A<CancellationToken>.Ignored)).MustNotHaveHappened();
    }

    [Test]
    public async Task StoreTrigger_PreservesPreviousFireTimeUtc_WhenReplacingExistingTrigger()
    {
        // Arrange
        var triggerKey = new TriggerKey("t1", "g1");
        var jobKey = new JobKey("j1", "jg1");
        var conn = new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);
        DateTimeOffset previousFireTime = new DateTimeOffset(2024, 6, 15, 10, 0, 0, TimeSpan.Zero);

        IJobDetail job = JobBuilder.Create<ConcurrentTestJob>()
            .WithIdentity(jobKey)
            .Build();

        // The new trigger (as would be constructed during app restart) has null PreviousFireTimeUtc
        IOperableTrigger newTrigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .StartNow()
            .WithSimpleSchedule(x => x.WithIntervalInHours(1).RepeatForever())
            .Build();

        // The existing trigger in the DB has PreviousFireTimeUtc set from a prior execution
        IOperableTrigger existingTrigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .StartNow()
            .WithSimpleSchedule(x => x.WithIntervalInHours(1).RepeatForever())
            .Build();
        existingTrigger.SetPreviousFireTimeUtc(previousFireTime);

        // TriggerExists returns true (trigger already in DB)
        A.CallTo(() => driverDelegate.TriggerExists(conn, triggerKey, A<CancellationToken>.Ignored))
            .Returns(Task.FromResult(true));

        // IsTriggerGroupPaused returns false
        A.CallTo(() => driverDelegate.IsTriggerGroupPaused(conn, A<string>.Ignored, A<CancellationToken>.Ignored))
            .Returns(Task.FromResult(false));

        // SelectTrigger returns the existing trigger with PreviousFireTimeUtc set
        A.CallTo(() => driverDelegate.SelectTrigger(conn, triggerKey, A<CancellationToken>.Ignored))
            .Returns(Task.FromResult((IOperableTrigger) existingTrigger));

        // Act
        await jobStoreSupport.CallStoreTrigger(conn, newTrigger, job, replaceExisting: true);

        // Assert: PreviousFireTimeUtc should be preserved from the existing trigger
        newTrigger.GetPreviousFireTimeUtc().Should().Be(previousFireTime,
            "PreviousFireTimeUtc should be preserved from the existing trigger when replacing (#1834)");

        // Verify UpdateTrigger was called (not InsertTrigger)
        A.CallTo(() => driverDelegate.UpdateTrigger(conn, newTrigger, A<string>.Ignored, job, A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task StoreTrigger_DoesNotOverridePreviousFireTimeUtc_WhenNewTriggerAlreadyHasIt()
    {
        // Arrange
        var triggerKey = new TriggerKey("t1", "g1");
        var jobKey = new JobKey("j1", "jg1");
        var conn = new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);
        DateTimeOffset newPreviousFireTime = new DateTimeOffset(2024, 7, 1, 12, 0, 0, TimeSpan.Zero);

        IJobDetail job = JobBuilder.Create<ConcurrentTestJob>()
            .WithIdentity(jobKey)
            .Build();

        // The new trigger already has PreviousFireTimeUtc set
        IOperableTrigger newTrigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .StartNow()
            .WithSimpleSchedule(x => x.WithIntervalInHours(1).RepeatForever())
            .Build();
        newTrigger.SetPreviousFireTimeUtc(newPreviousFireTime);

        // TriggerExists returns true
        A.CallTo(() => driverDelegate.TriggerExists(conn, triggerKey, A<CancellationToken>.Ignored))
            .Returns(Task.FromResult(true));

        A.CallTo(() => driverDelegate.IsTriggerGroupPaused(conn, A<string>.Ignored, A<CancellationToken>.Ignored))
            .Returns(Task.FromResult(false));

        // Act
        await jobStoreSupport.CallStoreTrigger(conn, newTrigger, job, replaceExisting: true);

        // Assert: should keep the new trigger's own PreviousFireTimeUtc, not query the existing one
        newTrigger.GetPreviousFireTimeUtc().Should().Be(newPreviousFireTime);

        // SelectTrigger should NOT have been called since newTrigger already had PreviousFireTimeUtc
        A.CallTo(() => driverDelegate.SelectTrigger(conn, triggerKey, A<CancellationToken>.Ignored))
            .MustNotHaveHappened();
    }

    [TestCase(AdoConstants.StatePaused)]
    [TestCase(AdoConstants.StateBlocked)]
    [TestCase(AdoConstants.StatePausedBlocked)]
    [TestCase(AdoConstants.StateWaiting)]
    [TestCase(AdoConstants.StateComplete)]
    [TestCase(AdoConstants.StateError)]
    public async Task StoreCalendar_PreservesTriggerState_WhenUpdatingTriggers(string originalState)
    {
        // Arrange
        var conn = new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);
        var calName = "testCal";
        ICalendar calendar = new BaseCalendar();
        var triggerKey = new TriggerKey("t1", "g1");
        var jobKey = new JobKey("j1", "jg1");

        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .StartNow()
            .WithSimpleSchedule(x => x.WithIntervalInHours(1).RepeatForever())
            .Build();

        IJobDetail job = JobBuilder.Create<ConcurrentTestJob>()
            .WithIdentity(jobKey)
            .Build();

        A.CallTo(() => driverDelegate.CalendarExists(conn, calName, A<CancellationToken>.Ignored))
            .Returns(Task.FromResult(true));
        A.CallTo(() => driverDelegate.UpdateCalendar(conn, calName, calendar, A<CancellationToken>.Ignored))
            .Returns(Task.FromResult(1));
        A.CallTo(() => driverDelegate.SelectTriggersForCalendar(conn, calName, A<CancellationToken>.Ignored))
            .Returns(Task.FromResult<IReadOnlyCollection<IOperableTrigger>>(new[] { trigger }));
        A.CallTo(() => driverDelegate.SelectTriggerState(conn, triggerKey, A<CancellationToken>.Ignored))
            .Returns(Task.FromResult(originalState));
        A.CallTo(() => driverDelegate.TriggerExists(conn, triggerKey, A<CancellationToken>.Ignored))
            .Returns(Task.FromResult(true));
        A.CallTo(() => driverDelegate.SelectJobDetail(conn, jobKey, A<ITypeLoadHelper>.Ignored, A<CancellationToken>.Ignored))
            .Returns(Task.FromResult<IJobDetail>(job));

        // Act
        await jobStoreSupport.CallStoreCalendar(conn, calName, calendar, replaceExisting: true, updateTriggers: true);

        // Assert: UpdateTrigger should be called with the original state preserved
        A.CallTo(() => driverDelegate.UpdateTrigger(conn, trigger, originalState, job, A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task StoreCalendar_SkipsDeletedTriggers_WhenUpdatingTriggers()
    {
        // Arrange
        var conn = new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);
        var calName = "testCal";
        ICalendar calendar = new BaseCalendar();
        var triggerKey = new TriggerKey("t1", "g1");
        var jobKey = new JobKey("j1", "jg1");

        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .StartNow()
            .WithSimpleSchedule(x => x.WithIntervalInHours(1).RepeatForever())
            .Build();

        A.CallTo(() => driverDelegate.CalendarExists(conn, calName, A<CancellationToken>.Ignored))
            .Returns(Task.FromResult(true));
        A.CallTo(() => driverDelegate.UpdateCalendar(conn, calName, calendar, A<CancellationToken>.Ignored))
            .Returns(Task.FromResult(1));
        A.CallTo(() => driverDelegate.SelectTriggersForCalendar(conn, calName, A<CancellationToken>.Ignored))
            .Returns(Task.FromResult<IReadOnlyCollection<IOperableTrigger>>(new[] { trigger }));
        A.CallTo(() => driverDelegate.SelectTriggerState(conn, triggerKey, A<CancellationToken>.Ignored))
            .Returns(Task.FromResult(AdoConstants.StateDeleted));

        // Act
        await jobStoreSupport.CallStoreCalendar(conn, calName, calendar, replaceExisting: true, updateTriggers: true);

        // Assert: trigger in DELETED state should be skipped entirely
        A.CallTo(() => driverDelegate.UpdateTrigger(
            A<ConnectionAndTransactionHolder>.Ignored,
            A<IOperableTrigger>.Ignored,
            A<string>.Ignored,
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
        var calName = "testCal";
        ICalendar calendar = new BaseCalendar();
        var jobKey = new JobKey("j1", "jg1");

        var pausedTriggerKey = new TriggerKey("paused", "g1");
        IOperableTrigger pausedTrigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(pausedTriggerKey)
            .ForJob(jobKey)
            .StartNow()
            .WithSimpleSchedule(x => x.WithIntervalInHours(1).RepeatForever())
            .Build();

        var waitingTriggerKey = new TriggerKey("waiting", "g1");
        IOperableTrigger waitingTrigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(waitingTriggerKey)
            .ForJob(jobKey)
            .StartNow()
            .WithSimpleSchedule(x => x.WithIntervalInHours(1).RepeatForever())
            .Build();

        IJobDetail job = JobBuilder.Create<ConcurrentTestJob>()
            .WithIdentity(jobKey)
            .Build();

        A.CallTo(() => driverDelegate.CalendarExists(conn, calName, A<CancellationToken>.Ignored))
            .Returns(Task.FromResult(true));
        A.CallTo(() => driverDelegate.UpdateCalendar(conn, calName, calendar, A<CancellationToken>.Ignored))
            .Returns(Task.FromResult(1));
        A.CallTo(() => driverDelegate.SelectTriggersForCalendar(conn, calName, A<CancellationToken>.Ignored))
            .Returns(Task.FromResult<IReadOnlyCollection<IOperableTrigger>>(new[] { pausedTrigger, waitingTrigger }));

        A.CallTo(() => driverDelegate.SelectTriggerState(conn, pausedTriggerKey, A<CancellationToken>.Ignored))
            .Returns(Task.FromResult(AdoConstants.StatePaused));
        A.CallTo(() => driverDelegate.SelectTriggerState(conn, waitingTriggerKey, A<CancellationToken>.Ignored))
            .Returns(Task.FromResult(AdoConstants.StateWaiting));

        A.CallTo(() => driverDelegate.TriggerExists(conn, A<TriggerKey>.Ignored, A<CancellationToken>.Ignored))
            .Returns(Task.FromResult(true));
        A.CallTo(() => driverDelegate.SelectJobDetail(conn, jobKey, A<ITypeLoadHelper>.Ignored, A<CancellationToken>.Ignored))
            .Returns(Task.FromResult<IJobDetail>(job));

        // Act
        await jobStoreSupport.CallStoreCalendar(conn, calName, calendar, replaceExisting: true, updateTriggers: true);

        // Assert: each trigger should be stored with its own original state
        A.CallTo(() => driverDelegate.UpdateTrigger(conn, pausedTrigger, AdoConstants.StatePaused, job, A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => driverDelegate.UpdateTrigger(conn, waitingTrigger, AdoConstants.StateWaiting, job, A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();
    }

    private static IOperableTrigger CreateTestTrigger(string name = "t1", string fireInstanceId = "test-fire-id")
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(name, "g1")
            .ForJob("j1", "jg1")
            .StartNow()
            .WithSimpleSchedule(x => x.WithIntervalInHours(1).RepeatForever())
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

    [DisallowConcurrentExecution]
    private class DisallowConcurrentTestJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }

    private class ConcurrentTestJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }

    private static RetryTestJobStoreSupport CreateRetryTestStore(int maxTransientRetries = 3)
    {
        return new RetryTestJobStoreSupport
        {
            MaxTransientRetries = maxTransientRetries,
            TransientRetryInterval = TimeSpan.Zero,
        };
    }

    public class TestJobStoreSupport : JobStoreSupport
    {
        protected override ConnectionAndTransactionHolder GetNonManagedTXConnection()
        {
            return new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);
        }

        protected override Task<T> ExecuteInLock<T>(
            string lockName,
            Func<ConnectionAndTransactionHolder, Task<T>> txCallback,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(default(T));
        }

        /// <summary>
        /// sets delegate directly
        /// </summary>
        internal IDriverDelegate DirectDelegate
        {
            set
            {
                FieldInfo fieldInfo = typeof(JobStoreSupport).GetField("driverDelegate", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(fieldInfo);
                fieldInfo.SetValue(this, value);
            }
        }

        internal Task<bool> CallRemoveTrigger(ConnectionAndTransactionHolder conn, TriggerKey triggerKey)
        {
            return RemoveTrigger(conn, triggerKey, CancellationToken.None);
        }

        internal Task<TriggerFiredBundle> CallTriggerFired(ConnectionAndTransactionHolder conn, IOperableTrigger trigger)
        {
            return TriggerFired(conn, trigger, CancellationToken.None);
        }

        internal Task<bool> CallRemoveJob(ConnectionAndTransactionHolder conn, JobKey jobKey)
        {
            return RemoveJob(conn, jobKey, true, CancellationToken.None);
        }

        internal Task CallStoreTrigger(
            ConnectionAndTransactionHolder conn,
            IOperableTrigger newTrigger,
            IJobDetail job,
            bool replaceExisting)
        {
            return StoreTrigger(conn, newTrigger, job, replaceExisting, AdoConstants.StateWaiting, false, false, CancellationToken.None);
        }

        internal Task CallStoreCalendar(
            ConnectionAndTransactionHolder conn,
            string calName,
            ICalendar calendar,
            bool replaceExisting,
            bool updateTriggers)
        {
            return StoreCalendar(conn, calName, calendar, replaceExisting, updateTriggers, CancellationToken.None);
        }
    }

    /// <summary>
    /// A <see cref="JobStoreSupport"/> subclass used to test retry logic in
    /// <see cref="JobStoreSupport.ExecuteInNonManagedTXLock{T}"/>.
    /// </summary>
    public sealed class RetryTestJobStoreSupport : JobStoreSupport
    {
        protected override ConnectionAndTransactionHolder GetNonManagedTXConnection()
        {
            // Return a holder with a mock connection and no transaction
            return new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);
        }

        protected override Task<T> ExecuteInLock<T>(
            string lockName,
            Func<ConnectionAndTransactionHolder, Task<T>> txCallback,
            CancellationToken cancellationToken = default)
        {
            return ExecuteInNonManagedTXLock(lockName, txCallback, cancellationToken);
        }

        protected override bool IsTransient(Exception ex)
        {
            // Mark JobPersistenceException wrapping TransientTestException as transient
            return ex is JobPersistenceException { InnerException: TransientTestException };
        }

        public Task<T> CallExecuteInNonManagedTXLock<T>(
            Func<ConnectionAndTransactionHolder, Task<T>> txCallback,
            CancellationToken cancellationToken)
        {
            return ExecuteInNonManagedTXLock(null, txCallback, cancellationToken);
        }
    }

    /// <summary>
    /// A test exception that will be recognized as transient by <see cref="RetryTestJobStoreSupport"/>.
    /// </summary>
    public sealed class TransientTestException : Exception
    {
        public TransientTestException() : base("Simulated transient database error (e.g. deadlock)")
        {
        }
    }

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
        // which IsTransient recognizes, enabling the retry in ExecuteInNonManagedTXLock.
        A.CallTo(() => del.SelectTriggerState(A<ConnectionAndTransactionHolder>.Ignored, trigger.Key, A<CancellationToken>.Ignored))
            .ReturnsLazily(call =>
            {
                selectStateCallCount++;
                if (selectStateCallCount == 1)
                {
                    throw new TransientTestException();
                }
                return Task.FromResult(AdoConstants.StateAcquired);
            });
        A.CallTo(() => del.SelectJobDetail(A<ConnectionAndTransactionHolder>.Ignored, trigger.JobKey, A<ITypeLoadHelper>.Ignored, A<CancellationToken>.Ignored))
            .Returns(Task.FromResult<IJobDetail>(job));

        IReadOnlyCollection<TriggerFiredResult> results = await store.TriggersFired(new[] { trigger });

        results.Should().HaveCount(1);
        results.First().TriggerFiredBundle.Should().NotBeNull();
        results.First().Exception.Should().BeNull();
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

        IReadOnlyCollection<TriggerFiredResult> results = await store.TriggersFired(new[] { trigger });

        // Non-transient exception should be wrapped in result, not retried
        results.Should().HaveCount(1);
        results.First().TriggerFiredBundle.Should().BeNull();
        results.First().Exception.Should().NotBeNull();
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
        DateTimeOffset? originalNextFireTime = triggerA.GetNextFireTimeUtc();
        DateTimeOffset? originalPrevFireTime = triggerA.GetPreviousFireTimeUtc();

        // Trigger A always succeeds — but its work is rolled back when B fails
        A.CallTo(() => del.SelectTriggerState(A<ConnectionAndTransactionHolder>.Ignored, triggerA.Key, A<CancellationToken>.Ignored))
            .Returns(Task.FromResult(AdoConstants.StateAcquired));

        // Trigger B throws transient on first call, succeeds on retry
        A.CallTo(() => del.SelectTriggerState(A<ConnectionAndTransactionHolder>.Ignored, triggerB.Key, A<CancellationToken>.Ignored))
            .ReturnsLazily(call =>
            {
                triggerBCallCount++;
                if (triggerBCallCount == 1)
                {
                    throw new TransientTestException();
                }
                return Task.FromResult(AdoConstants.StateAcquired);
            });

        A.CallTo(() => del.SelectJobDetail(A<ConnectionAndTransactionHolder>.Ignored, A<JobKey>.Ignored, A<ITypeLoadHelper>.Ignored, A<CancellationToken>.Ignored))
            .Returns(Task.FromResult<IJobDetail>(job));

        IReadOnlyCollection<TriggerFiredResult> results = await store.TriggersFired(new[] { triggerA, triggerB });

        // Both triggers should succeed after retry
        results.Should().HaveCount(2);
        results.Should().OnlyContain(r => r.TriggerFiredBundle != null && r.Exception == null);
        // Trigger A was called twice: first attempt (rolled back) + successful retry
        A.CallTo(() => del.SelectTriggerState(A<ConnectionAndTransactionHolder>.Ignored, triggerA.Key, A<CancellationToken>.Ignored))
            .MustHaveHappened(2, Times.Exactly);
        triggerBCallCount.Should().Be(2);

        // Verify original trigger objects were NOT mutated (clone protects against
        // double-mutation from trigger.Triggered() across retry attempts)
        triggerA.GetNextFireTimeUtc().Should().Be(originalNextFireTime,
            "original trigger must not be mutated by TriggersFired — clones should be used");
        triggerA.GetPreviousFireTimeUtc().Should().Be(originalPrevFireTime,
            "original trigger must not be mutated by TriggersFired — clones should be used");
    }

    private static TransientTriggersFiredTestStore CreateTransientTriggersFiredTestStore(int maxTransientRetries = 3)
    {
        return new TransientTriggersFiredTestStore
        {
            MaxTransientRetries = maxTransientRetries,
        };
    }

    /// <summary>
    /// A <see cref="JobStoreSupport"/> subclass used to test transient retry logic
    /// in the <see cref="JobStoreSupport.TriggersFired"/> method.
    /// </summary>
    public sealed class TransientTriggersFiredTestStore : JobStoreSupport
    {
        public TransientTriggersFiredTestStore()
        {
            LockHandler = new SimpleSemaphore();
            TransientRetryInterval = TimeSpan.Zero;
        }

        protected override ConnectionAndTransactionHolder GetNonManagedTXConnection()
        {
            return new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);
        }

        protected override Task<T> ExecuteInLock<T>(
            string lockName,
            Func<ConnectionAndTransactionHolder, Task<T>> txCallback,
            CancellationToken cancellationToken = default)
        {
            return ExecuteInNonManagedTXLock(lockName, txCallback, cancellationToken);
        }

        protected override bool IsTransient(Exception ex)
        {
            return ex is JobPersistenceException { InnerException: TransientTestException };
        }

        internal IDriverDelegate DirectDelegate
        {
            set
            {
                FieldInfo fieldInfo = typeof(JobStoreSupport).GetField("driverDelegate", BindingFlags.Instance | BindingFlags.NonPublic)!;
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
            .Returns(Task.FromResult<IReadOnlyCollection<SchedulerStateRecord>>(new List<SchedulerStateRecord>
            {
                new SchedulerStateRecord { SchedulerInstanceId = store.InstanceId, CheckinTimestamp = SystemTime.UtcNow(), CheckinInterval = TimeSpan.FromSeconds(15) }
            }));

        A.CallTo(() => del.UpdateSchedulerState(A<ConnectionAndTransactionHolder>.Ignored, A<string>.Ignored, A<DateTimeOffset>.Ignored, A<CancellationToken>.Ignored))
            .ReturnsLazily(call =>
            {
                updateCallCount++;
                if (updateCallCount == 1)
                {
                    throw new TransientTestException();
                }
                return Task.FromResult(1);
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
            .Returns(Task.FromResult<IReadOnlyCollection<SchedulerStateRecord>>(new List<SchedulerStateRecord>
            {
                new SchedulerStateRecord { SchedulerInstanceId = store.InstanceId, CheckinTimestamp = SystemTime.UtcNow(), CheckinInterval = TimeSpan.FromSeconds(15) }
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
            .Returns(Task.FromResult<IReadOnlyCollection<SchedulerStateRecord>>(new List<SchedulerStateRecord>
            {
                new SchedulerStateRecord { SchedulerInstanceId = store.InstanceId, CheckinTimestamp = SystemTime.UtcNow(), CheckinInterval = TimeSpan.FromSeconds(15) }
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
            .Returns(Task.FromResult<IReadOnlyCollection<SchedulerStateRecord>>(new List<SchedulerStateRecord>
            {
                new SchedulerStateRecord { SchedulerInstanceId = store.InstanceId, CheckinTimestamp = SystemTime.UtcNow(), CheckinInterval = TimeSpan.FromSeconds(15) }
            }));

        A.CallTo(() => del.UpdateSchedulerState(A<ConnectionAndTransactionHolder>.Ignored, A<string>.Ignored, A<DateTimeOffset>.Ignored, A<CancellationToken>.Ignored))
            .ReturnsLazily(call =>
            {
                updateCallCount++;
                if (updateCallCount == 1)
                {
                    throw new TransientTestException();
                }
                return Task.FromResult(1);
            });

        await store.DoCheckin(Guid.NewGuid());

        store.LastCheckin.Should().BeAfter(initialCheckin, "LastCheckin should advance after successful check-in");
    }

    private static TransientDoCheckinTestStore CreateTransientDoCheckinTestStore(int maxTransientRetries = 3)
    {
        return new TransientDoCheckinTestStore
        {
            MaxTransientRetries = maxTransientRetries,
        };
    }

    /// <summary>
    /// A <see cref="JobStoreSupport"/> subclass used to test transient retry logic
    /// in the <see cref="JobStoreSupport.DoCheckin"/> method.
    /// </summary>
    public sealed class TransientDoCheckinTestStore : JobStoreSupport
    {
        public TransientDoCheckinTestStore()
        {
            LockHandler = new SimpleSemaphore();
            TransientRetryInterval = TimeSpan.Zero;
            InstanceId = "test-instance";
            InstanceName = "test-scheduler";
        }

        public void SetFirstCheckIn(bool value)
        {
            firstCheckIn = value;
        }

        protected override ConnectionAndTransactionHolder GetNonManagedTXConnection()
        {
            return new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);
        }

        protected override Task<T> ExecuteInLock<T>(
            string lockName,
            Func<ConnectionAndTransactionHolder, Task<T>> txCallback,
            CancellationToken cancellationToken = default)
        {
            return ExecuteInNonManagedTXLock(lockName, txCallback, cancellationToken);
        }

        protected override bool IsTransient(Exception ex)
        {
            return ex is JobPersistenceException { InnerException: TransientTestException };
        }

        internal IDriverDelegate DirectDelegate
        {
            set
            {
                FieldInfo fieldInfo = typeof(JobStoreSupport).GetField("driverDelegate", BindingFlags.Instance | BindingFlags.NonPublic)!;
                fieldInfo.SetValue(this, value);
            }
        }
    }

    #endregion
}
