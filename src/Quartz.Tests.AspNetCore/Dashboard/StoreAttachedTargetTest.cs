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

using System.Globalization;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using Quartz.Configuration;
using Quartz.Dashboard.Services;
using Quartz.Extensibility;
using Quartz.Tests.AspNetCore.Dashboard.Support;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// A dashboard pointed at a database, showing the scheduler another process runs out of it.
/// </summary>
/// <remarks>
/// <para>
/// This is <see href="https://github.com/quartznet/quartznet/issues/3772">#3772</see> end to end: a real
/// scheduler writes into a SQLite database — its job, its trigger, its firing, its execution history,
/// and a check-in row — and a second container that has never heard of it lists it as a window, reads
/// its schedule, its nodes and its history, and refuses everything that belongs to the process running
/// it.
/// </para>
/// <para>
/// The check-in row is written by hand because SQLite refuses to be clustered, so no store can write one
/// against this database. The row is what a clustered node would have written and is in exactly the
/// format the delegate reads: the check-in as UTC ticks and the interval as whole milliseconds. The
/// window's reading of it is the point of the case, not who put it there.
/// </para>
/// </remarks>
public sealed class StoreAttachedTargetTest
{
    private const string Target = "test";
    private const string ClusterScheduler = "reporting";
    private const string NodeA = "node-a";

    private static readonly TimeSpan CheckInInterval = TimeSpan.FromSeconds(30);

    private SqliteTestDatabase database = null!;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        database = new SqliteTestDatabase("store-attached");
    }

    [TearDown]
    public void DeleteDatabase()
    {
        database.Dispose();
    }

    [Test]
    public async Task AWindowShowsTheClustersSchedule()
    {
        await using ServiceProvider node = await StartNode(ClusterScheduler);
        await RecordExecution(node, "import");

        WriteCheckIn(ClusterScheduler, NodeA, DateTimeOffset.UtcNow);

        await using DashboardHost dashboard = await DashboardHost.Attached(database);
        IQuartzApiClient client = dashboard.Client;

        List<SchedulerHeaderDto> schedulers = await client.GetSchedulers();
        SchedulerHeaderDto window = schedulers.Should().ContainSingle(x => x.SchedulerName == ClusterScheduler).Subject;

        window.Origin.Should().Be(SchedulerOrigin.Window,
            "nothing in this process runs it - it was found in a database, not registered");
        window.Target.Should().Be(Target, "the target says which database the name was found in");
        window.DisplayName.Should().Be("test/reporting",
            "a window's identity is the target and the scheduler name, and the dashboard spells it that way");
        window.Status.Should().Be(SchedulerStatus.Running,
            "a node is checking in, and the cluster's check-ins are the only liveness a shared store carries - "
            + "the window's own scheduler has never been started and never will be");

        PagedResult<JobKeyDto> jobs = await client.QueryJobs(ClusterScheduler, new DashboardJobQuery());
        jobs.Items.Should().ContainSingle(x => x.Name == "nightly",
            "the store is the contract: the job the other process stored is readable here without it being asked");

        PagedResult<TriggerHeaderDto> triggers = await client.QueryTriggers(ClusterScheduler, new DashboardTriggerQuery());
        triggers.Items.Should().ContainSingle(x => x.Name == "at-midnight" && x.Group == "nightly");

        List<ClusterNodeDto> nodes = await client.QueryClusterNodes(ClusterScheduler);
        nodes.Should().ContainSingle().Which.InstanceId.Should().Be(NodeA,
            "the window is not one of the cluster's nodes and must never be listed as one: it writes no "
            + "check-in row and can run nothing");
        nodes[0].State.Should().Be(ClusterNodeState.Alive);
        nodes[0].IsCurrentNode.Should().BeFalse("no row here belongs to the process reading them");

        PagedResult<DashboardHistoryEntry> executions = await client.QueryExecutions(
            new DashboardHistoryQuery { SchedulerName = ClusterScheduler });

        executions.Items.Should().ContainSingle()
            .Which.Should().Match<DashboardHistoryEntry>(
                entry => entry.JobName == "import" && entry.SchedulerInstanceId == NodeA,
                "the history is in the database the nodes share, so a window reads what they ran - which is the "
                + "whole reason the ADO history store exists");
    }

    [Test]
    public async Task AWindowWithNoLiveNodeIsNotRunning()
    {
        await using ServiceProvider node = await StartNode(ClusterScheduler);

        // The row a node that stopped an hour ago left behind: written, then never renewed.
        WriteCheckIn(ClusterScheduler, NodeA, DateTimeOffset.UtcNow - TimeSpan.FromHours(1));

        await using DashboardHost dashboard = await DashboardHost.Attached(database);

        List<SchedulerHeaderDto> schedulers = await dashboard.Client.GetSchedulers();
        schedulers.Should().ContainSingle(x => x.SchedulerName == ClusterScheduler)
            .Which.Status.Should().Be(SchedulerStatus.Shutdown,
                "every node that ever checked in has stopped, and a window judges a row by the interval the row "
                + "itself promised rather than by how long this process has been up");

        List<ClusterNodeDto> nodes = await dashboard.Client.QueryClusterNodes(ClusterScheduler);
        nodes.Should().ContainSingle().Which.State.Should().Be(ClusterNodeState.Failed);
    }

    [Test]
    public async Task AWindowWithNoCheckInRowSaysSoRatherThanGuessing()
    {
        await using ServiceProvider node = await StartNode(ClusterScheduler);

        await using DashboardHost dashboard = await DashboardHost.Attached(database);

        List<SchedulerHeaderDto> schedulers = await dashboard.Client.GetSchedulers();
        schedulers.Should().ContainSingle(x => x.SchedulerName == ClusterScheduler)
            .Which.Status.Should().Be(SchedulerStatus.Unknown,
                "a scheduler whose store is not clustered writes no check-in row at all, so there is nothing to "
                + "judge - and reporting it as stopped would be the false-dead reading shared-storage dashboards "
                + "are known for");
    }

    [Test]
    public async Task AWindowRefusesWhatBelongsToOneProcess()
    {
        await using ServiceProvider node = await StartNode(ClusterScheduler);
        WriteCheckIn(ClusterScheduler, NodeA, DateTimeOffset.UtcNow);

        await using DashboardHost dashboard = await DashboardHost.Attached(database);
        IQuartzApiClient client = dashboard.Client;

        Func<Task> start = async () => await client.Start(ClusterScheduler);
        await start.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*window*",
                "starting a window would start the never-started scheduler this process built, and the nodes "
                + "doing the work would not notice");

        Func<Task> standby = async () => await client.Standby(ClusterScheduler);
        await standby.Should().ThrowAsync<NotSupportedException>();

        Func<Task> shutdown = async () => await client.Shutdown(ClusterScheduler);
        await shutdown.Should().ThrowAsync<NotSupportedException>();

        Func<Task> interrupt = async () => await client.InterruptFireInstance(ClusterScheduler, "whatever");
        await interrupt.Should().ThrowAsync<NotSupportedException>();

        // And the other half of the promise: what the store is, the window can do.
        bool paused = await client.PauseTrigger(ClusterScheduler, new TriggerKeyDto("nightly", "at-midnight"));
        paused.Should().BeTrue("pausing a trigger is a write to the shared tables, and any node will honour it");

        TriggerState state = await client.GetTriggerState(ClusterScheduler, new TriggerKeyDto("nightly", "at-midnight"));
        state.Should().Be(TriggerState.Paused);
    }

    /// <summary>
    /// A window onto a store attached without <c>UseExecutionHistory()</c> says there is no history
    /// there, naming the store and the window, rather than showing this process's own history.
    /// </summary>
    [Test]
    public async Task AWindowOntoAStoreWithoutHistorySaysWhichStoreHasNone()
    {
        await using ServiceProvider node = await StartNode(ClusterScheduler);

        await using DashboardHost dashboard = await DashboardHost.Attached(database, history: false);

        Func<Task> read = async () => await dashboard.Client.QueryExecutions(
            new DashboardHistoryQuery { SchedulerName = ClusterScheduler });

        await read.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("The store attached as 'test', which 'reporting' is a window onto, keeps no execution history*UseExecutionHistory()*",
                "the page shows this text as it stands, so it has to say which store has no history and which "
                + "window reads through it - in a sentence that parses");
    }

    [Test]
    public async Task AWindowHasNoLiveEventStream()
    {
        await using ServiceProvider node = await StartNode(ClusterScheduler);

        await using DashboardHost dashboard = await DashboardHost.Attached(database);

        SchedulerEventSources.For(dashboard.Provider, ClusterScheduler).Should().BeNull(
            "a database carries a schedule, not a feed of what happened to it - and answering with this "
            + "process's own broker would show another cluster's name over this process's firings");

        SchedulerEventSources.For(dashboard.Provider, "local").Should().NotBeNull(
            "a scheduler of this process still has the one the container registered");
    }

    [Test]
    public async Task ANameThisProcessAlreadyUsesIsRefusedNamingBoth()
    {
        await using ServiceProvider node = await StartNode(ClusterScheduler);

        await using DashboardHost dashboard = await DashboardHost.Attached(
            database,
            services => services.AddQuartz(ClusterScheduler, q => q.UseInMemoryStore()));

        AttachedStore store = dashboard.Provider.GetRequiredService<AttachedStores>().Stores.Should().ContainSingle().Subject;

        store.Refusals.Should().ContainKey(ClusterScheduler)
            .WhoseValue.Should().Contain("test/reporting").And.Contain("'reporting'",
                "a scheduler in the database that cannot be shown must say so naming both the window it would "
                + "have been and the scheduler already wearing the name");

        List<SchedulerHeaderDto> schedulers = await dashboard.Client.GetSchedulers();
        schedulers.Should().ContainSingle(x => x.SchedulerName == ClusterScheduler)
            .Which.Origin.Should().Be(SchedulerOrigin.Container,
                "the scheduler under that name is this container's, and it keeps the name");
    }

    [Test]
    public async Task RediscoveryPicksUpASchedulerThatAppearsLater()
    {
        await using ServiceProvider node = await StartNode(ClusterScheduler);

        FakeTimeProvider clock = new(DateTimeOffset.UtcNow);
        await using DashboardHost dashboard = await DashboardHost.Attached(
            database,
            services => services.AddSingleton<TimeProvider>(clock));

        (await dashboard.Client.GetSchedulers()).Should().NotContain(x => x.SchedulerName == "billing");

        // A second service starts scheduling into the same tables, which is the case a one-off discovery
        // would never notice.
        await using ServiceProvider later = await StartNode("billing");

        clock.Advance(TimeSpan.FromMinutes(1));

        List<SchedulerHeaderDto> schedulers = [];
        await Eventually.Until(() =>
        {
            schedulers = dashboard.Client.GetSchedulers().AsTask().GetAwaiter().GetResult();
            return schedulers.Exists(x => x.SchedulerName == "billing");
        });

        schedulers.Should().ContainSingle(x => x.SchedulerName == "billing")
            .Which.Origin.Should().Be(SchedulerOrigin.Window);
    }

    /// <summary>
    /// A scheduler of another process: a real container, a real store, a job and a trigger in the shared
    /// database.
    /// </summary>
    private async ValueTask<ServiceProvider> StartNode(string schedulerName)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddQuartz(quartz =>
        {
            quartz.ConfigureScheduler(options =>
            {
                options.InstanceName = schedulerName;
                options.InstanceId = NodeA;
            });

            quartz.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                store.ProvisionSchema();
                store.UseExecutionHistory();
            });
        });

        ServiceProvider provider = services.BuildServiceProvider();

        // Creating the scheduler provisions the schema; scheduling is what puts this scheduler's name in
        // the tables the dashboard discovers it from.
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        await scheduler.ScheduleJob(
            JobBuilder.Create<NightlyJob>().WithIdentity("nightly", "batch").StoreDurably().Build(),
            TriggerBuilder.Create()
                .WithIdentity("at-midnight", "nightly")
                .StartAt(DateTimeOffset.UtcNow.AddYears(1))
                .Build());

        return provider;
    }

    /// <summary>
    /// What a firing leaves behind, written through the store the nodes share.
    /// </summary>
    /// <remarks>
    /// Written rather than fired, for the reason <c>DashboardDatabaseHistoryTest</c> writes its rows: the
    /// case is about what a window can read out of the database, and a real firing would make it about
    /// how long a job takes to start.
    /// </remarks>
    private static ValueTask RecordExecution(IServiceProvider node, string jobName)
    {
        return node.GetRequiredService<IExecutionHistoryStore>().AddExecution(new ExecutionHistoryEntry(
            SchedulerName: ClusterScheduler,
            SchedulerInstanceId: NodeA,
            JobGroup: "batch",
            JobName: jobName,
            TriggerGroup: "nightly",
            TriggerName: "at-midnight",
            FiredAtUtc: DateTimeOffset.UtcNow.AddMinutes(-1),
            Duration: TimeSpan.FromSeconds(2),
            Succeeded: true,
            ExceptionMessage: null));
    }

    /// <summary>
    /// The check-in row a clustered node writes, in the format <c>StdAdoDelegate</c> reads it back in:
    /// the timestamp as UTC ticks, the interval as whole milliseconds.
    /// </summary>
    private void WriteCheckIn(string schedulerName, string instanceId, DateTimeOffset checkedInAt)
    {
        using SqliteConnection connection = new(database.ConnectionString);
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO QRTZ_SCHEDULER_STATE (SCHED_NAME, INSTANCE_NAME, LAST_CHECKIN_TIME, CHECKIN_INTERVAL) "
            + "VALUES (@scheduler, @instance, @checkin, @interval)";
        command.Parameters.AddWithValue("@scheduler", schedulerName);
        command.Parameters.AddWithValue("@instance", instanceId);
        command.Parameters.AddWithValue("@checkin", checkedInAt.UtcTicks);
        command.Parameters.AddWithValue(
            "@interval",
            (long) CheckInInterval.TotalMilliseconds);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// A dashboard process: its container, its discovery started, and the client its pages read through.
    /// </summary>
    private sealed class DashboardHost : IAsyncDisposable
    {
        private DashboardHost(ServiceProvider provider, IQuartzApiClient client)
        {
            Provider = provider;
            Client = client;
        }

        public ServiceProvider Provider { get; }

        public IQuartzApiClient Client { get; }

        public static async ValueTask<DashboardHost> Attached(
            SqliteTestDatabase database,
            Action<IServiceCollection>? configure = null,
            bool history = true)
        {
            ServiceCollection services = new();
            services.AddLogging();

            // A scheduler of this process, so that every case says something about the difference between
            // one of these and a window.
            services.AddQuartz("local", q => q.UseInMemoryStore());

            services.AddQuartzDashboard(options => options.AttachStore(
                Target,
                store =>
                {
                    store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);

                    if (history)
                    {
                        store.UseExecutionHistory();
                    }
                }));

            configure?.Invoke(services);

            ServiceProvider provider = services.BuildServiceProvider();

            // The hosted service the dashboard registers, run on its own: the rest of what a host starts
            // needs mapped endpoints, and this case is about what discovery does.
            AttachedStoreDiscovery discovery = provider.GetServices<IHostedService>()
                .OfType<AttachedStoreDiscovery>()
                .Single();

            await discovery.StartAsync(CancellationToken.None);

            IOptions<QuartzDashboardOptions> options = provider.GetRequiredService<IOptions<QuartzDashboardOptions>>();
            InProcessQuartzApiClient client = new(
                provider.GetRequiredService<ISchedulerRepository>(),
                provider.GetRequiredService<ISchedulerRegistry>(),
                options,
                provider.GetRequiredService<IExecutionHistoryStore>(),
                provider,
                new SchedulerAuthorization(options, new TestSchedulerAuthorizationService(), new TestAuthenticationStateProvider()),
                provider.GetRequiredService<AttachedStores>());

            return new DashboardHost(provider, client);
        }

        public async ValueTask DisposeAsync()
        {
            await Provider.DisposeAsync();
        }
    }

    private sealed class NightlyJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }
}
