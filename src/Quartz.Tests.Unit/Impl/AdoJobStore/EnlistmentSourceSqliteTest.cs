#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

#nullable enable

using System.Data;
using System.Data.Common;
using System.Reflection;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.Calendar;
using Quartz.Jobs;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// <see cref="AdoJobStoreOptions.EnlistmentSource" />, on a real scheduler over a file SQLite database:
/// the scheduler asks it at the start of each call that reads or writes the store, so the call takes
/// part in the application's transaction without its caller enlisting anything.
/// </summary>
/// <remarks>
/// <para>
/// SQLite is the one database a unit test can have for the real thing, and it enlists a
/// <see cref="DbTransaction" /> as every other driver does; only the ambient-transaction form is refused.
/// </para>
/// <para>
/// The application is the fixture: <see cref="BeginUnitOfWork" /> opens the connection and transaction an
/// embedder would have bound to its own context, and the source reads them from there.
/// </para>
/// </remarks>
public sealed class EnlistmentSourceSqliteTest
{
    private const string SchedulerName = "enlistment-source";

    private const string CountJobsSql = "SELECT COUNT(*) FROM QRTZ_JOB_DETAILS";
    private const string CountTriggersSql = "SELECT COUNT(*) FROM QRTZ_TRIGGERS";
    private const string CountFiredTriggersSql = "SELECT COUNT(*) FROM QRTZ_FIRED_TRIGGERS";

    /// <summary>
    /// What an embedder keeps in a thread-static: the transaction it bound to the current thread.
    /// </summary>
    [ThreadStatic]
    private static DbTransaction? threadBoundTransaction;

    private static readonly JobKey jobKey = new("enlisted-job", "source");

    private SqliteTestDatabase database = null!;
    private ServiceProvider? container;
    private SqliteConnection? applicationConnection;
    private DbTransaction? applicationTransaction;
    private readonly List<IDisposable> deferredScopes = [];

    [SetUp]
    public void CreateEmptyDatabase()
    {
        database = new SqliteTestDatabase("enlistment-source");
    }

    [TearDown]
    public async Task DeleteDatabase()
    {
        foreach (IDisposable scope in deferredScopes)
        {
            scope.Dispose();
        }

        deferredScopes.Clear();

        if (applicationTransaction is not null)
        {
            await applicationTransaction.DisposeAsync();
            applicationTransaction = null;
        }

        if (applicationConnection is not null)
        {
            await applicationConnection.DisposeAsync();
            applicationConnection = null;
        }

        if (container is not null)
        {
            await container.DisposeAsync();
            container = null;
        }

        database.Dispose();
    }

    // ---------------------------------------------------------------------------------------------
    // Taking part in the application's transaction
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task SchedulingThenRollingBackLeavesNeitherTheJobNorTheTrigger()
    {
        Source source = new();
        source.Behaviour = EnlistApplicationTransaction;
        IScheduler scheduler = await GetScheduler(source);

        await BeginUnitOfWork();
        await scheduler.ScheduleJob(CreateJob(), CreateTrigger());

        (await scheduler.Exists(jobKey)).Should().BeTrue("the job store wrote through the application's transaction");
        source.Calls.Should().Be(2, "the application never enlisted anything itself: the scheduler asked once per call");

        await EndUnitOfWork(commit: false);

        CountRows(CountJobsSql).Should().Be(0, "the application rolled back");
        CountRows(CountTriggersSql).Should().Be(0, "the trigger was written in the same transaction as its job");
        (await scheduler.Exists(jobKey)).Should().BeFalse("and the store's own view agrees");
    }

    [Test]
    public async Task SchedulingThenCommittingLeavesTheJobAndTheTrigger()
    {
        Source source = new();
        source.Behaviour = EnlistApplicationTransaction;
        IScheduler scheduler = await GetScheduler(source);

        await BeginUnitOfWork();
        await scheduler.ScheduleJob(CreateJob(), CreateTrigger());
        await EndUnitOfWork(commit: true);

        CountRows(CountJobsSql).Should().Be(1, "the application committed");
        CountRows(CountTriggersSql).Should().Be(1);
        (await scheduler.GetTrigger(new TriggerKey(jobKey.Name, jobKey.Group))).Should().NotBeNull();
    }

    [Test]
    public async Task ASourceThatReturnsNullRunsTheCallInTheStoresOwnTransaction()
    {
        Source source = new();
        IScheduler scheduler = await GetScheduler(source);

        await scheduler.ScheduleJob(CreateJob(), CreateTrigger());

        source.Calls.Should().Be(1, "the source was asked, and had nothing to enlist");
        CountRows(CountJobsSql).Should().Be(1, "null means the call behaves exactly as it does with no source: the store committed");
        CountRows(CountTriggersSql).Should().Be(1);
    }

    // ---------------------------------------------------------------------------------------------
    // Which calls ask
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// One entry per <see cref="IScheduler" /> member that reads or writes the store, called with
    /// arguments that need nothing to exist. A call may well fail; it is asked about once either way.
    /// </summary>
    private static readonly (string Name, Func<IScheduler, Task> Call)[] storeTouchingCalls = BuildStoreTouchingCalls();

    private static (string Name, Func<IScheduler, Task> Call)[] BuildStoreTouchingCalls()
    {
        JobKey key = new("sweep-job", "sweep");
        TriggerKey triggerKey = new("sweep-trigger", "sweep");
        IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity(key).StoreDurably().Build();
        ITrigger trigger = TriggerBuilder.Create().WithIdentity(triggerKey).ForJob(key).StartAt(DateTimeOffset.UtcNow.AddHours(1)).Build();
        IReadOnlyCollection<ITrigger> triggers = [trigger];
        IReadOnlyCollection<JobKey> jobKeys = [key];
        IReadOnlyCollection<TriggerKey> triggerKeys = [triggerKey];
        GroupMatcher<JobKey> jobGroup = GroupMatcher<JobKey>.GroupEquals("sweep");
        GroupMatcher<TriggerKey> triggerGroup = GroupMatcher<TriggerKey>.GroupEquals("sweep");
        PauseDetails details = new() { Reason = "sweep" };
        ScheduleJobOptions replacing = new() { Replace = true };

        return
        [
            (nameof(IScheduler.QueryFireInstances), s => s.QueryFireInstances(new FireInstanceQuery()).AsTask()),
            (nameof(IScheduler.QueryClusterNodes), s => s.QueryClusterNodes().AsTask()),
            (nameof(IScheduler.Clear), s => s.Clear().AsTask()),
            (nameof(IScheduler.ScheduleJob), s => s.ScheduleJob(job, trigger).AsTask()),
            (nameof(IScheduler.ScheduleJob), s => s.ScheduleJob(trigger, replacing).AsTask()),
            (nameof(IScheduler.ScheduleJob), s => s.ScheduleJob(job, triggers).AsTask()),
            (nameof(IScheduler.ScheduleTrigger), s => s.ScheduleTrigger(trigger, TriggerConflict.Replace).AsTask()),
            (nameof(IScheduler.ScheduleJobs), s => s.ScheduleJobs(new Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>> { [job] = triggers }).AsTask()),
            (nameof(IScheduler.AddJob), s => s.AddJob(job).AsTask()),
            (nameof(IScheduler.DeleteJob), s => s.DeleteJob(key).AsTask()),
            (nameof(IScheduler.DeleteJobs), s => s.DeleteJobs(jobKeys).AsTask()),
            (nameof(IScheduler.DeleteJobs), s => s.DeleteJobs(jobGroup).AsTask()),
            (nameof(IScheduler.UnscheduleJob), s => s.UnscheduleJob(triggerKey).AsTask()),
            (nameof(IScheduler.UnscheduleJobs), s => s.UnscheduleJobs(triggerKeys).AsTask()),
            (nameof(IScheduler.UnscheduleJobs), s => s.UnscheduleJobs(triggerGroup).AsTask()),
            (nameof(IScheduler.RescheduleJob), s => s.RescheduleJob(triggerKey, trigger).AsTask()),
            (nameof(IScheduler.UpdateTriggerDetails), s => s.UpdateTriggerDetails(triggerKey, new TriggerDetailsUpdate().WithDescription("sweep")).AsTask()),
            (nameof(IScheduler.TriggerJob), s => s.TriggerJob(key).AsTask()),
            (nameof(IScheduler.Exists), s => s.Exists(key).AsTask()),
            (nameof(IScheduler.Exists), s => s.Exists(triggerKey).AsTask()),
            (nameof(IScheduler.Exists), s => s.Exists("sweep-calendar").AsTask()),
            (nameof(IScheduler.PauseJob), s => s.PauseJob(key).AsTask()),
            (nameof(IScheduler.PauseJobs), s => s.PauseJobs(jobKeys).AsTask()),
            (nameof(IScheduler.PauseJobGroups), s => s.PauseJobGroups(jobGroup).AsTask()),
            (nameof(IScheduler.PauseTrigger), s => s.PauseTrigger(triggerKey).AsTask()),
            (nameof(IScheduler.PauseTriggers), s => s.PauseTriggers(triggerKeys).AsTask()),
            (nameof(IScheduler.PauseTriggerGroups), s => s.PauseTriggerGroups(triggerGroup).AsTask()),
            (nameof(IScheduler.ResumeJob), s => s.ResumeJob(key).AsTask()),
            (nameof(IScheduler.ResumeJobs), s => s.ResumeJobs(jobKeys).AsTask()),
            (nameof(IScheduler.ResumeJobGroups), s => s.ResumeJobGroups(jobGroup).AsTask()),
            (nameof(IScheduler.ResumeTrigger), s => s.ResumeTrigger(triggerKey).AsTask()),
            (nameof(IScheduler.ResumeTriggers), s => s.ResumeTriggers(triggerKeys).AsTask()),
            (nameof(IScheduler.ResumeTriggerGroups), s => s.ResumeTriggerGroups(triggerGroup).AsTask()),
            (nameof(IScheduler.PauseAll), s => s.PauseAll().AsTask()),
            (nameof(IScheduler.ResumeAll), s => s.ResumeAll().AsTask()),
            (nameof(IScheduler.PauseTriggerWith), s => s.PauseTriggerWith(triggerKey, details).AsTask()),
            (nameof(IScheduler.PauseJobWith), s => s.PauseJobWith(key, details).AsTask()),
            (nameof(IScheduler.PauseTriggerGroupsWith), s => s.PauseTriggerGroupsWith(triggerGroup, details).AsTask()),
            (nameof(IScheduler.PauseJobGroupsWith), s => s.PauseJobGroupsWith(jobGroup, details).AsTask()),
            (nameof(IScheduler.PauseAllWith), s => s.PauseAllWith(details).AsTask()),
            (nameof(IScheduler.PauseTriggersWith), s => s.PauseTriggersWith(triggerKeys, details).AsTask()),
            (nameof(IScheduler.PauseJobsWith), s => s.PauseJobsWith(jobKeys, details).AsTask()),
            (nameof(IScheduler.GetTriggerPause), s => s.GetTriggerPause(triggerKey).AsTask()),
            (nameof(IScheduler.GetTriggerGroupPause), s => s.GetTriggerGroupPause("sweep").AsTask()),
            (nameof(IScheduler.GetJobGroupPause), s => s.GetJobGroupPause("sweep").AsTask()),
            (nameof(IScheduler.QueryJobs), s => s.QueryJobs(new JobQuery()).AsTask()),
            (nameof(IScheduler.QueryTriggers), s => s.QueryTriggers(new TriggerQuery()).AsTask()),
            (nameof(IScheduler.QueryJobGroups), s => s.QueryJobGroups(new JobGroupQuery()).AsTask()),
            (nameof(IScheduler.QueryTriggerGroups), s => s.QueryTriggerGroups(new TriggerGroupQuery()).AsTask()),
            (nameof(IScheduler.QueryCalendarNames), s => s.QueryCalendarNames(new CalendarQuery()).AsTask()),
            (nameof(IScheduler.GetJobDetails), s => s.GetJobDetails(jobKeys).AsTask()),
            (nameof(IScheduler.GetTriggers), s => s.GetTriggers(triggerKeys).AsTask()),
            (nameof(IScheduler.GetJobDetail), s => s.GetJobDetail(key).AsTask()),
            (nameof(IScheduler.GetTrigger), s => s.GetTrigger(triggerKey).AsTask()),
            (nameof(IScheduler.GetTriggerState), s => s.GetTriggerState(triggerKey).AsTask()),
            (nameof(IScheduler.ResetTriggerFromErrorState), s => s.ResetTriggerFromErrorState(triggerKey).AsTask()),
            (nameof(IScheduler.ResetTriggersFromErrorState), s => s.ResetTriggersFromErrorState(triggerKeys).AsTask()),
            // Composed in IScheduler itself: the query is the one call, and no trigger is in error here.
            (nameof(IScheduler.ResetTriggersFromErrorState), s => s.ResetTriggersFromErrorState(triggerGroup).AsTask()),
            (nameof(IScheduler.AddCalendar), s => s.AddCalendar("sweep-calendar", new HolidayCalendar()).AsTask()),
            (nameof(IScheduler.DeleteCalendar), s => s.DeleteCalendar("sweep-calendar").AsTask()),
            (nameof(IScheduler.GetCalendar), s => s.GetCalendar("sweep-calendar").AsTask()),
        ];
    }

    /// <summary>
    /// The members that touch no store: they start, stop, describe or interrupt, and the scheduler
    /// never asks the source about them.
    /// </summary>
    private static readonly HashSet<string> membersThatTouchNoStore =
    [
        nameof(IScheduler.Start),
        nameof(IScheduler.StartDelayed),
        nameof(IScheduler.Standby),
        nameof(IScheduler.Shutdown),
        nameof(IScheduler.GetMetadata),
        nameof(IScheduler.GetStatus),
        nameof(IScheduler.GetSchedulerInstanceId),
        nameof(IScheduler.SetExecutionLimits),
        nameof(IScheduler.GetExecutionLimits),
        nameof(IScheduler.Interrupt),
        nameof(IScheduler.InterruptFireInstance),
    ];

    [Test]
    public async Task EveryMemberThatReadsOrWritesTheStoreAsksTheSourceOnce()
    {
        Source source = new();
        IScheduler scheduler = await GetScheduler(source);

        List<string> offenders = [];
        for (int i = 0; i < storeTouchingCalls.Length; i++)
        {
            (string name, Func<IScheduler, Task> call) = storeTouchingCalls[i];
            int before = source.Calls;

            try
            {
                await call(scheduler);
            }
            catch (Exception)
            {
                // Some of these fail - a job that is not there, a calendar nothing references. Whether the
                // source was asked is the question, and it is asked before anything can fail.
            }

            int asked = source.Calls - before;
            if (asked != 1)
            {
                offenders.Add($"#{i} {name}: asked {asked} times");
            }
        }

        offenders.Should().BeEmpty("each call asks the source exactly once, before it touches the store");
    }

    [Test]
    public void TheCallsAboveNameEveryMemberOfTheSchedulerOrSayWhyNot()
    {
        Dictionary<string, int> overloads = typeof(IScheduler)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => !method.IsSpecialName)
            .GroupBy(method => method.Name)
            .ToDictionary(group => group.Key, group => group.Count());

        Dictionary<string, int> listed = storeTouchingCalls
            .GroupBy(entry => entry.Name)
            .ToDictionary(group => group.Key, group => group.Count());

        foreach (string name in membersThatTouchNoStore)
        {
            overloads.Should().ContainKey(name, "a member that does not exist cannot be exempt");
            listed.Should().NotContainKey(name, "a member is either asked about or exempt");
        }

        Dictionary<string, int> expected = overloads
            .Where(pair => !membersThatTouchNoStore.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        listed.Should().BeEquivalentTo(
            expected,
            "an IScheduler member added later either reads or writes the store, and is asked about, or says it does not");
    }

    /// <summary>
    /// The one store read the facade has beside <see cref="IScheduler" />: how an extension resolves the group a
    /// manual firing belongs to. Its caller is the application's, so it asks as the rest do.
    /// </summary>
    [Test]
    public async Task TheSharedExecutionGroupLookupAsksTheSourceToo()
    {
        Source source = new();
        IScheduler scheduler = await GetScheduler(source);

        string? group = await ((ISharedExecutionGroupSource) scheduler).GetSharedExecutionGroup(jobKey);

        group.Should().BeNull("no job is stored, so no trigger of it names a group");
        source.Calls.Should().Be(1);
    }

    [Test]
    public async Task StartingStoppingAndDescribingTheSchedulerNeverAskTheSource()
    {
        Source source = new();
        IScheduler scheduler = await GetScheduler(source);

        await scheduler.StartDelayed(TimeSpan.FromMilliseconds(20));
        for (int attempt = 0; attempt < 200 && scheduler.Status != SchedulerStatus.Running; attempt++)
        {
            await Task.Delay(50);
        }

        scheduler.Status.Should().Be(SchedulerStatus.Running);

        await scheduler.Standby();
        await scheduler.Start();

        _ = scheduler.Context;
        _ = scheduler.ListenerManager;
        _ = scheduler.SchedulerName;
        _ = scheduler.SchedulerInstanceId;
        await scheduler.GetStatus();
        await scheduler.GetMetadata();
        await scheduler.GetSchedulerInstanceId();
        await scheduler.GetExecutionLimits();
        await scheduler.SetExecutionLimits(null);
        await scheduler.Interrupt(jobKey);
        await scheduler.InterruptFireInstance("no-such-firing");

        await scheduler.Shutdown();

        source.Calls.Should().Be(0,
            "the scheduler starting, running its own work and shutting down never reaches the source: "
            + "starting inside an enlistment is refused, and none of the scheduler's own work is the application's");
    }

    [Test]
    public async Task AnExplicitEnlistmentWinsOverTheSource()
    {
        Source source = new();
        source.Behaviour = _ => throw new InvalidOperationException("an explicit enlistment is the caller's answer");
        IScheduler scheduler = await GetScheduler(source);

        await BeginUnitOfWork();

        using (scheduler.EnlistTransaction(applicationTransaction!))
        {
            Func<Task> act = async () => await scheduler.ScheduleJob(CreateJob(), CreateTrigger());
            await act.Should().NotThrowAsync("the source is not asked when the flow has enlisted something itself");
        }

        source.Calls.Should().Be(0);

        await EndUnitOfWork(commit: false);

        CountRows(CountJobsSql).Should().Be(0, "the schedule went into the transaction the caller enlisted");
    }

    [Test]
    public async Task AJobThatCallsTheSchedulerAsksTheSourceAndTheSchedulersOwnWorkDoesNot()
    {
        Source source = new();
        source.Behaviour = EnlistApplicationTransaction;
        JobRecorder recorder = new(() => source.Calls, SchedulerName);
        IScheduler scheduler = await GetScheduler(source, services => services.AddSingleton(recorder));


        await BeginUnitOfWork();
        await scheduler.ScheduleJob(
            JobBuilder.Create<RecordingJob>().WithIdentity(jobKey).StoreDurably().Build(),
            TriggerBuilder.Create().WithIdentity(jobKey.Name, jobKey.Group).ForJob(jobKey).StartNow().Build());
        await EndUnitOfWork(commit: true);

        // Started once the application's transaction is over: SQLite locks in process, so a scheduler
        // already running would be waiting on the transaction it is not part of.
        await scheduler.Start();

        recorder.Fired.Wait(TimeSpan.FromSeconds(30)).Should().BeTrue(
            "the application committed, so the scheduler fires the trigger - in transactions of its own, since the "
            + "application's is over");

        await WaitFor(() => CountRows(CountTriggersSql) == 0 && CountRows(CountFiredTriggersSql) == 0);

        recorder.Failure.Should().BeNull();
        recorder.ThreadOfTheFiring.Should().NotBe(Environment.CurrentManagedThreadId,
            "the job ran on the scheduler's own flow, which the application's enlistment never reached");
        recorder.EnlistedInsideTheJob.Should().BeFalse("the scheduler's own work does not run enlisted");
        recorder.AskedByTheJobsOwnCall.Should().Be(1, "a call through context.Scheduler is the application's, so it asks");
        recorder.JobSeenThroughContext.Should().NotBeNull();

        source.Calls.Should().Be(2,
            "the application's ScheduleJob and the job's GetJobDetail asked; acquiring, firing and completing the "
            + "trigger are the scheduler's own work and were never offered to the source");

        await scheduler.Shutdown(true);
    }

    // ---------------------------------------------------------------------------------------------
    // Where it is called
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task TheSourceIsCalledOnTheCallersThreadBeforeTheCallsFirstAwait()
    {
        int? threadOfTheSource = null;
        DbTransaction? boundWhenAsked = null;

        Source source = new();
        source.Behaviour = scheduler =>
        {
            threadOfTheSource = Environment.CurrentManagedThreadId;
            boundWhenAsked = threadBoundTransaction;
            return boundWhenAsked is null ? null : scheduler.EnlistTransaction(boundWhenAsked);
        };

        IScheduler scheduler = await GetScheduler(source);
        await BeginUnitOfWork();

        // What an embedder does: its transaction is bound to the thread, and ends when the call is made
        // rather than when the task it returns completes.
        int callerThread = Environment.CurrentManagedThreadId;
        threadBoundTransaction = applicationTransaction;
        ValueTask<DateTimeOffset> pending = scheduler.ScheduleJob(CreateJob(), CreateTrigger());
        threadBoundTransaction = null;

        threadOfTheSource.Should().Be(callerThread, "the source is asked inside the call, not on a continuation");
        boundWhenAsked.Should().BeSameAs(applicationTransaction,
            "so state that lives on the calling thread is still there to be read");

        await pending;
        await EndUnitOfWork(commit: false);

        CountRows(CountJobsSql).Should().Be(0, "and the enlistment it made took the call into the transaction");
    }

    // ---------------------------------------------------------------------------------------------
    // Failure
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task AnExceptionFromTheSourceReachesTheCallerUnchangedBeforeAnyStoreWork()
    {
        InvalidOperationException failure = new("the embedder has no transaction to give");
        Source source = new();
        source.Behaviour = _ => throw failure;
        IScheduler scheduler = await GetScheduler(source);

        Func<Task> act = async () => await scheduler.ScheduleJob(CreateJob(), CreateTrigger());

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure,
            "the source's own exception is the answer, not a wrapper around it");

        CountRows(CountJobsSql).Should().Be(0, "it was thrown before the store was touched");
    }

    [Test]
    public async Task TheScopeIsDisposedOnceAfterASuccessfulCallAndAFailureToDisposeIsReported()
    {
        InvalidOperationException disposeFailure = new("the embedder could not end the enlistment");
        TrackingScope scope = new() { Failure = disposeFailure };
        Source source = new();
        source.Behaviour = _ => scope;
        IScheduler scheduler = await GetScheduler(source);

        Func<Task> act = async () => await scheduler.ScheduleJob(CreateJob(), CreateTrigger());

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(disposeFailure,
            "the call succeeded, so the only thing wrong is the one thing that is reported");
        scope.Disposals.Should().Be(1);
    }

    [Test]
    public async Task TheScopeIsDisposedWhenTheCallThrowsAndTheCallsExceptionWins()
    {
        Source source = new();
        IScheduler scheduler = await GetScheduler(source);
        await scheduler.ScheduleJob(CreateJob(), CreateTrigger());

        TrackingScope scope = new() { Failure = new InvalidOperationException("ending the enlistment failed too") };
        source.Behaviour = _ => scope;

        // The job is already stored and nothing says to replace it.
        Func<Task> act = async () => await scheduler.ScheduleJob(CreateJob(), CreateTrigger());

        await act.Should().ThrowAsync<ObjectAlreadyExistsException>(
            "the call's own exception is the one the caller needs; ending the enlistment must not replace it");
        scope.Disposals.Should().Be(1, "a call that fails ends its enlistment as one that succeeds does");
    }

    [Test]
    public async Task TheScopeIsDisposedWhenTheCallIsCancelled()
    {
        Source source = new();
        TrackingScope scope = new();
        source.Behaviour = _ => scope;
        IScheduler scheduler = await GetScheduler(source);

        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        Func<Task> act = async () => await scheduler.ScheduleJob(CreateJob(), CreateTrigger(), cancellationToken: cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        scope.Disposals.Should().Be(1);
    }

    /// <summary>
    /// The enlistment is an <see cref="AsyncLocal{T}" />, so a call that set it and handed the store's task
    /// back would leave it in the caller's execution context. The scope here never ends of its own accord
    /// - the source keeps it alive, as one that ends it after the commit does - so nothing but the call
    /// giving the context back can hide it from the next one.
    /// </summary>
    [TestCase("a call that succeeds")]
    [TestCase("a call that fails")]
    [TestCase("a call that is cancelled")]
    public async Task AFlowThatMadeAnEnlistedCallIsNotEnlistedAfterIt(string howTheCallEnds)
    {
        Source source = new();
        source.Behaviour = DeferScopeUntilTheTestEnds;
        IScheduler scheduler = await GetScheduler(source);

        await BeginUnitOfWork();
        await scheduler.ScheduleJob(CreateJob(), CreateTrigger());
        AmbientConnection.Get(SchedulerName).Should().BeNull("the first call returned, and with it the context it enlisted in");

        using CancellationTokenSource cancelled = new();
        Func<Task> second = howTheCallEnds switch
        {
            "a call that fails" => async () => await scheduler.ScheduleJob(CreateJob(), CreateTrigger()),
            "a call that is cancelled" => async () =>
            {
                await cancelled.CancelAsync();
                await scheduler.Exists(jobKey, cancelled.Token);
            },
            _ => async () => await scheduler.Exists(jobKey),
        };

        try
        {
            await second();
        }
        catch (Exception)
        {
            // Failing or being cancelled is the point of the second call.
        }

        AmbientConnection.Get(SchedulerName).Should().BeNull($"{howTheCallEnds} gave the caller's context back too");

        int before = source.Calls;
        await scheduler.Exists(jobKey);
        source.Calls.Should().Be(before + 1,
            "the next call on the flow is asked about again, which an enlistment left behind would have pre-empted");
    }

    [Test]
    public async Task ARefusalOfTheEnlistmentComesOutOfTheSourceAndNothingIsStored()
    {
        Source source = new();
        ForeignConnection foreign = new();
        source.Behaviour = scheduler => scheduler.EnlistTransaction(foreign.BeginTransaction());
        IScheduler scheduler = await GetScheduler(source);

        Func<Task> act = async () => await scheduler.ScheduleJob(CreateJob(), CreateTrigger());

        (await act.Should().ThrowAsync<ArgumentException>(
                "a connection of another provider cannot carry the store's commands, and the enlistment says so"))
            .Which.Message.Should().Contain("different provider");

        CountRows(CountJobsSql).Should().Be(0);
    }

    // ---------------------------------------------------------------------------------------------
    // Configuration
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task ASourceWithoutAcceptEnlistedTransactionsFailsWhenTheSchedulerIsBuilt()
    {
        Source source = new();
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = SchedulerName);
            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                store.ProvisionSchema();
                store.ConfigureStore(options => options.EnlistmentSource = source.Enlist);
            });
        });

        container = services.BuildServiceProvider();

        Func<Task> act = async () => await container.GetRequiredService<ISchedulerFactory>().GetScheduler();

        Exception failure = (await act.Should().ThrowAsync<Exception>(
            "enlistments the store would ignore are refused rather than switched on")).Which;

        string messages = string.Join(" | ", ChainOf(failure).Select(exception => exception.Message));
        messages.Should().Contain("EnlistmentSource").And.Contain("AcceptEnlistedTransactions")
            .And.Contain("Set AcceptEnlistedTransactions to true",
                "the message says what to do, not only what is wrong");
    }

    [Test]
    public async Task ASourceOnAnInMemoryStoreIsNeverAsked()
    {
        Source source = new();
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = SchedulerName);
            q.UseInMemoryStore();
        });

        // The only way to put a source beside an in-memory store: past the builder, which has no such
        // setting for it. There is nothing to enlist in, and the setting is read by the store that has it.
        services.Configure<AdoJobStoreOptions>(options =>
        {
            options.AcceptEnlistedTransactions = true;
            options.EnlistmentSource = source.Enlist;
        });

        container = services.BuildServiceProvider();
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();

        await scheduler.ScheduleJob(CreateJob(), CreateTrigger());

        source.Calls.Should().Be(0, "an in-memory store stores nothing in the application's database");
    }

    // ---------------------------------------------------------------------------------------------
    // What the tests share
    // ---------------------------------------------------------------------------------------------

    private IDisposable? EnlistApplicationTransaction(IScheduler scheduler)
    {
        return applicationTransaction is { } transaction ? scheduler.EnlistTransaction(transaction) : null;
    }

    /// <summary>
    /// Enlists the application's transaction and hands the scheduler a scope that ends nothing, keeping the
    /// real one to be ended after the commit - the way a source does that wants the signal to follow it.
    /// </summary>
    private IDisposable? DeferScopeUntilTheTestEnds(IScheduler scheduler)
    {
        if (applicationTransaction is not { } transaction)
        {
            return null;
        }

        deferredScopes.Add(scheduler.EnlistTransaction(transaction));
        return new TrackingScope();
    }

    private async Task BeginUnitOfWork()
    {
        applicationConnection = new SqliteConnection(database.ConnectionString);
        await applicationConnection.OpenAsync();
        applicationTransaction = await applicationConnection.BeginTransactionAsync();
    }

    private async Task EndUnitOfWork(bool commit)
    {
        DbTransaction transaction = applicationTransaction!;

        // From here the application has no transaction to enlist.
        applicationTransaction = null;

        if (commit)
        {
            await transaction.CommitAsync();
        }
        else
        {
            await transaction.RollbackAsync();
        }

        await transaction.DisposeAsync();
        await applicationConnection!.DisposeAsync();
        applicationConnection = null;
    }

    private async Task<IScheduler> GetScheduler(Source source, Action<IServiceCollection>? configure = null)
    {
        ServiceCollection services = new();

        configure?.Invoke(services);

        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = SchedulerName;
                options.InstanceId = "one";
            });

            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                store.ProvisionSchema();
                store.ConfigureStore(options =>
                {
                    options.AcceptEnlistedTransactions = true;
                    options.EnlistmentSource = source.Enlist;
                });
            });
        });

        container = services.BuildServiceProvider();

        return await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
    }

    private static IJobDetail CreateJob()
    {
        return JobBuilder.Create<NoOpJob>().WithIdentity(jobKey).StoreDurably().Build();
    }

    private static ITrigger CreateTrigger()
    {
        return TriggerBuilder.Create()
            .WithIdentity(jobKey.Name, jobKey.Group)
            .ForJob(jobKey)
            .StartAt(DateTimeOffset.UtcNow.AddHours(1))
            .Build();
    }

    /// <summary>
    /// Counts rows from a connection of the test's own, which sees only what has been committed.
    /// </summary>
    private long CountRows(string sql)
    {
        using SqliteConnection connection = new(database.ConnectionString);
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;

        return (long) command.ExecuteScalar()!;
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(50);
        }

        condition().Should().BeTrue("the condition was not met within ten seconds");
    }

    private static List<Exception> ChainOf(Exception exception)
    {
        List<Exception> chain = [];
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            chain.Add(current);
        }

        return chain;
    }

    /// <summary>
    /// The source under test: asks the behaviour it was given, and remembers how often it was asked.
    /// </summary>
    private sealed class Source
    {
        private int calls;

        internal Func<IScheduler, IDisposable?> Behaviour { get; set; } = static _ => null;

        internal int Calls => Volatile.Read(ref calls);

        internal IDisposable? Enlist(IScheduler scheduler)
        {
            Interlocked.Increment(ref calls);
            return Behaviour(scheduler);
        }
    }

    /// <summary>
    /// What a source hands back: counts how often it was disposed, ends the enlistment it was given, if any,
    /// and fails if it was told to.
    /// </summary>
    private sealed class TrackingScope : IDisposable
    {
        private int disposals;

        internal IDisposable? Inner { get; init; }

        internal Exception? Failure { get; init; }

        internal int Disposals => Volatile.Read(ref disposals);

        public void Dispose()
        {
            Interlocked.Increment(ref disposals);
            Inner?.Dispose();

            if (Failure is not null)
            {
                throw Failure;
            }
        }
    }

    /// <summary>
    /// A connection of a provider the store is not configured for.
    /// </summary>
    private sealed class ForeignConnection : DbConnection
    {
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ConnectionString { get; set; } = "";

        public override string Database => "";

        public override string DataSource => "";

        public override string ServerVersion => "";

        public override ConnectionState State => ConnectionState.Open;

        public override void ChangeDatabase(string databaseName)
        {
        }

        public override void Close()
        {
        }

        public override void Open()
        {
        }

        protected override DbTransaction BeginDbTransaction(System.Data.IsolationLevel isolationLevel)
        {
            return new ForeignTransaction(this);
        }

        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }

    private sealed class ForeignTransaction(DbConnection connection) : DbTransaction
    {
        protected override DbConnection DbConnection => connection;

        public override System.Data.IsolationLevel IsolationLevel => System.Data.IsolationLevel.ReadCommitted;

        public override void Commit()
        {
        }

        public override void Rollback()
        {
        }
    }

    /// <summary>
    /// What the job under the running scheduler tells the test.
    /// </summary>
    public sealed class JobRecorder(Func<int> sourceCalls, string schedulerName)
    {
        internal Func<int> SourceCalls { get; } = sourceCalls;

        internal string SchedulerName { get; } = schedulerName;

        internal ManualResetEventSlim Fired { get; } = new(false);

        internal int ThreadOfTheFiring { get; set; }

        internal bool EnlistedInsideTheJob { get; set; }

        internal int AskedByTheJobsOwnCall { get; set; }

        internal IJobDetail? JobSeenThroughContext { get; set; }

        internal Exception? Failure { get; set; }
    }

    /// <summary>
    /// Calls the scheduler it was handed, the way a job that schedules follow-up work does.
    /// </summary>
    public sealed class RecordingJob(JobRecorder recorder) : IJob
    {
        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                recorder.ThreadOfTheFiring = Environment.CurrentManagedThreadId;
                recorder.EnlistedInsideTheJob = AmbientConnection.Get(recorder.SchedulerName) is not null;

                int before = recorder.SourceCalls();
                recorder.JobSeenThroughContext = await context.Scheduler.GetJobDetail(context.JobDetail.Key, cancellationToken);
                recorder.AskedByTheJobsOwnCall = recorder.SourceCalls() - before;
            }
            catch (Exception exception)
            {
                recorder.Failure = exception;
            }
            finally
            {
                recorder.Fired.Set();
            }
        }
    }
}
