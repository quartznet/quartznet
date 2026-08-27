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

using System.Data.Common;

namespace Quartz.Examples.Example13;

/// <summary>
/// This example will demonstrate the clustering features of the ADO.NET job store.
/// </summary>
/// <remarks>
/// <para>
/// A cluster is several scheduler instances sharing one database. This example is one node of one:
/// run it in a second terminal and the two share the work, because they share an
/// <c>InstanceName</c> and the tables it names. Kill one and the other recovers the jobs it was
/// running, which is what <c>RequestRecovery()</c> asks for.
/// </para>
/// <para>
/// It needs a SQL Server with the Quartz schema in it, named by the <c>QUARTZ_EXAMPLES_SQLSERVER</c>
/// environment variable. The DDL is <c>database/tables/tables_sqlserver.sql</c> in this repository, and
/// <c>src/Quartz.Examples/README.md</c> has the one-line docker command that produces a server to run
/// it against. With the variable unset the example says so and stops, rather than reaching for a
/// default that would have to spell a password in this file.
/// </para>
/// <para>
/// <i>Note:</i> Never run clustering on separate machines, unless their clocks are synchronized using
/// some form of time-sync service (daemon). A node decides that another has died by comparing its own
/// clock with a check-in time the other one wrote.
/// </para>
/// </remarks>
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
public class ClusteringJobsExecutionExample : IExample
{
    /// <summary>
    /// The environment variable this example takes its connection string from.
    /// </summary>
    /// <remarks>
    /// There is no default, and deliberately so: a connection string carries a credential, and a
    /// credential written into a source file is a credential that has leaked. The readme has the shape
    /// of the value, beside a docker command that produces a database to point it at.
    /// </remarks>
    private const string ConnectionStringVariable = "QUARTZ_EXAMPLES_SQLSERVER";

    /// <summary>
    /// The group the jobs live in. Fixed rather than named after the node, because every node
    /// schedules the same jobs and the point is that any of them may run any of them.
    /// </summary>
    private const string JobGroup = "cluster-demo";

    public async ValueTask Run(CancellationToken cancellationToken = default)
    {
        string? connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine($"Set {ConnectionStringVariable} to a SQL Server connection string - server, database and credentials - for a database carrying the Quartz schema, and run this again. src/Quartz.Examples/README.md has the shape of the value and a docker command that produces the database.");
            return;
        }

        Console.WriteLine("------- Initializing ----------------------");

        QuartzSchedulerBuilder builder = QuartzSchedulerBuilder.Create();

        builder
            .ConfigureScheduler(options =>
            {
                // every node in the cluster shares the instance name: it is what makes them one cluster
                options.InstanceName = "QuartzExamplesCluster";

                // ...and each needs its own id. Generating one is the easy way to be sure; set
                // InstanceId explicitly when something outside Quartz has to name the node.
                options.GenerateInstanceId = true;
            })
            .UseDefaultThreadPool(maxConcurrency: 5)
            .UsePersistentStore(store =>
            {
                store.UseSqlServer(connectionString);

                // if running SQLite this would be UseSystemDataSqlite (System.Data.SQLite) or
                // UseSqlite (Microsoft.Data.Sqlite), plus UseLockHandler<UpdateRowLockHandler>()

                store.UseClustering(cluster =>
                {
                    // how often this node says "still alive", and how long past a missed check-in
                    // another node waits before taking its work over. 7.5 seconds each by default,
                    // shortened here so that killing a node has a visible effect within the run.
                    cluster.CheckinInterval = TimeSpan.FromSeconds(5);
                    cluster.CheckinMisfireThreshold = TimeSpan.FromSeconds(5);
                });

                store.UseSystemTextJsonSerializer();
                store.ConfigureStore(options =>
                {
                    options.StoreJobDataAsStrings = true;
                    options.MisfireThreshold = TimeSpan.FromSeconds(60);
                });
            });

        IScheduler scheduler;
        try
        {
            scheduler = await builder.BuildScheduler(cancellationToken);
        }
        catch (Exception ex) when (ex is SchedulerException or DbException)
        {
            // the connection string itself is not echoed back: it carries a credential, and a console
            // is somewhere a log scraper and a screen recording both reach
            Console.Error.WriteLine($"Could not reach the database {ConnectionStringVariable} names.");
            Console.Error.WriteLine("  create a database and run database/tables/tables_sqlserver.sql against it,");
            Console.Error.WriteLine($"  or point {ConnectionStringVariable} somewhere else.");
            Console.Error.WriteLine("  src/Quartz.Examples/README.md has a docker command that produces one.");
            Console.Error.WriteLine(ex.Message);
            return;
        }

        Console.WriteLine($"------- Initialization Complete ----------- this node is {scheduler.SchedulerInstanceId}");

        Console.WriteLine("------- Scheduling Jobs ------------------");

        // Every node schedules the same jobs into the same group, replacing what is already there. The
        // first node to start puts them in; the rest find them and carry on. Which node then runs a
        // given firing is the cluster's decision, and watching that decision is the point.
        await Schedule<SimpleRecoveryJob>(scheduler, "job_1", TimeSpan.FromSeconds(10), cancellationToken);
        await Schedule<SimpleRecoveryJob>(scheduler, "job_2", TimeSpan.FromSeconds(12), cancellationToken);
        await Schedule<SimpleRecoveryStatefulJob>(scheduler, "job_3", TimeSpan.FromSeconds(14), cancellationToken);

        Console.WriteLine("------- Starting Scheduler ---------------");
        await scheduler.Start(cancellationToken);
        Console.WriteLine("------- Started Scheduler ----------------");

        Console.WriteLine("------- Start this example again in another terminal to add a node,");
        Console.WriteLine("------- then kill one of them and watch the other recover its jobs.");
        Console.WriteLine("------- (Ctrl+C stops early)");

        try
        {
            // five minutes, which is long enough to start a second node and kill it again
            DateTimeOffset until = TimeProvider.System.GetUtcNow().AddMinutes(5);

            while (TimeProvider.System.GetUtcNow() < until)
            {
                await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
                await ReportCluster(scheduler, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("------- Seen enough, shutting down -------");
        }

        Console.WriteLine("------- Shutting Down --------------------");
        await scheduler.Shutdown(waitForJobsToComplete: true, CancellationToken.None);
        Console.WriteLine("------- Shutdown Complete ----------------");
    }

    private static async ValueTask Schedule<TJob>(
        IScheduler scheduler,
        string name,
        TimeSpan interval,
        CancellationToken cancellationToken) where TJob : IJob
    {
        IJobDetail job = JobBuilder.Create<TJob>()
            .WithIdentity(name, JobGroup)
            // ask the cluster to re-run this job if the node running it dies mid-execution
            .RequestRecovery()
            .Build();

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity(name, JobGroup)
            .StartAt(DateTimeOffset.UtcNow.AddSeconds(5))
            .WithSimpleSchedule(x => x.WithInterval(interval).RepeatForever())
            .Build();

        await scheduler.ScheduleJob(job, [trigger], new ScheduleJobOptions { Replace = true }, cancellationToken);

        Console.WriteLine($"{job.Key} every {interval.TotalSeconds:0} seconds, recoverable");
    }

    /// <summary>
    /// What this node believes about the cluster, read off the check-in table.
    /// </summary>
    private static async ValueTask ReportCluster(IScheduler scheduler, CancellationToken cancellationToken)
    {
        List<ClusterNode> nodes = await scheduler.QueryClusterNodes(cancellationToken);

        Console.WriteLine($"------- Cluster: {nodes.Count} node(s) -------------");

        foreach (ClusterNode node in nodes)
        {
            string marker = node.IsCurrentNode ? " (this node)" : "";
            Console.WriteLine($"        {node.InstanceId}{marker}: {node.State}, last check-in {node.LastCheckInUtc?.LocalDateTime:HH:mm:ss}");
        }
    }
}
