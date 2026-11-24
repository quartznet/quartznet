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

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// <see cref="SchedulerWaitExtensions" /> over the database (#4414): a wait on one node for a firing another
/// node runs, read from the execution history every node writes, and the refusal of a cluster without one.
/// </summary>
/// <remarks>
/// SQLite refuses <c>UseClustering()</c>, because its locks are in-process, so each node here is the SQLite
/// store wrapped to say it is clustered, with its own instance id, on one file. Only one node writes at a time:
/// the waiting node refuses the job's execution group, so the other one runs it.
/// </remarks>
[NonParallelizable]
public sealed class FiringWaitSqliteTest
{
    private const string SchedulerName = "firing-wait";
    private const string RemoteGroup = "remote";

    private static readonly TimeSpan waitLimit = TimeSpan.FromSeconds(30);

    private readonly List<(ServiceProvider Node, IScheduler Scheduler)> nodes = [];
    private SqliteTestDatabase database = null!;

    [SetUp]
    public void CreateDatabase()
    {
        database = new SqliteTestDatabase("firing-wait");
        CancellableJob.Reset();
    }

    [TearDown]
    public async Task ShutDownEveryNode()
    {
        // Every node, whatever the test did: a node left running holds the file open. A scheduler the test
        // already shut down returns at once.
        foreach ((ServiceProvider node, IScheduler scheduler) in nodes)
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
            await node.DisposeAsync();
        }

        nodes.Clear();
        database.Dispose();
    }

    /// <summary>
    /// The case a cluster needs the history for: node A fires the job and waits, node B runs it, and no
    /// completion on A could have said how it ended.
    /// </summary>
    [Test]
    public async Task AWaitOnOneNodeIsAnsweredByTheFiringAnotherNodeRan()
    {
        IScheduler a = await Node("node-a", refuseRemote: true);
        IScheduler b = await Node("node-b");
        JobKey job = await AddDurable<RemoteJob>(a, "remote-job");
        await a.Start();
        await b.Start();

        FiringResult result = await a.TriggerJobAndWait(job, waitLimit);

        result.Status.Should().Be(FiringWaitStatus.Ended);
        result.Outcome.Should().Be(ExecutionOutcome.Succeeded);
        result.Execution!.SchedulerInstanceId.Should().Be("node-b", "node A refuses the job's execution group, so B ran it");
        result.Execution.Manual.Should().BeTrue();
        (await a.GetTriggerState(result.TriggerKey)).Should().Be(TriggerState.None);
    }

    /// <summary>
    /// The row comes up to a write-behind interval after the trigger is gone. The wait looks for it for
    /// five seconds and twice the interval before it gives up.
    /// </summary>
    [Test]
    public async Task ARowWrittenBehindIsWaitedForAfterTheTriggerIsGone()
    {
        Action<ExecutionHistoryOptions> writeBehind = options => options.WriteBehindInterval = TimeSpan.FromSeconds(1);
        IScheduler a = await Node("node-a", refuseRemote: true, history: writeBehind);
        IScheduler b = await Node("node-b", history: writeBehind);
        JobKey job = await AddDurable<RemoteJob>(a, "written-behind");
        await a.Start();
        await b.Start();

        FiringResult result = await a.TriggerJobAndWait(job, waitLimit);

        result.Status.Should().Be(FiringWaitStatus.Ended, "the row was flushed inside the grace the wait gives a gone trigger");
        result.Execution!.SchedulerInstanceId.Should().Be("node-b");
    }

    /// <summary>
    /// The design guard of the baseline: a final row that was there when the wait began is not its answer,
    /// though every poll of a clustered node reads the history.
    /// </summary>
    [Test]
    public async Task ARecurringTriggersEarlierRowIsNotTheAnswerToAWaitThatBeginsAfterIt()
    {
        (IScheduler scheduler, ServiceProvider node) = await NodeWithContainer("node-a");
        JobKey job = await AddDurable<LocalJob>(scheduler, "recurring");
        TriggerKey key = new("recurring", "wait");
        await scheduler.ScheduleJob(TriggerBuilder.Create()
            .WithIdentity(key)
            .ForJob(job)
            .StartNow()
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(2)).RepeatForever())
            .Build());
        await scheduler.Start();
        ExecutionHistoryEntry earlier = await Recorded(node, scheduler, key);

        FiringResult result = await scheduler.WaitForFiring(key, waitLimit);

        result.Status.Should().Be(FiringWaitStatus.Ended);
        result.Execution!.EntryId.Should().NotBe(earlier.EntryId, "the row the first look found is the baseline, not the answer");
        result.Execution.FiredAtUtc.Should().BeAfter(earlier.FiredAtUtc);
    }

    /// <summary>
    /// A firing the shutdown cancels and hands back for recovery (#4014) goes on as a recovery trigger, so it
    /// does not end the wait. The shutdown then fails the wait, as it fails every wait still open.
    /// </summary>
    [Test]
    public async Task AFiringHandedBackForRecoveryDoesNotEndTheWaitAndTheShutdownFailsIt()
    {
        (IScheduler scheduler, ServiceProvider node) = await NodeWithContainer("node-a", clustered: false, sharedHistory: false, handBack: true);
        JobKey job = new("handed-back", "wait");
        await scheduler.AddJob(JobBuilder.Create<CancellableJob>().WithIdentity(job).RequestRecovery().StoreDurably().Build());
        await scheduler.Start();

        Task<FiringResult> wait = scheduler.TriggerJobAndWait(job, Timeout.InfiniteTimeSpan).AsTask();
        await CancellableJob.Started.WaitAsync(waitLimit);

        await scheduler.Shutdown(waitForJobsToComplete: true);

        Func<Task> act = () => wait.WaitAsync(waitLimit);
        (await act.Should().ThrowAsync<SchedulerException>("the occurrence goes on as a recovery trigger, so the cancellation is not its end"))
            .WithMessage("The scheduler shut down before the firing of trigger 'DEFAULT.MT_*' ended.");

        PagedResult<ExecutionHistoryEntry> rows = await node.GetRequiredService<IExecutionHistoryStore>()
            .QueryExecutions(new ExecutionHistoryQuery { SchedulerName = SchedulerName, Job = job });
        rows.Items.Should().ContainSingle().Which.Summary.Should().StartWith(JobRunClassifier.HandedBackSummary,
            "the control: the firing was handed back rather than completed");
    }

    [Test]
    public async Task AClusterWithoutASharedHistoryIsRefusedBeforeAnythingIsStored()
    {
        IScheduler scheduler = await Node("node-a", sharedHistory: false);
        JobKey job = await AddDurable<LocalJob>(scheduler, "refused");

        Func<Task> trigger = async () => await scheduler.TriggerJobAndWait(job, waitLimit);
        Func<Task> wait = async () => await scheduler.WaitForFiring(new TriggerKey("any", "wait"), waitLimit);

        await trigger.Should().ThrowAsync<NotSupportedException>().WithMessage("*clustered*UseExecutionHistory()*add_execution_history*");
        await wait.Should().ThrowAsync<NotSupportedException>();
        (await scheduler.GetTriggersOfJob(job)).Should().BeEmpty("the refusal comes before the trigger is stored");
    }

    [Test]
    public async Task TheNamedKeyIsStoredInTheDatabaseAndRefusedTwice()
    {
        IScheduler scheduler = await Node("node-a", clustered: false);
        JobKey job = await AddDurable<RemoteJob>(scheduler, "named");
        TriggerKey named = new("request-7", "api");

        await scheduler.TriggerJob(job, new JobDataMap { ["reason"] = "rerun" }, named);

        ITrigger stored = (await scheduler.GetTrigger(named))!;
        stored.Should().NotBeNull();
        stored.JobDataMap.GetString(SchedulerConstants.ManualTrigger).Should().Be("true");
        stored.JobDataMap.GetString("reason").Should().Be("rerun");
        stored.ExecutionGroup.Should().Be(RemoteGroup);

        Func<Task> again = async () => await scheduler.TriggerJob(job, null, named);
        await again.Should().ThrowAsync<ObjectAlreadyExistsException>();
    }

    private async Task<IScheduler> Node(
        string instanceId,
        bool refuseRemote = false,
        bool clustered = true,
        bool sharedHistory = true,
        Action<ExecutionHistoryOptions>? history = null)
    {
        (IScheduler scheduler, _) = await NodeWithContainer(instanceId, refuseRemote, clustered, sharedHistory, history: history);
        return scheduler;
    }

    private async Task<(IScheduler Scheduler, ServiceProvider Node)> NodeWithContainer(
        string instanceId,
        bool refuseRemote = false,
        bool clustered = true,
        bool sharedHistory = true,
        bool handBack = false,
        Action<ExecutionHistoryOptions>? history = null)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddQuartzExecutionHistory(history);
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = SchedulerName;
                options.InstanceId = instanceId;
                options.IdleWaitTime = TimeSpan.FromSeconds(1);
                if (handBack)
                {
                    // The shutdown asks running jobs to stop, and then waits for them to.
                    options.ShutdownJobInterruption = ShutdownJobInterruption.WhenWaitingForJobs;
                }
            });

            if (refuseRemote)
            {
                q.RefuseExecutionGroups(RemoteGroup);
            }

            if (clustered)
            {
                // First wins: the persistent store below is configured, and this is the store built.
                q.UseJobStore(provider => new ClusterMemberStore(ActivatorUtilities.CreateInstance<LocalTransactionJobStore>(provider)));
            }

            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                store.ProvisionSchema();
                if (sharedHistory)
                {
                    store.UseExecutionHistory();
                }

                if (handBack)
                {
                    store.ConfigureStore(options => options.RecoverFiringsCancelledByShutdown = true);
                }
            });
        });

        ServiceProvider node = services.BuildServiceProvider();
        IScheduler scheduler = await node.GetRequiredService<ISchedulerFactory>().GetScheduler();
        nodes.Add((node, scheduler));
        return (scheduler, node);
    }

    private static async Task<JobKey> AddDurable<TJob>(IScheduler scheduler, string name) where TJob : IJob
    {
        JobKey key = new(name, "wait");
        await scheduler.AddJob(JobBuilder.Create<TJob>().WithIdentity(key).StoreDurably().Build());
        return key;
    }

    private static async Task<ExecutionHistoryEntry> Recorded(ServiceProvider node, IScheduler scheduler, TriggerKey key)
    {
        IExecutionHistoryStore store = node.GetRequiredService<IExecutionHistoryStore>();
        DateTimeOffset deadline = DateTimeOffset.UtcNow + waitLimit;

        while (true)
        {
            PagedResult<ExecutionHistoryEntry> page = await store.QueryExecutions(new ExecutionHistoryQuery
            {
                SchedulerName = scheduler.SchedulerName,
                Trigger = key
            });

            if (page.Items.Count > 0)
            {
                return page.Items[0];
            }

            DateTimeOffset.UtcNow.Should().BeBefore(deadline, "the recorder writes as the firing completes");
            await Task.Delay(50);
        }
    }

    [ExecutionGroup(RemoteGroup)]
    public sealed class RemoteJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    public sealed class LocalJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>Runs until its firing is cancelled, and lets the cancellation out.</summary>
    public sealed class CancellableJob : IJob
    {
        private static TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static Task Started => started.Task;

        public static void Reset() => started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    /// <summary>
    /// The SQLite store, saying it is clustered: what a node of a cluster is to a wait, which SQLite itself
    /// refuses to be.
    /// </summary>
    private sealed class ClusterMemberStore(IJobStore inner) : DelegatingJobStore(inner)
    {
        public override bool Clustered => true;
    }
}
