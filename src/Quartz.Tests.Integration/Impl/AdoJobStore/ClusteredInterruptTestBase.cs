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

using System.Collections.Concurrent;
using System.Diagnostics;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// An interrupt asked of one node for a firing another node runs, through the store they share (#4071):
/// the asking node records a request on the firing's row, and the running node cancels the job's token
/// after its next check-in.
/// </summary>
/// <remarks>
/// SQLite refuses clustering, so the unit suite drives two stores on one file by hand; this is the same
/// claim with both nodes started and checking in on their own, every second.
/// </remarks>
public abstract class ClusteredInterruptTestBase : ClusteredJobStoreTestBase
{
    private const string Group = "clusterInterrupt";
    private const string NodeA = "interruptNodeA";
    private const string NodeB = "interruptNodeB";

    /// <summary>
    /// What a remote interrupt is given to land: the nodes check in every second, and a request lands
    /// after the next one, so ten seconds is ten check-ins of slack for a loaded machine.
    /// </summary>
    private const int DeliveryBoundMs = 10_000;

    protected ClusteredInterruptTestBase(string provider) : base(provider)
    {
    }

    protected override string SchedulerName => "ClusterInterruptTest";

    [SetUp]
    public void ResetJob() => RemoteJob.Reset();

    [TearDown]
    public void LetGo() => RemoteJob.Stop();

    [Test]
    public async Task AnInterruptAskedOfAnotherNodeCancelsTheJobOnTheNodeRunningIt()
    {
        IScheduler nodeA = await CreateScheduler(NodeA);
        IScheduler nodeB = await CreateScheduler(NodeB);
        InterruptRecorder interrupts = new();
        OutcomeRecorder outcomes = new();
        JobKey jobKey = new("asked-elsewhere", Group);

        try
        {
            nodeA.ListenerManager.AddSchedulerListener(interrupts);
            nodeA.ListenerManager.AddJobListener(outcomes);
            await nodeA.Start();
            await nodeB.Start();

            string firing = await RunOnNodeA(nodeA, jobKey);

            Stopwatch asked = Stopwatch.StartNew();
            (await nodeB.Interrupt(jobKey)).Should().BeTrue(
                "node B runs nothing of the job, and recording a request for the node that does is its interrupt");

            await WaitForCondition(
                () => Task.FromResult(RemoteJob.Cancelled.Contains(firing)),
                DeliveryBoundMs,
                async () => $"node A to cancel the firing after one of its check-ins. State:\n{await DumpDatabaseState()}");
            TimeSpan delivered = asked.Elapsed;

            await WaitForCondition(
                () => Task.FromResult(outcomes.Outcomes.ContainsKey(firing)),
                DeliveryBoundMs,
                "the interrupted firing to complete on node A");

            interrupts.FireInstanceIds.Should().Equal([firing],
                "node A announces the interrupt it carried out, as it announces a local one");
            outcomes.Outcomes[firing].Should().Be(ExecutionOutcome.Cancelled, "the firing ends as an interrupted one");
            delivered.Should().BeLessThan(TimeSpan.FromMilliseconds(DeliveryBoundMs));
            TestContext.Out.WriteLine($"Remote interrupt delivered in {delivered.TotalMilliseconds:F0} ms with a one-second check-in.");
        }
        finally
        {
            RemoteJob.Stop();
            await nodeA.Shutdown(waitForJobsToComplete: false);
            await nodeB.Shutdown(waitForJobsToComplete: false);
        }
    }

    [Test]
    public async Task AnInterruptOfOneFireInstanceAskedOfAnotherNodeCancelsThatFiring()
    {
        IScheduler nodeA = await CreateScheduler(NodeA);
        IScheduler nodeB = await CreateScheduler(NodeB);
        JobKey jobKey = new("one-firing", Group);

        try
        {
            await nodeA.Start();
            await nodeB.Start();

            string firing = await RunOnNodeA(nodeA, jobKey);

            PagedResult<FireInstance> executing = await nodeB.QueryFireInstances(new FireInstanceQuery { Job = jobKey });
            executing.Items.Should().ContainSingle().Which.SchedulerInstanceId.Should().Be(NodeA,
                "the premise: node B sees the firing, and that node A runs it");

            (await nodeB.InterruptFireInstance(firing)).Should().BeTrue();

            await WaitForCondition(
                () => Task.FromResult(RemoteJob.Cancelled.Contains(firing)),
                DeliveryBoundMs,
                async () => $"node A to cancel the firing after one of its check-ins. State:\n{await DumpDatabaseState()}");
        }
        finally
        {
            RemoteJob.Stop();
            await nodeA.Shutdown(waitForJobsToComplete: false);
            await nodeB.Shutdown(waitForJobsToComplete: false);
        }
    }

    /// <summary>
    /// Schedules the job pinned to node A, so which node runs it is a premise rather than an observation,
    /// and waits for it to start.
    /// </summary>
    private async Task<string> RunOnNodeA(IScheduler nodeA, JobKey jobKey)
    {
        IJobDetail job = JobBuilder.Create<RemoteJob>().WithIdentity(jobKey).Build();
        await nodeA.ScheduleJob(job, TriggerBuilder.Create()
            .WithIdentity(jobKey.Name, Group)
            .ForJob(job)
            .WithPreferredNode(PreferredNode.For(NodeA))
            .StartNow()
            .Build());

        await WaitForCondition(
            () => Task.FromResult(!RemoteJob.Started.IsEmpty),
            timeoutMs: 60_000,
            async () => $"the job to start on {NodeA}. State:\n{await DumpDatabaseState()}");

        (string fireInstanceId, string node) = RemoteJob.Started.Single();
        node.Should().Be(NodeA, "the trigger is pinned to node A");
        return fireInstanceId;
    }

    private sealed class InterruptRecorder : ISchedulerListener
    {
        private readonly ConcurrentQueue<string> fireInstanceIds = new();

        public IReadOnlyList<string> FireInstanceIds => fireInstanceIds.ToList();

        public ValueTask JobInterrupted(IScheduler scheduler, JobKey jobKey, string fireInstanceId, CancellationToken cancellationToken = default)
        {
            fireInstanceIds.Enqueue(fireInstanceId);
            return default;
        }
    }

    private sealed class OutcomeRecorder : IJobListener
    {
        public string Name => nameof(OutcomeRecorder);

        public ConcurrentDictionary<string, ExecutionOutcome> Outcomes { get; } = new(StringComparer.Ordinal);

        public ValueTask JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;

        public ValueTask JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;

        public ValueTask JobWasExecuted(IJobExecutionContext context, JobExecutionException jobException, CancellationToken cancellationToken = default)
        {
            Outcomes[context.FireInstanceId] = context.Outcome;
            return default;
        }
    }

    /// <summary>
    /// Runs until its token is cancelled or the test ends, and records where it ran and what was cancelled.
    /// </summary>
    public sealed class RemoteJob : IJob
    {
        private static CancellationTokenSource stop = new();

        public static ConcurrentQueue<(string FireInstanceId, string Node)> Started { get; private set; } = new();

        public static ConcurrentQueue<string> Cancelled { get; private set; } = new();

        public static void Reset()
        {
            stop = new CancellationTokenSource();
            Started = new ConcurrentQueue<(string, string)>();
            Cancelled = new ConcurrentQueue<string>();
        }

        public static void Stop() => stop.Cancel();

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Started.Enqueue((context.FireInstanceId, context.Scheduler.SchedulerInstanceId));
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stop.Token);

            try
            {
                await Task.Delay(Timeout.Infinite, linked.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Cancelled.Enqueue(context.FireInstanceId);
                throw;
            }
            catch (OperationCanceledException)
            {
                // The test is over.
            }
        }
    }
}
