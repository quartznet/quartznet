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

using System.Globalization;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Core;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// What a manual firing reads to find the execution group its job's triggers share, and the group it finds
/// (#4174).
/// </summary>
/// <remarks>
/// <para>
/// A manual firing of a job whose type declares no group counts against the one group every stored trigger
/// of the job carries (#4092). It used to read every trigger's header to find it: every column, a probe of
/// the fired triggers a row, and a sort, on every <c>TriggerJob</c>, and a manual firing that has not run yet
/// is one more trigger for the next one to read. A delegate Quartz ships now reads the group column alone.
/// Any other delegate keeps the listing, so an override of it still runs.
/// </para>
/// <para>
/// Run with both, since both have to give the same group. A scheduler over a SQLite file of its own, never
/// started, with every statement counted.
/// </para>
/// </remarks>
[TestFixture(true)]
[TestFixture(false)]
[NonParallelizable]
public sealed class ManualFiringExecutionGroupSqliteTest
{
    private const string JobGroup = "manual-firing";
    private const string Reports = "reports";

    private readonly bool shipped;
    private readonly List<IScheduler> schedulers = [];
    private readonly List<ServiceProvider> containers = [];

    private SqliteTestDatabase database = null!;
    private CountingDbProvider provider = null!;

    /// <param name="shipped">
    /// Whether the store runs on the SQLite delegate Quartz ships, or on a subclass of it, which the store
    /// treats as a delegate of somebody's own.
    /// </param>
    public ManualFiringExecutionGroupSqliteTest(bool shipped)
    {
        this.shipped = shipped;
    }

    [SetUp]
    public void CreateDatabase()
    {
        database = new SqliteTestDatabase("manual-firing-group");
        provider = new CountingDbProvider(new DbProvider("SQLite-Microsoft", database.ConnectionString), emulateBatches: false);
    }

    [TearDown]
    public async Task ShutDown()
    {
        foreach (IScheduler scheduler in schedulers)
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }

        foreach (ServiceProvider container in containers)
        {
            await container.DisposeAsync();
        }

        schedulers.Clear();
        containers.Clear();
        database.Dispose();
    }

    /// <summary>
    /// The read #4174 replaces: a manual firing asks the database for the job's group column, not for its
    /// trigger headers, and makes the same statements behind 200 triggers as behind 10.
    /// </summary>
    [Test]
    public async Task AManualFiringReadsTheGroupColumnOfItsJobsTriggersRatherThanTheirHeaders()
    {
        IgnoreUnless(shipped, "the read of the group column is the shipped delegates' own");

        IScheduler scheduler = await Build();
        List<string> few = await ManualFiringStatements(scheduler, storedTriggers: 10);
        List<string> many = await ManualFiringStatements(scheduler, storedTriggers: 200);

        Kinds(many).Should().Equal(Kinds(few),
            "the job's triggers are read in one statement either way, so 190 more of them are more rows, not more statements");
        Kinds(many).Should().ContainSingle(kind => kind == "groups-of-job");
        Kinds(many).Should().NotContain("trigger-headers",
            "every column of every trigger, a fired-trigger probe a row and a sort, to keep one string");
    }

    /// <summary>
    /// <c>TriggerJob</c> with an input resolves the group through the scheduler rather than its store, and so
    /// does the scheduler a container hands out: both reach the same read.
    /// </summary>
    [Test]
    public async Task ATypedManualFiringReadsTheGroupColumnThroughTheSchedulerAndTheContainersHandle()
    {
        IgnoreUnless(shipped, "the read of the group column is the shipped delegates' own");

        IScheduler scheduler = await Build();
        JobKey job = await StoreJob(scheduler, "typed", storedTriggers: 3, Reports);

        provider.Reset();
        await scheduler.TriggerJob(job, "payload");
        List<string> direct = Kinds(Statements());

        IScheduler handle = await BuildInContainer();
        provider.Reset();
        TriggerKey viaHandle = await handle.TriggerJob(job, "payload");
        List<string> deferred = Kinds(Statements());

        direct.Should().ContainSingle(kind => kind == "groups-of-job");
        direct.Should().NotContain("trigger-headers");
        deferred.Should().ContainSingle(kind => kind == "groups-of-job",
            "the container's IScheduler is a handle, and the read it hands on is the scheduler's");
        deferred.Should().NotContain("trigger-headers");
        (await handle.GetTrigger(viaHandle))!.ExecutionGroup.Should().Be(Reports);
    }

    /// <summary>
    /// A delegate of somebody's own is asked for the listing it always was, so an override of
    /// <see cref="IDriverDelegate.SelectTriggerHeaders" /> still runs.
    /// </summary>
    [Test]
    public async Task ADelegateOfSomebodysOwnKeepsTheListingItAlwaysMade()
    {
        IgnoreUnless(!shipped, "a shipped delegate is asked for the group column only");

        IScheduler scheduler = await Build();
        List<string> untyped = Kinds(await ManualFiringStatements(scheduler, storedTriggers: 10));

        JobKey job = new("render-10", JobGroup);
        provider.Reset();
        await scheduler.TriggerJob(job, "payload");
        List<string> typed = Kinds(Statements());

        untyped.Should().ContainSingle(kind => kind == "trigger-headers");
        typed.Should().ContainSingle(kind => kind == "trigger-headers");
        untyped.Concat(typed).Should().NotContain("groups-of-job",
            "the store does not know what a subclass overrides, and the column read would pass its listing by");
    }

    [Test]
    public async Task AManualFiringTakesTheGroupOnlyWhenEveryTriggerOfTheJobCarriesIt()
    {
        IScheduler scheduler = await Build();
        JobKey agreeing = await StoreJob(scheduler, "agreeing", Reports, Reports);
        JobKey mixed = await StoreJob(scheduler, "mixed", Reports, "batch");
        JobKey partly = await StoreJob(scheduler, "partly", Reports, null);
        JobKey cased = await StoreJob(scheduler, "cased", Reports, "Reports");
        JobKey untriggered = await StoreJob(scheduler, "untriggered");

        foreach (JobKey job in new[] { agreeing, mixed, partly, cased, untriggered })
        {
            await scheduler.TriggerJob(job);
        }

        (await GroupOfTheManualFiring(scheduler, agreeing)).Should().Be(Reports,
            "every trigger of the job carries reports, so that is the job's group");
        (await GroupOfTheManualFiring(scheduler, mixed)).Should().BeNull("two groups is no one group");
        (await GroupOfTheManualFiring(scheduler, partly)).Should().BeNull("a grouped and an ungrouped trigger is no one group");
        (await GroupOfTheManualFiring(scheduler, cased)).Should().BeNull(
            "groups compare ordinally, as execution limits count them, whatever the database's collation says");
        (await GroupOfTheManualFiring(scheduler, untriggered)).Should().BeNull("no trigger says anything");
    }

    /// <summary>
    /// A group written to the column by hand rather than through a trigger is taken as the job's trigger
    /// headers report it, so the answer is the one the listing gave.
    /// </summary>
    [Test]
    public async Task AGroupWrittenToTheColumnByHandIsTakenAsTheHeadersReportIt()
    {
        IScheduler scheduler = await Build();
        JobKey padded = await StoreJob(scheduler, "padded", Reports, Reports);
        JobKey blank = await StoreJob(scheduler, "blank", Reports, Reports);
        JobKey empty = await StoreJob(scheduler, "empty", Reports, Reports);
        JobKey spaces = await StoreJob(scheduler, "spaces", Reports, Reports);

        await WriteStoredGroup(padded, 0, " " + Reports);
        await WriteStoredGroup(blank, 1, "   ");
        await WriteStoredGroup(empty, 1, "");
        await WriteStoredGroup(spaces, 0, "  ");
        await WriteStoredGroup(spaces, 1, "  ");

        foreach (JobKey job in new[] { padded, blank, empty, spaces })
        {
            string? listed = ManualFiringExecutionGroup.SharedBy(
                (await scheduler.QueryTriggers(new TriggerQuery { Job = job, Take = PagedQuery.All })).Items);

            (await ManualFiringExecutionGroup.SharedGroupOf(scheduler, job)).Should().Be(listed,
                "the store's answer for {0} is the one the job's trigger headers give", job);
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Helpers
    //////////////////////////////////////////////////////////////////////////////////////////////

    private static void IgnoreUnless(bool applies, string reason)
    {
        if (!applies)
        {
            Assert.Ignore(reason);
        }
    }

    /// <summary>A scheduler on this fixture's database, never started.</summary>
    private async Task<IScheduler> Build()
    {
        IScheduler scheduler = await QuartzSchedulerBuilder.Create(Configure).BuildScheduler();
        schedulers.Add(scheduler);
        return scheduler;
    }

    /// <summary>
    /// The <see cref="IScheduler" /> a container hands out over this fixture's database: a handle that
    /// builds its scheduler on first use.
    /// </summary>
    private async Task<IScheduler> BuildInContainer()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddQuartz(Configure);

        ServiceProvider container = services.BuildServiceProvider();
        containers.Add(container);

        IScheduler handle = container.GetRequiredService<IScheduler>();
        schedulers.Add(await container.GetRequiredService<ISchedulerFactory>().GetScheduler());
        return handle;
    }

    private void Configure(IQuartzBuilder q)
    {
        q.ConfigureScheduler(options =>
        {
            options.InstanceName = "manual-firing-group";
            options.InstanceId = "node";
        });

        q.UsePersistentStore(store =>
        {
            // Before UseSqlite, whose own delegate would otherwise be the one registered first, and win.
            store.UseDriverDelegate(_ => shipped ? new SQLiteDelegate() : new OwnSqliteDelegate());
            store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
            store.UseConnectionProvider(_ => provider);
            store.ProvisionSchema();
        });
    }

    /// <summary>
    /// Stores a job with <paramref name="storedTriggers" /> triggers in <see cref="Reports" />, and returns
    /// the statements of one manual firing of it.
    /// </summary>
    private async Task<List<string>> ManualFiringStatements(IScheduler scheduler, int storedTriggers)
    {
        JobKey job = await StoreJob(scheduler, "render-" + storedTriggers.ToString(CultureInfo.InvariantCulture), storedTriggers, Reports);

        provider.Reset();
        await scheduler.TriggerJob(job);
        List<string> statements = Statements();

        TestContext.Out.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{(shipped ? "shipped" : "own")} delegate, {storedTriggers} stored triggers: {statements.Count} statements: {string.Join(", ", Kinds(statements))}"));

        (await GroupOfTheManualFiring(scheduler, job)).Should().Be(Reports, "the premise: every trigger of the job carries reports");
        return statements;
    }

    private List<string> Statements() => provider.RoundTrips.Select(x => x.Statements[0]).ToList();

    private static Task<JobKey> StoreJob(IScheduler scheduler, string name, int storedTriggers, string? executionGroup)
    {
        return StoreJob(scheduler, name, Enumerable.Repeat(executionGroup, storedTriggers).ToArray());
    }

    /// <summary>
    /// A durable job with a trigger a group, due tomorrow so it never fires: each is here for the group it
    /// carries. Trigger <c>i</c> is named after the job and <c>i</c>.
    /// </summary>
    private static async Task<JobKey> StoreJob(IScheduler scheduler, string name, params string?[] executionGroups)
    {
        IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity(name, JobGroup).StoreDurably().Build();
        if (executionGroups.Length == 0)
        {
            await scheduler.AddJob(job);
            return job.Key;
        }

        DateTimeOffset tomorrow = TimeProvider.System.GetUtcNow().AddDays(1);

        List<ITrigger> triggers = [];
        for (int i = 0; i < executionGroups.Length; i++)
        {
            triggers.Add(TriggerBuilder.Create()
                .WithIdentity(TriggerName(job.Key, i), JobGroup)
                .ForJob(job)
                .WithExecutionGroup(executionGroups[i])
                .StartAt(tomorrow)
                .Build());
        }

        await scheduler.ScheduleJob(job, triggers, ScheduleJobOptions.Replacing);
        return job.Key;
    }

    private static string TriggerName(JobKey job, int index) => job.Name + "-" + index.ToString(CultureInfo.InvariantCulture);

    /// <summary>The execution group of the one manual firing stored for the job, as loading it reads it.</summary>
    private static async Task<string?> GroupOfTheManualFiring(IScheduler scheduler, JobKey job)
    {
        List<ITrigger> triggers = await scheduler.GetTriggersOfJob(job);
        return triggers.Should().ContainSingle(
                trigger => trigger.JobDataMap.ContainsKey(SchedulerConstants.ManualTrigger),
                "one manual firing of the job was stored")
            .Subject.ExecutionGroup;
    }

    /// <summary>Writes <paramref name="executionGroup" /> to a trigger's row as it is, past the trigger's own normalization.</summary>
    private async Task WriteStoredGroup(JobKey job, int index, string executionGroup)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE QRTZ_TRIGGERS SET EXECUTION_GROUP = @group WHERE TRIGGER_NAME = @name AND TRIGGER_GROUP = @triggerGroup";
        command.Parameters.AddWithValue("@group", executionGroup);
        command.Parameters.AddWithValue("@name", TriggerName(job, index));
        command.Parameters.AddWithValue("@triggerGroup", JobGroup);
        (await command.ExecuteNonQueryAsync()).Should().Be(1, "the premise: the trigger's row is there to write to");
    }

    private static List<string> Kinds(List<string> statements) => statements.Select(Kind).ToList();

    /// <summary>Names the two reads under test by the constant each is built from, and the rest by their text.</summary>
    private static string Kind(string sql)
    {
        if (sql == Sql(StdAdoConstants.SqlSelectExecutionGroupsOfJob))
        {
            return "groups-of-job";
        }

        if (sql.StartsWith(Sql(StdAdoConstants.SqlSelectTriggerHeaders), StringComparison.Ordinal))
        {
            return "trigger-headers";
        }

        return "other:" + string.Join(' ', sql.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string Sql(string constant) => AdoJobStoreUtil.ReplaceTablePrefixCached(constant, AdoConstants.DefaultTablePrefix);

    /// <summary>
    /// The shipped SQLite delegate as a type of somebody's own, which the store does not trust to answer
    /// the statements only Quartz's own delegates carry.
    /// </summary>
    public sealed class OwnSqliteDelegate : SQLiteDelegate;

    /// <summary>Never executed: the scheduler is never started.</summary>
    public sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
