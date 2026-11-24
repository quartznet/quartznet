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

using FakeItEasy;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// A running firing's log through a cluster of targets (#4437): only the member whose node runs the firing is
/// asked, by the row's node or, without one, the node the store says owns it.
/// </summary>
/// <remarks>
/// Two nodes on one SQLite file, each the SQLite store wrapped to say it is clustered, with its own instance id, as
/// in <c>FiringWaitSqliteTest</c>: SQLite refuses <c>UseClustering()</c>. Node A refuses the job's execution group,
/// so node B runs it, and its lines are in node B's process alone.
/// </remarks>
[NonParallelizable]
public sealed class ClusterFireInstanceLogSqliteTest
{
    private const string SchedulerName = "cluster-running-log";
    private const string RemoteGroup = "remote";

    private static readonly TimeSpan waitLimit = TimeSpan.FromSeconds(30);
    private static readonly JobKey jobKey = new("logging", "running-log");

    private readonly List<(ServiceProvider Node, IScheduler Scheduler)> nodes = [];
    private SqliteTestDatabase database = null!;
    private IScheduler a = null!;
    private IScheduler b = null!;
    private string firing = null!;

    [SetUp]
    public async Task FireOnNodeB()
    {
        database = new SqliteTestDatabase("cluster-running-log");
        RemoteLoggingJob.Reset();

        a = await Node("node-a", refuseRemote: true);
        b = await Node("node-b");
        await a.AddJob(JobBuilder.Create<RemoteLoggingJob>().WithIdentity(jobKey).StoreDurably().Build());
        await a.Start();
        await b.Start();

        await a.TriggerJob(jobKey);
        (string fireInstanceId, string ranOn) = await RemoteLoggingJob.Logged.Task.WaitAsync(waitLimit);
        ranOn.Should().Be("node-b", "the premise: node A refuses the job's group, so B runs it");
        firing = fireInstanceId;
    }

    [TearDown]
    public async Task ShutDownEveryNode()
    {
        // The job first, or a shutdown that waits for it would wait for ever; then every node, whatever the test
        // did: a node left running holds the file open.
        RemoteLoggingJob.Release.TrySetResult();
        foreach ((ServiceProvider node, IScheduler scheduler) in nodes)
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
            await node.DisposeAsync();
        }

        nodes.Clear();
        database.Dispose();
    }

    [Test]
    public async Task TheNodeThatDoesNotRunTheFiringSaysSoUnderItsOwnId()
    {
        FireInstanceLog log = await FireInstanceLogs.Read(a, Request(node: null));

        log.State.Should().Be(FireInstanceLogState.NotRunning, "the lines are in node B's process, not A's");
        log.SchedulerInstanceId.Should().Be("node-a", "which is how the dashboard tells this from a firing that ended");
    }

    /// <summary>
    /// The design guard of the routing: forwarded to the preferred member, as a store read is, the read would reach
    /// node A, which answers that it runs no such firing.
    /// </summary>
    [Test]
    public async Task WithTheRowsNodeTheClusterAsksThatNodeAlone()
    {
        ClusterAwareScheduler cluster = Cluster(("a", a, "node-a"), ("b", b, "node-b"));

        FireInstanceLog log = await FireInstanceLogs.Read(cluster, Request(node: "node-b"));

        log.State.Should().Be(FireInstanceLogState.Running);
        log.SchedulerInstanceId.Should().Be("node-b");
        log.Entries.Should().ContainSingle().Which.Should().EndWith("running on node B");
    }

    [Test]
    public async Task WithoutTheRowsNodeTheStoreSaysWhichNodeOwnsTheFiring()
    {
        ClusterAwareScheduler cluster = Cluster(("a", a, "node-a"), ("b", b, "node-b"));

        FireInstanceLog log = await FireInstanceLogs.Read(cluster, Request(node: null));

        log.State.Should().Be(FireInstanceLogState.Running, "every node sees every firing of the shared store, and B owns this one");
        log.SchedulerInstanceId.Should().Be("node-b");
    }

    [Test]
    public async Task AnOwnerNoMemberFrontsIsUnreachableNamingIt()
    {
        IScheduler elsewhere = A.Fake<IScheduler>();
        ClusterAwareScheduler cluster = Cluster(("a", a, "node-a"), ("c", elsewhere, "node-c"));

        FireInstanceLog log = await FireInstanceLogs.Read(cluster, Request(node: null));

        log.State.Should().Be(FireInstanceLogState.Unreachable, "node B runs the firing, and no member of this cluster reaches it");
        log.SchedulerInstanceId.Should().Be("node-b");
        Fake.GetCalls(elsewhere).Should().BeEmpty("only the member running the firing could answer, and none does");
    }

    [Test]
    public async Task AFiringTheStoreNoLongerListsIsNotRunningWithNoNode()
    {
        ClusterAwareScheduler cluster = Cluster(("a", a, "node-a"), ("b", b, "node-b"));

        FireInstanceLog log = await FireInstanceLogs.Read(cluster, new FireInstanceLogRequest("ended-long-ago", 0, FireInstanceLogs.DefaultTake, null));

        log.State.Should().Be(FireInstanceLogState.NotRunning);
        log.SchedulerInstanceId.Should().BeNull("no node owns a firing the store does not list");
    }

    private FireInstanceLogRequest Request(string? node)
    {
        return new FireInstanceLogRequest(firing, After: 0, FireInstanceLogs.DefaultTake, node);
    }

    private static ClusterAwareScheduler Cluster(params (string Target, IScheduler Scheduler, string InstanceId)[] members)
    {
        return new ClusterAwareScheduler(
            string.Join('+', members.Select(x => x.Target)),
            [.. members.Select(x => new ClusterMember(x.Target, x.Scheduler, x.InstanceId))],
            preferred: 0);
    }

    private async Task<IScheduler> Node(string instanceId, bool refuseRemote = false)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = SchedulerName;
                options.InstanceId = instanceId;
                options.IdleWaitTime = TimeSpan.FromSeconds(1);
            });

            if (refuseRemote)
            {
                q.RefuseExecutionGroups(RemoteGroup);
            }

            // First wins: the persistent store below is configured, and this is the store built.
            q.UseJobStore(provider => new ClusterMemberStore(ActivatorUtilities.CreateInstance<LocalTransactionJobStore>(provider)));
            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                store.ProvisionSchema();
            });

            q.UseExecutionLogCapture();
        });

        ServiceProvider node = services.BuildServiceProvider();
        IScheduler scheduler = await node.GetRequiredService<ISchedulerFactory>().GetScheduler();
        nodes.Add((node, scheduler));
        return scheduler;
    }

    /// <summary>Logs one line where it runs, and runs until it is released.</summary>
    [ExecutionGroup(RemoteGroup)]
    public sealed class RemoteLoggingJob : IJob
    {
        private readonly ILogger<RemoteLoggingJob> logger;

        public RemoteLoggingJob(ILogger<RemoteLoggingJob> logger)
        {
            this.logger = logger;
        }

        /// <summary>The firing's fire instance id and the node running it, once it has logged.</summary>
        public static TaskCompletionSource<(string FireInstanceId, string Node)> Logged { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static TaskCompletionSource Release { get; private set; } = New();

        public static void Reset()
        {
            Logged = new TaskCompletionSource<(string FireInstanceId, string Node)>(TaskCreationOptions.RunContinuationsAsynchronously);
            Release = New();
        }

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            string node = context.Scheduler.SchedulerInstanceId;
            logger.LogInformation("running on node {Node}", node == "node-b" ? "B" : "A");
            Logged.TrySetResult((context.FireInstanceId, node));
            await Release.Task.WaitAsync(waitLimit, cancellationToken);
        }

        private static TaskCompletionSource New() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>
    /// The SQLite store, saying it is clustered: a node of a cluster, which SQLite itself refuses to be.
    /// </summary>
    private sealed class ClusterMemberStore(IJobStore inner) : DelegatingJobStore(inner)
    {
        public override bool Clustered => true;
    }
}
