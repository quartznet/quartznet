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

using AwesomeAssertions.Execution;

using FakeItEasy;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

using Quartz.Configuration;
using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// Discovering the schedulers in a database, and the windows opened onto them.
/// </summary>
/// <remarks>
/// A file-backed SQLite database rather than a container, because everything here is about SQL a
/// dialect-neutral statement issues and about what the listing says afterwards. The dashboard's own
/// end-to-end case is <c>StoreAttachedTargetTest</c>.
/// </remarks>
public sealed class AttachedStoreTest
{
    private SqliteTestDatabase database = null!;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        database = new SqliteTestDatabase("attached-store");
    }

    [TearDown]
    public void DeleteDatabase()
    {
        database.Dispose();
    }

    [Test]
    public async Task DiscoveryFindsEverySchedulerNameTheDatabaseHolds()
    {
        await using ServiceProvider alpha = await Node("alpha", schedule: true);
        await using ServiceProvider beta = await Node("beta", schedule: false);

        await using ServiceProvider application = Application();
        await using AttachedStore store = new("prod", Recipe, application);

        List<string> names = await store.Discover();

        names.Should().Equal(["alpha", "beta"],
            "a scheduler leaves a trace in three tables and no one of them is enough - 'beta' scheduled "
            + "nothing at all and is still a scheduler this database holds");
    }

    [Test]
    public async Task ADiscoveredSchedulerBecomesAWindowInTheListing()
    {
        await using ServiceProvider alpha = await Node("alpha", schedule: true);

        await using ServiceProvider application = Application();
        await using AttachedStore store = new("prod", Recipe, application);

        List<string> added = await store.Synchronize();
        added.Should().Equal(["alpha"]);

        List<SchedulerRegistration> listing = await application.GetRequiredService<ISchedulerRegistry>().QuerySchedulers();

        SchedulerRegistration window = listing.Should().ContainSingle(x => x.Name == "alpha").Subject;
        window.Origin.Should().Be(SchedulerOrigin.Window,
            "it was discovered in a database rather than registered, and nothing here runs it");
        window.Target.Should().Be("prod", "the target says which database the name was found in");
        window.SchedulerInstanceId.Should().BeNull(
            "a window is not a node, and the id its store carries is one this process invented - reporting "
            + "it would put a node in the listing that does not exist");

        (await store.Synchronize()).Should().BeEmpty(
            "a second round costs one query and opens nothing: a name that already has a window is left alone");
    }

    [Test]
    public async Task AnObserverIsNeverOneOfTheNodesItReads()
    {
        await using ServiceProvider alpha = await Node("alpha", schedule: true);

        await using ServiceProvider application = Application();
        await using AttachedStore store = new("prod", Recipe, application);
        await store.Synchronize();

        IScheduler window = application.GetRequiredService<Extensibility.ISchedulerRepository>().Lookup("alpha")!;

        (await window.QueryClusterNodes()).Should().BeEmpty(
            "nothing has checked in under this scheduler's name, and an observer must not answer with "
            + "itself - it writes no check-in row and can run nothing");

        window.Status.Should().Be(SchedulerStatus.Created,
            "a window is built and never started, which is what makes deriving its reported status from "
            + "the cluster's check-ins necessary rather than a refinement");
    }

    /// <summary>
    /// Starting a window is refused by its store, before recovery has touched a single row.
    /// </summary>
    /// <remarks>
    /// The reviewer's evidence for the defect, kept as its regression test. A start used to run the
    /// cluster's start-up as though this process were one of its nodes: recovery put the node's
    /// <c>ACQUIRED</c> trigger back to <c>WAITING</c> and deleted its fired-trigger row, so a firing a live
    /// node was in the middle of could be run a second time. Anything that reaches the scheduler object —
    /// the HTTP API, <see cref="Extensibility.ISchedulerRepository" />, an application's own code — could
    /// do it, which is why the refusal is the store's rather than a page's.
    /// </remarks>
    [Test]
    public async Task StartingAWindowIsRefusedAndLeavesTheNodesFiringAlone()
    {
        await using ServiceProvider alpha = await Node("alpha", schedule: true);

        IJobStore nodeStore = alpha.GetRequiredService<IJobStore>();
        await nodeStore.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = DateTimeOffset.UtcNow.AddYears(2),
            MaxCount = 1,
            TimeWindow = TimeSpan.FromDays(1)
        });

        (await FiredRowCount()).Should().Be(1, "the node has acquired its trigger and is about to fire it");
        (await TriggerState()).Should().Be("ACQUIRED");

        await using ServiceProvider application = Application();
        await using AttachedStore store = new("prod", Recipe, application);
        await store.Synchronize();

        IScheduler window = application.GetRequiredService<Extensibility.ISchedulerRepository>().Lookup("alpha")!;

        Func<Task> start = async () => await window.Start();

        await start.Should().ThrowAsync<SchedulerException>()
            .WithMessage("*'alpha'*window*",
                "the refusal names the scheduler and says what it is, so whoever pressed start learns why "
                + "nothing happened");

        await start.Should().ThrowAsync<SchedulerException>(
            "a refused start leaves the scheduler as it was built, so asking again is refused again rather "
            + "than sliding down the resume path that skips the store's start-up hook");

        using AssertionScope scope = new();
        window.Status.Should().Be(SchedulerStatus.Created, "a window is never started");
        (await TriggerState()).Should().Be("ACQUIRED",
            "recovery would have put the node's trigger back to WAITING for this process to acquire");
        (await FiredRowCount()).Should().Be(1,
            "recovery would have deleted the node's fired-trigger row, which is what lets a firing run twice");
    }

    [Test]
    public async Task AWindowIsNotAHealthCheckTarget()
    {
        await using ServiceProvider alpha = await Node("alpha", schedule: true);

        ServiceCollection services = new();
        services.AddLogging();
        services.AddQuartz("core", q => q.UseInMemoryStore());
        services.AddHealthChecks().AddQuartz("alpha");

        await using ServiceProvider application = services.BuildServiceProvider();
        await using AttachedStore store = new("prod", Recipe, application);
        await store.Synchronize();

        HealthReport report = await application.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(registration => registration.Name == "quartz-scheduler-alpha");

        HealthReportEntry entry = report.Entries["quartz-scheduler-alpha"];
        entry.Status.Should().Be(HealthStatus.Unhealthy);
        entry.Description.Should().Contain("window").And.Contain("prod",
            "a window is in the repository like every other scheduler, so the check would otherwise report "
            + "'created but never started' for a cluster that is running perfectly well elsewhere");
    }

    /// <summary>
    /// A name this process already uses is refused for good, naming both, and the rest of the database
    /// is still shown.
    /// </summary>
    [Test]
    public async Task ANameThisProcessAlreadyUsesIsRefusedForGood()
    {
        await using ServiceProvider alpha = await Node("alpha", schedule: true);
        await using ServiceProvider beta = await Node("beta", schedule: false);

        ServiceCollection services = new();
        services.AddLogging();
        services.AddQuartz("alpha", q => q.UseInMemoryStore());

        await using ServiceProvider application = services.BuildServiceProvider();
        await using AttachedStore store = new("prod", Recipe, application);

        (await store.Synchronize()).Should().Equal(["beta"]);

        store.Refusals.Should().ContainKey("alpha")
            .WhoseValue.Should().Contain("prod/alpha").And.Contain("registered with the container");

        (await store.Synchronize()).Should().BeEmpty(
            "a taken name stays taken, so it is decided once rather than tried and logged every round");
    }

    /// <summary>
    /// A window that fails to build for any reason but a taken name is tried again next round, and does
    /// not cost the round the names after it.
    /// </summary>
    /// <remarks>
    /// <see cref="ISchedulerRuntime.Add" /> reports a taken name and a recipe that would not build as the
    /// same <see cref="SchedulerConfigException" />, so a validation failure used to be recorded as a
    /// permanent "two schedulers under one name" refusal — and anything that was not a
    /// <see cref="SchedulerConfigException" /> escaped the round and skipped every name after it. Both
    /// failures are raised here by the runtime the store builds through, which lets the case say exactly
    /// which name fails and when.
    /// </remarks>
    [TestCase(typeof(SchedulerConfigException))]
    [TestCase(typeof(InvalidOperationException))]
    public async Task AWindowThatFailsToOpenIsTriedAgainAndTheRoundGoesOn(Type failure)
    {
        await using ServiceProvider alpha = await Node("alpha", schedule: true);
        await using ServiceProvider beta = await Node("beta", schedule: false);

        ServiceCollection services = new();
        services.AddLogging();
        services.AddQuartz("core", q => q.UseInMemoryStore());
        services.AddSingleton<ISchedulerRuntime>(provider =>
        {
            ISchedulerRuntime runtime = A.Fake<ISchedulerRuntime>(options => options.Wrapping(
                provider.GetRequiredService<SchedulerRuntime>()));

            A.CallTo(() => runtime.Add("alpha", A<Action<IQuartzBuilder>>._, A<SchedulerAddOptions>._, A<CancellationToken>._))
                .Throws(() => (Exception) Activator.CreateInstance(failure, "alpha's options did not validate this time")!)
                .Once();

            return runtime;
        });

        await using ServiceProvider application = services.BuildServiceProvider();
        await using AttachedStore store = new("prod", Recipe, application);

        (await store.Synchronize()).Should().Equal(["beta"],
            "one name that could not be opened this round does not cost the operator the rest of the database");

        store.Refusals.Should().BeEmpty(
            "nothing about the failure says the name is taken, and recording it as a collision would hide "
            + "the scheduler for the life of the process");

        (await store.Synchronize()).Should().Equal(["alpha"],
            "a window that failed to build is tried again, and opens once whatever stopped it has passed");
    }

    [Test]
    public void ADelegateThatCannotDiscoverSchedulerNamesSaysSo()
    {
        IDriverDelegate driverDelegate = A.Fake<IDriverDelegate>();
        A.CallTo(() => driverDelegate.SelectSchedulerNames(A<ConnectionAndTransactionHolder>._, A<CancellationToken>._))
            .CallsBaseMethod();

        Func<Task> discover = async () => await driverDelegate.SelectSchedulerNames(null!);

        discover.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*StdAdoDelegate*",
                "every other member of the interface is scoped to one scheduler's rows, so there is no "
                + "cross-scheduler statement for a default body to borrow - and answering with nothing would "
                + "show an empty dashboard for a database full of schedulers");
    }

    [Test]
    public async Task ASecondStoreUnderOneNameIsRefused()
    {
        await using ServiceProvider application = Application();
        await using AttachedStores stores = new(application.GetRequiredService<SchedulerWindowRegistry>());

        stores.Add(new AttachedStore("prod", Recipe, application));

        Action second = () => stores.Add(new AttachedStore("PROD", Recipe, application));

        second.Should().Throw<SchedulerConfigException>()
            .WithMessage("*prod*",
                "the target's name is half of every window's identity, so two databases under one name "
                + "would give two schedulers one spelling");
    }

    /// <summary>
    /// A scheduler of another process, which is what leaves rows in the shared database.
    /// </summary>
    private async ValueTask<ServiceProvider> Node(string schedulerName, bool schedule)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddQuartz(quartz =>
        {
            quartz.ConfigureScheduler(options => options.InstanceName = schedulerName);
            quartz.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                store.ProvisionSchema();
            });
        });

        ServiceProvider provider = services.BuildServiceProvider();
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        if (schedule)
        {
            await scheduler.ScheduleJob(
                JobBuilder.Create<NoOpJob>().WithIdentity("nightly", "batch").StoreDurably().Build(),
                TriggerBuilder.Create()
                    .WithIdentity("at-midnight", "nightly")
                    .StartAt(DateTimeOffset.UtcNow.AddYears(1))
                    .Build());
        }
        else
        {
            // A durable job and no trigger, which is the case one table alone would miss.
            await scheduler.AddJob(
                JobBuilder.Create<NoOpJob>().WithIdentity("on-demand", "batch").StoreDurably().Build());
        }

        return provider;
    }

    /// <summary>The container the windows are added to: a scheduler of its own and nothing else.</summary>
    private static ServiceProvider Application()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddQuartz("core", q => q.UseInMemoryStore());
        return services.BuildServiceProvider();
    }

    private void Recipe(IPersistentStoreBuilder store)
    {
        store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
    }

    private async Task<long> FiredRowCount()
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM QRTZ_FIRED_TRIGGERS";

        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private async Task<string> TriggerState()
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT TRIGGER_STATE FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = 'at-midnight'";

        return (string) (await command.ExecuteScalarAsync())!;
    }

    private sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }
}
