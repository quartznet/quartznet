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

using System.Collections.Concurrent;

using FakeItEasy;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Core;
using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// An interrupt asked of a firing another node runs goes through the store (#4071): the asking node
/// records a request, and the node running the firing reads its requests and cancels the job's token.
/// </summary>
/// <remarks>
/// The store is the in-memory one, made to say it is clustered and to report what the test says another
/// node is running and has been asked. What the ADO.NET store does with a request is
/// <c>InterruptRequestSqliteTest</c>; two real nodes are <c>ClusteredInterruptTestBase</c> in the
/// integration suite.
/// </remarks>
[NonParallelizable]
public sealed class ClusteredInterruptTest
{
    private static readonly TimeSpan waitLimit = TimeSpan.FromSeconds(30);

    private static readonly JobKey jobKey = new("long", "clustered-interrupt");

    private ServiceProvider? container;
    private IScheduler scheduler = null!;
    private ClusteredStore store = null!;

    [SetUp]
    public void ResetJob()
    {
        LongJob.Reset();
    }

    [TearDown]
    public async Task ShutDown()
    {
        if (container is not null)
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
            await container.DisposeAsync();
            container = null;
        }
    }

    [Test]
    public async Task InterruptOfAJobRunningOnAnotherNodeRecordsARequestAndAnswersTrue()
    {
        await Build(clustered: true);
        store.RunningElsewhere("fired-on-b", "node-b");

        bool interrupted = await scheduler.Interrupt(jobKey);

        interrupted.Should().BeTrue("a request recorded for the node running the firing is the interrupt this node can make");
        store.Requested.Should().Equal(["fired-on-b"], "the firing runs on another node, so its row carries the request");
    }

    [Test]
    public async Task InterruptOfAFireInstanceRunningOnAnotherNodeRecordsARequestAndAnswersTrue()
    {
        await Build(clustered: true);
        store.RunningElsewhere("fired-on-b", "node-b");

        (await scheduler.InterruptFireInstance("fired-on-b")).Should().BeTrue();

        store.Requested.Should().Equal(["fired-on-b"]);
    }

    [Test]
    public async Task InterruptOfAFireInstanceNoNodeRunsAnswersFalse()
    {
        await Build(clustered: true);

        (await scheduler.InterruptFireInstance("fired-nowhere")).Should().BeFalse(
            "the store found no executing firing to record the request on");
        store.Requested.Should().Equal(["fired-nowhere"], "the store is asked, and says there was nothing");
    }

    [Test]
    public async Task AFiringInterruptedHereIsNotAlsoRequested()
    {
        await Build(clustered: true);
        await scheduler.Start();
        Started local = await RunLongJob();
        store.RunningElsewhere("fired-on-b", "node-b");

        (await scheduler.Interrupt(jobKey)).Should().BeTrue();

        (await LongJob.Cancelled.WaitAsync(waitLimit)).Should().Be(local.FireInstanceId,
            "the firing on this node is interrupted directly, before Interrupt returns");
        store.Requested.Should().Equal(["fired-on-b"],
            "only the firing this node does not run needs a request; the local one is already interrupted");
    }

    [Test]
    public async Task OffAClusterAnInterruptAsksTheStoreNothing()
    {
        await Build(clustered: false);
        store.RunningElsewhere("fired-on-b", "node-b");

        (await scheduler.Interrupt(jobKey)).Should().BeFalse("nothing of the job runs on this node, and no other node shares the store");
        (await scheduler.InterruptFireInstance("fired-on-b")).Should().BeFalse();

        store.Requested.Should().BeEmpty("a store that is not clustered is asked for no request, as before 4.6");
    }

    [Test]
    public async Task APollInterruptsALocalFiringWithAPendingRequestAndAnnouncesItOnce()
    {
        await Build(clustered: true);
        InterruptRecorder interrupts = new();
        scheduler.ListenerManager.AddSchedulerListener(interrupts);
        await scheduler.Start();
        Started local = await RunLongJob();

        store.Pending.Add(local.FireInstanceId);

        // Through the signaler the store holds, which is the path a check-in takes.
        IInterruptRequestPoller poller = (IInterruptRequestPoller) container!.GetRequiredService<ISchedulerSignaler>();
        poller.PollInterruptRequests();

        (await LongJob.Cancelled.WaitAsync(waitLimit)).Should().Be(local.FireInstanceId,
            "a pending request for a firing this node runs cancels its token, as a local interrupt does");
        (await interrupts.Interrupted.WaitAsync(waitLimit)).Should().Be((jobKey, local.FireInstanceId),
            "and is announced to the listeners as a local interrupt is");

        // The request stays on the row until the firing completes, so the next read finds it again.
        QuartzScheduler quartz = ((StdScheduler) scheduler).scheduler;
        (await quartz.DeliverInterruptRequests()).Should().Be(0, "the firing's token is already cancelled");
        interrupts.Count.Should().Be(1, "a firing is interrupted and announced once, however often its request is read");
    }

    [Test]
    public async Task APollPassesOverARequestForAFiringThisNodeIsNotRunning()
    {
        await Build(clustered: true);
        store.Pending.Add("fired-elsewhere");

        QuartzScheduler quartz = ((StdScheduler) scheduler).scheduler;
        (await quartz.DeliverInterruptRequests()).Should().Be(0, "nothing of that firing runs here yet");
    }

    [Test]
    public async Task APollThatFailsLetsTheNextOneRun()
    {
        await Build(clustered: true);
        store.FailNextRead = true;
        QuartzScheduler quartz = ((StdScheduler) scheduler).scheduler;

        quartz.PollInterruptRequests();
        await store.ReadAttempted.Task.WaitAsync(waitLimit);

        // The failed read let go of the single-flight guard, so the next check-in's poll reads again.
        await scheduler.Start();
        Started local = await RunLongJob();
        store.Pending.Add(local.FireInstanceId);
        await WaitUntil(() =>
        {
            quartz.PollInterruptRequests();
            return LongJob.Cancelled.IsCompleted;
        });

        (await LongJob.Cancelled).Should().Be(local.FireInstanceId);
    }

    [Test]
    public async Task APollAfterShutdownReadsNothing()
    {
        await Build(clustered: true);
        QuartzScheduler quartz = ((StdScheduler) scheduler).scheduler;
        await scheduler.Shutdown();

        quartz.PollInterruptRequests();

        store.Reads.Should().Be(0, "a scheduler that has shut down has no firing left to interrupt and a store that is closed");
    }

    [Test]
    public async Task TheInMemoryStoreRecordsAndReadsNoRequest()
    {
        await Build(clustered: false);
        IJobStore ram = store.Inner;

        (await ram.RequestInterrupt("anything")).Should().BeFalse("every firing of the in-memory store runs in this process");
        (await ram.GetInterruptRequests()).Should().BeEmpty();
    }

    [Test]
    public async Task TheDefaultMembersRecordNothingAndReadNothing()
    {
        // A store written before 4.6 does not declare either member, and runs the interface's bodies.
        IJobStore older = A.Fake<IJobStore>(options => options.CallsBaseMethods());

        (await older.RequestInterrupt("anything")).Should().BeFalse("a store that records nothing has no way to reach another node");
        (await older.GetInterruptRequests()).Should().BeEmpty();
    }

    [Test]
    public async Task TheDelegatingStoreHandsBothMembersOn()
    {
        IJobStore inner = A.Fake<IJobStore>();
        A.CallTo(() => inner.RequestInterrupt("fired-on-b", A<CancellationToken>._)).Returns(new ValueTask<bool>(true));
        A.CallTo(() => inner.GetInterruptRequests(A<CancellationToken>._)).Returns(new ValueTask<List<string>>(new List<string> { "fired-here" }));

        DelegatingJobStore delegating = new(inner);

        (await delegating.RequestInterrupt("fired-on-b")).Should().BeTrue();
        (await delegating.GetInterruptRequests()).Should().Equal(["fired-here"]);
        A.CallTo(() => inner.RequestInterrupt("fired-on-b", A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    private async Task Build(bool clustered)
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = "clustered-interrupt");
            q.UseJobStore(provider =>
            {
                store = new ClusteredStore(ActivatorUtilities.CreateInstance<RAMJobStore>(provider), clustered);
                return store;
            });
        });

        container = services.BuildServiceProvider();
        scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
    }

    private async Task<Started> RunLongJob()
    {
        await scheduler.ScheduleJob(
            JobBuilder.Create<LongJob>().WithIdentity(jobKey).Build(),
            TriggerBuilder.Create().WithIdentity("long", "clustered-interrupt").StartNow().Build());

        return await LongJob.Started.WaitAsync(waitLimit);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.Add(waitLimit);
        while (!condition())
        {
            DateTimeOffset.UtcNow.Should().BeBefore(deadline, "the condition should have held well within the limit");
            await Task.Delay(20);
        }
    }

    internal sealed record Started(string FireInstanceId);

    /// <summary>
    /// The in-memory store, saying it is clustered and answering for firings the test says run on another
    /// node, and for the requests the test says are pending on this one.
    /// </summary>
    private sealed class ClusteredStore(IJobStore inner, bool clustered) : DelegatingJobStore(inner)
    {
        private readonly ConcurrentDictionary<string, FireInstance> elsewhere = new(StringComparer.Ordinal);
        private int reads;

        public override bool Clustered => clustered;

        public List<string> Requested { get; } = [];

        public ConcurrentBag<string> Pending { get; } = [];

        public bool FailNextRead { get; set; }

        public TaskCompletionSource ReadAttempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Reads => Volatile.Read(ref reads);

        public void RunningElsewhere(string fireInstanceId, string instanceId)
        {
            elsewhere[fireInstanceId] = new FireInstance(
                fireInstanceId,
                new TriggerKey("long", "clustered-interrupt"),
                jobKey,
                instanceId,
                FireInstanceState.Executing,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                ExecutionGroup: null);
        }

        public override async ValueTask<PagedResult<FireInstance>> QueryFireInstances(FireInstanceQuery query, CancellationToken cancellationToken = default)
        {
            PagedResult<FireInstance> here = await base.QueryFireInstances(query, cancellationToken);
            List<FireInstance> all = [.. here.Items, .. elsewhere.Values.Where(x => query.Job is null || x.JobKey == query.Job)];
            return new PagedResult<FireInstance>(all, HasMore: false);
        }

        public override ValueTask<bool> RequestInterrupt(string fireInstanceId, CancellationToken cancellationToken = default)
        {
            lock (Requested)
            {
                Requested.Add(fireInstanceId);
            }

            return new ValueTask<bool>(elsewhere.ContainsKey(fireInstanceId));
        }

        public override ValueTask<List<string>> GetInterruptRequests(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref reads);
            ReadAttempted.TrySetResult();

            if (FailNextRead)
            {
                FailNextRead = false;
                throw new JobPersistenceException("the database is away");
            }

            return new ValueTask<List<string>>([.. Pending]);
        }
    }

    private sealed class InterruptRecorder : ISchedulerListener
    {
        private readonly TaskCompletionSource<(JobKey, string)> interrupted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int count;

        public Task<(JobKey, string)> Interrupted => interrupted.Task;

        public int Count => Volatile.Read(ref count);

        public ValueTask JobInterrupted(IScheduler scheduler, JobKey jobKey, string fireInstanceId, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref count);
            interrupted.TrySetResult((jobKey, fireInstanceId));
            return default;
        }
    }

    /// <summary>
    /// Runs until its token is cancelled, and says which firing started and which was cancelled.
    /// </summary>
    public sealed class LongJob : IJob
    {
        private static TaskCompletionSource<Started> started = New<Started>();
        private static TaskCompletionSource<string> cancelled = New<string>();

        internal static Task<Started> Started => started.Task;

        internal static Task<string> Cancelled => cancelled.Task;

        public static void Reset()
        {
            started = New<Started>();
            cancelled = New<string>();
        }

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            started.TrySetResult(new Started(context.FireInstanceId));
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                cancelled.TrySetResult(context.FireInstanceId);
                throw;
            }
        }

        private static TaskCompletionSource<T> New<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
