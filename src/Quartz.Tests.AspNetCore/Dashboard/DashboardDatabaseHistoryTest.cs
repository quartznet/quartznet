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

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Dashboard.Services;
using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// The dashboard's History page, reading a history a whole cluster wrote.
/// </summary>
/// <remarks>
/// <para>
/// This is what <see href="https://github.com/quartznet/quartznet/issues/3771">#3771</see> was: the
/// shipped history is per process, so a dashboard attached to a cluster through a shared store showed
/// an empty History page — nothing it read had been written by a node. With
/// <c>UseExecutionHistory()</c> the rows are in the scheduler's own database, every node writes into
/// one feed, and the page's node filter is what tells them apart.
/// </para>
/// <para>
/// Read through <see cref="IDashboardHistoryStore" /> rather than through Quartz's own seam, because
/// that is what the page asks: the point is that the whole chain from the store to the page carries
/// the node.
/// </para>
/// </remarks>
public sealed class DashboardDatabaseHistoryTest
{
    private const string SchedulerName = "cluster";

    private static readonly DateTimeOffset Start = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    private SqliteTestDatabase database = null!;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        database = new SqliteTestDatabase("dashboard-history");
    }

    [TearDown]
    public void DeleteDatabase()
    {
        database.Dispose();
    }

    [Test]
    public async Task TheHistoryPageReadsEveryNodesExecutionsAndCanNarrowToOne()
    {
        ServiceCollection services = new();

        services.AddQuartzDashboard();
        services.AddQuartz(quartz =>
        {
            quartz.ConfigureScheduler(options =>
            {
                options.InstanceName = SchedulerName;
                options.InstanceId = "node-a";
            });

            quartz.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                store.ProvisionSchema();
                store.UseExecutionHistory();
            });
        });

        await using ServiceProvider provider = services.BuildServiceProvider();

        // Building the scheduler is what provisions the schema and tells the driver delegate its table
        // prefix. It is never started: this case is about what a page reads, not about firing.
        await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        IExecutionHistoryStore history = provider.GetRequiredService<IExecutionHistoryStore>();
        history.Should().BeOfType<AdoExecutionHistoryStore>(
            "UseExecutionHistory() is what puts the cluster's history where every node can reach it");

        // Two nodes of one cluster, writing into the same tables. In a real cluster these are two
        // processes; here one store stands in for both, which is exactly what the shared tables make
        // indistinguishable from the page's side.
        await history.AddExecution(Execution(Start.AddMinutes(-2), "import", node: "node-a"));
        await history.AddExecution(Execution(Start.AddMinutes(-1), "report", node: "node-b"));

        IDashboardHistoryStore page = provider.GetRequiredService<IDashboardHistoryStore>();

        PagedResult<DashboardHistoryEntry> everything = await page.QueryExecutions(
            new DashboardHistoryQuery { SchedulerName = SchedulerName, IncludeTotalCount = true });

        everything.Items.Select(entry => entry.JobName).Should().Equal(["report", "import"],
            "a dashboard reading a cluster's store sees what every node ran, newest first — which is "
            + "the whole of what an in-memory history could not do");

        PagedResult<DashboardHistoryEntry> onA = await page.QueryExecutions(
            new DashboardHistoryQuery { SchedulerName = SchedulerName, SchedulerInstanceId = "node-a" });

        onA.Items.Should().ContainSingle().Which.JobName.Should().Be("import",
            "and the node filter is what makes several nodes' rows readable rather than a mixed list");

        onA.Items.Should().AllSatisfy(entry => entry.SchedulerInstanceId.Should().Be("node-a",
            "every row carries the node that produced it, which is what the filter reads"));
    }

    private static ExecutionHistoryEntry Execution(DateTimeOffset firedAt, string jobName, string node) => new(
        SchedulerName: SchedulerName,
        SchedulerInstanceId: node,
        JobGroup: "batch",
        JobName: jobName,
        TriggerGroup: "nightly",
        TriggerName: "at-midnight",
        FiredAtUtc: firedAt,
        Duration: TimeSpan.FromSeconds(2),
        Succeeded: true,
        ExceptionMessage: null);
}
