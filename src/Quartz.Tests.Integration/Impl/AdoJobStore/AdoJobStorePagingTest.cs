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
using Microsoft.Extensions.DependencyInjection.Extensions;

using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Jobs;
using Quartz.Util;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The paged listing and batch read API against every supported database, because paging is where the
/// dialects disagree: the ANSI OFFSET/FETCH clause, MySQL's LIMIT/OFFSET, and the ordering that makes a
/// page deterministic in the first place are all per-dialect concerns.
/// </summary>
/// <remarks>
/// One of the seeded groups is named with an underscore on purpose. '_' is the single-character LIKE
/// wildcard, so a group matcher that does not escape the pattern it is given would match all three
/// groups instead of the one asked for.
/// </remarks>
[NonParallelizable]
public class AdoJobStorePagingTest
{
    private const string GroupA = "pgA";
    private const string GroupUnderscore = "pg_B";
    private const string GroupC = "pgC";

    private static readonly string[] Groups = [GroupA, GroupUnderscore, GroupC];

    private const int JobCount = 30;
    private const int JobsPerGroup = JobCount / 3;

    private readonly List<IScheduler> createdSchedulers = [];

    [Test]
    [Category("db-sqlserver")]
    public Task TestSqlServer()
    {
        return RunPagingTest(TestConstants.DefaultSqlServerProvider, TestConstants.SqlServerConnectionString, typeof(SqlServerDelegate));
    }

    [Test]
    [Category("db-postgres")]
    public Task TestPostgreSql()
    {
        return RunPagingTest("Npgsql", TestConstants.PostgresConnectionString, typeof(PostgreSQLDelegate));
    }

    [Test]
    [Category("db-mysql")]
    public Task TestMySql()
    {
        string connectionString = Environment.GetEnvironmentVariable("MYSQL_CONNECTION_STRING")
                                  ?? "Server = localhost; Database = quartznet; Uid = quartznet; Pwd = quartznet";
        return RunPagingTest("MySqlConnector", connectionString, typeof(MySQLDelegate));
    }

    [Test]
    [Category("db-firebird")]
    public Task TestFirebird()
    {
        string connectionString = Environment.GetEnvironmentVariable("FIREBIRD_CONNECTION_STRING")
                                  ?? "User=SYSDBA;Password=masterkey;Database=/firebird/data/quartz.fdb;DataSource=localhost;Port=3050;Dialect=3;Charset=NONE;Role=;Connection lifetime=15;Pooling=true;MinPoolSize=0;MaxPoolSize=50;Packet Size=8192;ServerType=0;";
        return RunPagingTest("Firebird", connectionString, typeof(FirebirdDelegate));
    }

    [Test]
    [Category("db-oracle")]
    public Task TestOracle()
    {
        string connectionString = Environment.GetEnvironmentVariable("ORACLE_CONNECTION_STRING")
                                  ?? "Data Source=(DESCRIPTION=(ADDRESS_LIST=(ADDRESS=(PROTOCOL=TCP)(HOST=localhost)(PORT=1521)))(CONNECT_DATA=(SERVICE_NAME=xe)));User Id=system;Password=oracle;";
        return RunPagingTest("OracleODPManaged", connectionString, typeof(OracleDelegate));
    }

    [Test]
    [Category("db-sqlite")]
    public async Task TestSQLiteMicrosoft()
    {
        string dbFileName = "test-paging-sqlite-ms.db";
        if (File.Exists(dbFileName))
        {
            SqliteConnection.ClearAllPools();
            File.Delete(dbFileName);
        }

        string connectionString = $"Data Source={dbFileName};";

        await using (SqliteConnection connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync();
            await using SqliteCommand command = new SqliteCommand(LoadSqliteTableScript(), connection);
            await command.ExecuteNonQueryAsync();
        }

        await RunPagingTest("SQLite-Microsoft", connectionString, typeof(SQLiteDelegate));
    }

    private static string LoadSqliteTableScript()
    {
        string path = File.Exists("../../../../database/tables/tables_sqlite.sql")
            ? "../../../../database/tables/tables_sqlite.sql"
            : "../../../../../database/tables/tables_sqlite.sql";

        return File.ReadAllText(path);
    }

    private async Task RunPagingTest(string dbProvider, string connectionString, Type driverDelegateType)
    {
        IScheduler scheduler = await CreateScheduler(dbProvider, connectionString, driverDelegateType);

        // The scheduler is never started: paging asserts on stored rows, and a running scheduler would
        // move trigger states underneath the assertions.
        await scheduler.Clear();
        await Seed(scheduler);

        try
        {
            await AssertJobPaging(scheduler);
            await AssertJobGroupMatching(scheduler);
            await AssertTriggerPaging(scheduler);
            await AssertTriggerStateFiltering(scheduler);
            await AssertBatchReadRoundTrip(scheduler);
            await AssertJobGroupPauseState(scheduler);
        }
        finally
        {
            await scheduler.Clear();
        }
    }

    private async Task<IScheduler> CreateScheduler(string dbProvider, string connectionString, Type driverDelegateType)
    {
        string suffix = $"{dbProvider}_{Guid.NewGuid():N}".Replace('-', '_');

        QuartzSchedulerBuilder config = QuartzSchedulerBuilder.Create();
        config.ConfigureScheduler(o =>
        {
            o.InstanceId = $"paging_instance_{suffix}";
            o.InstanceName = $"PagingTestScheduler_{dbProvider}".Replace('-', '_');
        });

        config.UsePersistentStore(store =>
        {
            store.ConfigureStore(o =>
            {
                o.StoreJobDataAsStrings = false;
                o.PerformSchemaValidation = true;
            });

            store.UseGenericDatabase(dbProvider, connectionString);
            store.Services.Replace(ServiceDescriptor.Singleton(typeof(IDriverDelegate), driverDelegateType));
            store.UseSystemTextJsonSerializer();
        });

        IScheduler scheduler = await config.BuildScheduler();
        createdSchedulers.Add(scheduler);
        return scheduler;
    }

    /// <summary>
    /// Seeds <see cref="JobCount" /> jobs, each with one trigger of the same name and group, spread
    /// evenly over the three groups. Names are zero padded so the stored ordinal ordering is the one
    /// the assertions expect.
    /// </summary>
    private static async Task Seed(IScheduler scheduler)
    {
        for (int i = 0; i < JobCount; i++)
        {
            string group = Groups[i % Groups.Length];
            string name = Name(i);

            IJobDetail job = JobBuilder.Create<NoOpJob>()
                .WithIdentity(name, group)
                .WithDescription("seeded " + name)
                .Build();

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity(name, group)
                .ForJob(job)
                // Far enough out that nothing fires and nothing misfires while the test runs.
                .StartAt(DateTimeOffset.UtcNow.AddYears(1))
                .Build();

            await scheduler.ScheduleJob(job, trigger);
        }
    }

    private static string Name(int index) => $"item-{index:00}";

    /// <summary>
    /// Asserts the ordering a listing has to produce, without pinning it to one collation: which group
    /// sorts before which is the database's business, but each group's rows have to be contiguous and
    /// the names within a group have to ascend. The names are zero padded digits, which every collation
    /// orders the same way.
    /// </summary>
    private static void AssertGroupedAndOrdered(IReadOnlyList<(string Group, string Name)> items)
    {
        items.Should().HaveCount(JobCount);
        items.Select(x => x.Group).Distinct().Should().HaveCount(Groups.Length);

        List<string> groupRuns = [];
        for (int i = 0; i < items.Count; i++)
        {
            if (i == 0 || items[i].Group != items[i - 1].Group)
            {
                groupRuns.Add(items[i].Group);
            }
            else
            {
                string.CompareOrdinal(items[i].Name, items[i - 1].Name).Should().BePositive(
                    "names ascend within a group, but '{0}' came after '{1}'", items[i].Name, items[i - 1].Name);
            }
        }

        groupRuns.Should().OnlyHaveUniqueItems("every group's rows must be contiguous, not interleaved");
    }

    private static async Task AssertJobPaging(IScheduler scheduler)
    {
        PagedResult<JobHeader> everything = await scheduler.QueryJobs(new JobQuery());
        List<(string Group, string Name)> expected = everything.Items.Select(x => (x.Key.Group, x.Key.Name)).ToList();
        AssertGroupedAndOrdered(expected);

        PagedResult<JobHeader> first = await scheduler.QueryJobs(new JobQuery { Take = 12, IncludeTotalCount = true });
        first.Items.Should().HaveCount(12);
        first.HasMore.Should().BeTrue("30 jobs do not fit in a page of 12");
        first.TotalCount.Should().Be(JobCount, "the total ignores paging");

        PagedResult<JobHeader> second = await scheduler.QueryJobs(new JobQuery { Skip = 12, Take = 12, IncludeTotalCount = true });
        second.Items.Should().HaveCount(12);
        second.HasMore.Should().BeTrue();
        second.TotalCount.Should().Be(JobCount);

        PagedResult<JobHeader> third = await scheduler.QueryJobs(new JobQuery { Skip = 24, Take = 12, IncludeTotalCount = true });
        third.Items.Should().HaveCount(6, "the last page holds only the remainder");
        third.HasMore.Should().BeFalse("nothing follows the last page");
        third.TotalCount.Should().Be(JobCount);

        List<(string Group, string Name)> paged =
        [
            .. first.Items.Select(x => (x.Key.Group, x.Key.Name)),
            .. second.Items.Select(x => (x.Key.Group, x.Key.Name)),
            .. third.Items.Select(x => (x.Key.Group, x.Key.Name)),
        ];

        paged.Should().Equal(expected, "the pages must tile the whole ordered result with no gap and no overlap");

        PagedResult<JobHeader> exactPage = await scheduler.QueryJobs(new JobQuery { Skip = 20, Take = 10 });
        exactPage.Items.Should().HaveCount(10);
        exactPage.HasMore.Should().BeFalse("a page that ends exactly on the last row has nothing after it");

        PagedResult<JobHeader> pastEnd = await scheduler.QueryJobs(new JobQuery { Skip = JobCount, Take = 5, IncludeTotalCount = true });
        pastEnd.Items.Should().BeEmpty();
        pastEnd.HasMore.Should().BeFalse();
        pastEnd.TotalCount.Should().Be(JobCount);

        PagedResult<JobHeader> countOnly = await scheduler.QueryJobs(new JobQuery { Take = 0, IncludeTotalCount = true });
        countOnly.Items.Should().BeEmpty("Take = 0 turns the query into a count");
        countOnly.TotalCount.Should().Be(JobCount);

        everything.HasMore.Should().BeFalse("an unpaged query returns everything");
        everything.TotalCount.Should().BeNull("the total is only computed when it is asked for");

        JobHeader header = everything.Items.First(x => x.Key.Name == Name(0));
        header.Description.Should().Be("seeded " + Name(0));
        header.JobTypeName.Should().Contain(nameof(NoOpJob), "the listing carries the recorded type name");
    }

    private static async Task AssertJobGroupMatching(IScheduler scheduler)
    {
        PagedResult<JobHeader> exact = await scheduler.QueryJobs(new JobQuery
        {
            Group = GroupMatcher<JobKey>.GroupEquals(GroupUnderscore),
            IncludeTotalCount = true
        });

        exact.Items.Should().HaveCount(JobsPerGroup);
        exact.TotalCount.Should().Be(JobsPerGroup);
        exact.Items.Should().OnlyContain(x => x.Key.Group == GroupUnderscore);

        PagedResult<JobHeader> startsWithUnderscore = await scheduler.QueryJobs(new JobQuery
        {
            Group = GroupMatcher<JobKey>.GroupStartsWith("pg_"),
            IncludeTotalCount = true
        });

        startsWithUnderscore.TotalCount.Should().Be(JobsPerGroup,
            "'_' in the matcher's own text is a literal, so only the group actually named '{0}' matches", GroupUnderscore);
        startsWithUnderscore.Items.Should().OnlyContain(x => x.Key.Group == GroupUnderscore);

        PagedResult<JobHeader> startsWithPrefix = await scheduler.QueryJobs(new JobQuery
        {
            Group = GroupMatcher<JobKey>.GroupStartsWith("pg"),
            IncludeTotalCount = true
        });

        startsWithPrefix.TotalCount.Should().Be(JobCount, "every seeded group starts with 'pg'");

        PagedResult<JobHeader> pagedGroup = await scheduler.QueryJobs(new JobQuery
        {
            Group = GroupMatcher<JobKey>.GroupEquals(GroupA),
            Skip = 4,
            Take = 4,
            IncludeTotalCount = true
        });

        pagedGroup.Items.Should().HaveCount(4);
        pagedGroup.HasMore.Should().BeTrue();
        pagedGroup.TotalCount.Should().Be(JobsPerGroup, "the total counts the matching rows, not the page");
    }

    private static async Task AssertTriggerPaging(IScheduler scheduler)
    {
        PagedResult<TriggerHeader> everything = await scheduler.QueryTriggers(new TriggerQuery());
        List<(string Group, string Name)> expected = everything.Items.Select(x => (x.Key.Group, x.Key.Name)).ToList();
        AssertGroupedAndOrdered(expected);

        PagedResult<TriggerHeader> first = await scheduler.QueryTriggers(new TriggerQuery { Take = 12, IncludeTotalCount = true });
        PagedResult<TriggerHeader> second = await scheduler.QueryTriggers(new TriggerQuery { Skip = 12, Take = 12 });
        PagedResult<TriggerHeader> third = await scheduler.QueryTriggers(new TriggerQuery { Skip = 24, Take = 12 });

        first.TotalCount.Should().Be(JobCount);
        first.HasMore.Should().BeTrue();
        third.HasMore.Should().BeFalse();

        List<(string Group, string Name)> paged =
        [
            .. first.Items.Select(x => (x.Key.Group, x.Key.Name)),
            .. second.Items.Select(x => (x.Key.Group, x.Key.Name)),
            .. third.Items.Select(x => (x.Key.Group, x.Key.Name)),
        ];

        paged.Should().Equal(expected);

        PagedResult<TriggerHeader> byGroup = await scheduler.QueryTriggers(new TriggerQuery
        {
            Group = GroupMatcher<TriggerKey>.GroupEquals(GroupUnderscore),
            IncludeTotalCount = true
        });

        byGroup.TotalCount.Should().Be(JobsPerGroup);
        byGroup.Items.Should().OnlyContain(x => x.Key.Group == GroupUnderscore);

        PagedResult<TriggerHeader> byJob = await scheduler.QueryTriggers(new TriggerQuery
        {
            Job = new JobKey(Name(0), GroupA),
            IncludeTotalCount = true
        });

        byJob.TotalCount.Should().Be(1);
        byJob.Items.Single().Key.Should().Be(new TriggerKey(Name(0), GroupA));

        TriggerHeader header = byJob.Items.Single();
        header.JobKey.Should().Be(new JobKey(Name(0), GroupA));
        header.State.Should().Be(TriggerState.Normal);
        header.NextFireTimeUtc.Should().NotBeNull("a scheduled trigger has a next fire time");
    }

    private static async Task AssertTriggerStateFiltering(IScheduler scheduler)
    {
        await scheduler.PauseTriggers(GroupMatcher<TriggerKey>.GroupEquals(GroupC));

        try
        {
            PagedResult<TriggerHeader> paused = await scheduler.QueryTriggers(new TriggerQuery
            {
                State = TriggerState.Paused,
                IncludeTotalCount = true
            });

            paused.TotalCount.Should().Be(JobsPerGroup, "only the paused group's triggers are paused");
            paused.Items.Should().OnlyContain(x => x.Key.Group == GroupC);
            paused.Items.Should().OnlyContain(x => x.State == TriggerState.Paused);

            PagedResult<TriggerHeader> normal = await scheduler.QueryTriggers(new TriggerQuery
            {
                State = TriggerState.Normal,
                IncludeTotalCount = true
            });

            normal.TotalCount.Should().Be(JobCount - JobsPerGroup);
            normal.Items.Should().NotContain(x => x.Key.Group == GroupC);

            PagedResult<TriggerHeader> pausedAndGrouped = await scheduler.QueryTriggers(new TriggerQuery
            {
                State = TriggerState.Paused,
                Group = GroupMatcher<TriggerKey>.GroupEquals(GroupA),
                IncludeTotalCount = true
            });

            pausedAndGrouped.TotalCount.Should().Be(0, "the filters combine with AND");

            PagedResult<TriggerHeader> pausedPage = await scheduler.QueryTriggers(new TriggerQuery
            {
                State = TriggerState.Paused,
                Skip = 6,
                Take = 6,
                IncludeTotalCount = true
            });

            pausedPage.Items.Should().HaveCount(4);
            pausedPage.HasMore.Should().BeFalse();
            pausedPage.TotalCount.Should().Be(JobsPerGroup, "paging must not change the filtered total");
        }
        finally
        {
            await scheduler.ResumeTriggers(GroupMatcher<TriggerKey>.GroupEquals(GroupC));
        }
    }

    private static async Task AssertBatchReadRoundTrip(IScheduler scheduler)
    {
        List<JobKey> jobKeys = [];
        List<TriggerKey> triggerKeys = [];
        for (int i = 0; i < JobCount; i++)
        {
            string group = Groups[i % Groups.Length];
            jobKeys.Add(new JobKey(Name(i), group));
            triggerKeys.Add(new TriggerKey(Name(i), group));
        }

        List<IJobDetail> jobs = await scheduler.GetJobDetails(jobKeys);
        jobs.Select(x => x.Key).Should().Equal(jobKeys, "a batch read comes back in the order it was asked in");
        jobs.Should().OnlyContain(x => x.Description.StartsWith("seeded ", StringComparison.Ordinal));

        List<ITrigger> triggers = await scheduler.GetTriggers(triggerKeys);
        triggers.Select(x => x.Key).Should().Equal(triggerKeys);
        triggers.Should().OnlyContain(x => x.NextFireTimeUtc != null);

        // A key that was never stored is simply absent rather than an error or a null hole.
        List<JobKey> withMissing = [jobKeys[3], new JobKey("no-such-job", GroupA), jobKeys[1]];
        List<IJobDetail> partial = await scheduler.GetJobDetails(withMissing);
        partial.Select(x => x.Key).Should().Equal([jobKeys[3], jobKeys[1]]);

        List<TriggerKey> triggersWithMissing = [triggerKeys[5], new TriggerKey("no-such-trigger", GroupC), triggerKeys[2]];
        List<ITrigger> partialTriggers = await scheduler.GetTriggers(triggersWithMissing);
        partialTriggers.Select(x => x.Key).Should().Equal([triggerKeys[5], triggerKeys[2]]);

        (await scheduler.GetJobDetails([])).Should().BeEmpty();
        (await scheduler.GetTriggers([])).Should().BeEmpty();
    }

    /// <summary>
    /// The job group listing's three shapes against a real database.
    /// </summary>
    /// <remarks>
    /// The paused variants are the ones worth running per dialect: the unfiltered and unpaused
    /// statements carry a correlated subquery over PAUSED_JOB_GRPS that binds the scheduler name once
    /// and matches the outer SCHED_NAME column for the rest, which is exactly the shape a provider
    /// with positional parameter binding gets wrong. The paused-only statement reads the other table
    /// entirely, so nothing but a real round trip proves the two agree.
    /// </remarks>
    private static async Task AssertJobGroupPauseState(IScheduler scheduler)
    {
        PagedResult<JobGroup> before = await scheduler.QueryJobGroups(new JobGroupQuery { IncludeTotalCount = true });
        before.Items.Select(x => x.Name).Should().BeEquivalentTo(Groups);
        before.Items.Should().OnlyContain(x => !x.Paused, "nothing has been paused yet");
        before.TotalCount.Should().Be(Groups.Length);

        await scheduler.PauseJobs(GroupMatcher<JobKey>.GroupEquals(GroupUnderscore));

        try
        {
            PagedResult<JobGroup> paused = await scheduler.QueryJobGroups(new JobGroupQuery { Paused = true, IncludeTotalCount = true });
            paused.Items.Select(x => x.Name).Should().Equal([GroupUnderscore],
                "the pause is recorded for the group that was named, and only that group");
            paused.TotalCount.Should().Be(1, "the count matches the listing it counts");

            PagedResult<JobGroup> unpaused = await scheduler.QueryJobGroups(new JobGroupQuery { Paused = false, IncludeTotalCount = true });
            unpaused.Items.Select(x => x.Name).Should().BeEquivalentTo([GroupA, GroupC],
                "the unpaused listing is the complement of the paused one");
            unpaused.TotalCount.Should().Be(2);

            PagedResult<JobGroup> named = await scheduler.QueryJobGroups(new JobGroupQuery { Name = GroupUnderscore, Take = 1 });
            named.Items.Should().ContainSingle().Which.Paused.Should().BeTrue(
                "the unfiltered listing reports each group's own state, and '_' in the name is a literal");

            PagedResult<JobGroup> countOnly = await scheduler.QueryJobGroups(new JobGroupQuery { Paused = true, Take = 0, IncludeTotalCount = true });
            countOnly.Items.Should().BeEmpty();
            countOnly.TotalCount.Should().Be(1, "a count-only query answers the same total as the listing");
        }
        finally
        {
            await scheduler.ResumeJobs(GroupMatcher<JobKey>.GroupEquals(GroupUnderscore));
        }

        (await scheduler.QueryJobGroups(new JobGroupQuery { Paused = true })).Items.Should().BeEmpty(
            "resuming the group takes its row back out");
    }

    [TearDown]
    public async Task ShutdownSchedulers()
    {
        foreach (IScheduler scheduler in createdSchedulers)
        {
            await scheduler.Shutdown();
        }

        createdSchedulers.Clear();
    }
}
