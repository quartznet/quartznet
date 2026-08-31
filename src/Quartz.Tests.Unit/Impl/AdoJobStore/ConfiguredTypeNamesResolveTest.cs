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

using System.Collections.Specialized;
using System.Reflection;
using System.Runtime.CompilerServices;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Documentation.Samples.HowTos;
using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// Two type names a persistent configuration spells out by hand, and the promise that both still
/// resolve now that the store hierarchy has left the public contract.
/// </summary>
/// <remarks>
/// <para>
/// <c>quartz.jobStore.type</c> and <c>quartz.jobStore.driverDelegateType</c> are strings in a file, and
/// a string does not care whether the type it names is public. That is the whole point of internalizing
/// the store: the *configuration* keeps working while the *type* leaves the contract. The two facts are
/// easy to break independently — a rename, a `SimpleTypeLoader` that stops passing
/// <c>BindingFlags.NonPublic</c>, an `ActivatorUtilities` call that starts requiring a public type — and
/// nothing else in the suite notices, because every other test names these types in code.
/// </para>
/// <para>
/// The delegate half runs against a real file-backed SQLite database rather than stopping at
/// registration, because resolution is only half the promise: the delegate is constructed, initialized
/// with a <see cref="DriverDelegateContext" />, and asked for SQL, and a break anywhere along that path
/// shows up as a scheduler that starts and never fires.
/// </para>
/// </remarks>
public sealed class ConfiguredTypeNamesResolveTest
{
    /// <summary>
    /// The spelling a 3.x configuration file carries, not <c>typeof(...)</c>. A test that computed the
    /// name from the type would keep passing through the rename that broke everybody's file.
    /// </summary>
    private const string LocalTransactionJobStoreName = "Quartz.Impl.AdoJobStore.LocalTransactionJobStore, Quartz";

    private const string ExternalTransactionJobStoreName = "Quartz.Impl.AdoJobStore.ExternalTransactionJobStore, Quartz";

    private string databaseFile = null!;
    private string connectionString = null!;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        databaseFile = Path.Combine(Path.GetTempPath(), $"quartz-delegate-{Guid.NewGuid():N}.db");
        connectionString = $"Data Source={databaseFile}";
    }

    [TearDown]
    public void DeleteDatabase()
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(databaseFile))
        {
            File.Delete(databaseFile);
        }
    }

    [Test]
    public async Task ADelegateNamedByStringRunsTheSchedule()
    {
        CountingSqliteDelegate.AcquisitionSqlAsked = 0;

        ServiceCollection services = new();
        services.AddQuartz(StoreAndDelegateByName(
            nameof(ADelegateNamedByStringRunsTheSchedule),
            typeof(CountingSqliteDelegate).AssemblyQualifiedName!));

        await using ServiceProvider container = services.BuildServiceProvider();

        container.GetRequiredService<IDriverDelegate>().Should().BeOfType<CountingSqliteDelegate>(
            "the delegate a configuration file names is the one the store is built with");

        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();

        TaskCompletionSource fired = new(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.Context[SignallingJob.SignalKey] = fired;

        TriggerKey triggerKey = new("trigger", "delegate-by-name");

        await scheduler.ScheduleJob(
            JobBuilder.Create<SignallingJob>().WithIdentity("job", "delegate-by-name").Build(),
            TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .StartNow()
                // Repeating, so the read-back below reads a trigger rather than finding the row a
                // completed one-shot trigger took with it.
                .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
                .Build());

        await scheduler.Start();
        await fired.Task.WaitAsync(TimeSpan.FromSeconds(30));

        (await scheduler.GetTrigger(triggerKey)).Should().NotBeNull(
            "a schedule the named delegate wrote has to be one it can read back");

        CountingSqliteDelegate.AcquisitionSqlAsked.Should().BeGreaterThan(0,
            "the delegate's own SQL hook is what proves it is driving the store rather than merely "
            + "sitting in the container beside the one that is");

        await scheduler.Shutdown(waitForJobsToComplete: true);
    }

    /// <summary>
    /// The same key, naming a delegate from an assembly that is <b>not</b> a friend of Quartz.
    /// </summary>
    /// <remarks>
    /// <see cref="MyDatabaseDelegate" /> is the documentation's own sample, declared in
    /// <c>Quartz.Documentation.Samples</c>, which has no <c>InternalsVisibleTo</c> grant. So this
    /// asserts what the test above cannot: that a delegate written outside Quartz, against nothing but
    /// the public kit, is a type <c>driverDelegateType</c> can name. It stops at registration, because
    /// the sample speaks ANSI SQL and the database under it here is SQLite.
    /// </remarks>
    [Test]
    public void ADelegateFromAnAssemblyThatIsNotAFriendResolves()
    {
        ServiceCollection services = new();
        services.AddQuartz(StoreAndDelegateByName(
            nameof(ADelegateFromAnAssemblyThatIsNotAFriendResolves),
            typeof(MyDatabaseDelegate).AssemblyQualifiedName!));

        using ServiceProvider container = services.BuildServiceProvider();

        container.GetRequiredService<IDriverDelegate>().Should().BeOfType<MyDatabaseDelegate>();

        typeof(IScheduler).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(grant => grant.AssemblyName.Split(',')[0])
            .Should().NotContain(typeof(MyDatabaseDelegate).Assembly.GetName().Name,
                "the samples project's second job is to be the compile-time proof that the delegate "
                + "kit is complete, and one InternalsVisibleTo grant would end that quietly — the "
                + "samples would go on compiling and would stop meaning anything");
    }

    /// <summary>
    /// The store the configuration string names is internal, and both halves of that matter.
    /// </summary>
    [Test]
    [TestCase(LocalTransactionJobStoreName)]
    [TestCase(ExternalTransactionJobStoreName)]
    public void AShippedStoreStillResolvesByNameAndIsNoLongerPublic(string configuredTypeName)
    {
        Type? resolved = Type.GetType(configuredTypeName);

        resolved.Should().NotBeNull(
            "this is the string in an application's configuration file, and it has to go on naming the "
            + "store it always named — internalizing a type does not rename it");

        resolved!.IsPublic.Should().BeFalse(
            "the point of the exercise is that the store is no longer part of the contract; a type that "
            + "went back to public would put a hundred protected hooks back with it");

        resolved.GetConstructors().Should().NotBeEmpty(
            "ActivatorUtilities needs a public constructor, not a public type — which is exactly why the "
            + "configuration string keeps working");
    }

    [Test]
    public void AStoreNamedByStringIsTheStoreThatIsBuilt()
    {
        ServiceCollection services = new();
        services.AddQuartz(StoreAndDelegateByName(
            nameof(AStoreNamedByStringIsTheStoreThatIsBuilt),
            typeof(SQLiteDelegate).AssemblyQualifiedName!));

        using ServiceProvider container = services.BuildServiceProvider();

        container.GetRequiredService<IJobStore>().GetType().FullName.Should()
            .Be("Quartz.Impl.AdoJobStore.LocalTransactionJobStore",
                "the whole configuration path — type loader, constructor selection, registration — has to "
                + "survive the type going internal, not just Type.GetType");
    }

    /// <summary>
    /// The container-managed store, chosen by the string a configuration file carries and by the
    /// builder member, is one store either way.
    /// </summary>
    /// <remarks>
    /// <c>quartz.jobStore.type</c> was the only route to it while the type was internal and nothing on
    /// <c>IPersistentStoreBuilder</c> selected a store, so <c>UseAmbientTransactions</c> is measured
    /// against it rather than against a name typed here: a selector that reached a different store than
    /// the string always has would split one setting into two that disagree.
    /// </remarks>
    [Test]
    public void TheAmbientTransactionStoreIsOneStoreWhicheverWayItIsChosen()
    {
        NameValueCollection properties = StoreAndDelegateByName(
            nameof(TheAmbientTransactionStoreIsOneStoreWhicheverWayItIsChosen),
            typeof(SQLiteDelegate).AssemblyQualifiedName!);
        properties["quartz.jobStore.type"] = ExternalTransactionJobStoreName;

        ServiceCollection namedByString = new();
        namedByString.AddQuartz(properties);
        using ServiceProvider fromString = namedByString.BuildServiceProvider();

        ServiceCollection chosenInCode = new();
        chosenInCode.AddQuartz(q => q.UsePersistentStore(store =>
        {
            store.UseSqlite(connectionString);
            store.UseAmbientTransactions();
        }));
        using ServiceProvider fromCode = chosenInCode.BuildServiceProvider();

        Type stringStore = fromString.GetRequiredService<IJobStore>().GetType();

        stringStore.FullName.Should().Be("Quartz.Impl.AdoJobStore.ExternalTransactionJobStore",
            "the legacy key is what every deployment on this store spells today, and it has to go on "
            + "naming it");
        fromCode.GetRequiredService<IJobStore>().Should().BeOfType(stringStore,
            "the typed selector exists because the string was the only way to reach this store; the two "
            + "have to arrive at the same one");
    }

    /// <summary>
    /// A whole persistent store spelled the way a configuration file spells it: two type names, a data
    /// source, and nothing in code. Naming the store in code would register a driver delegate of its
    /// own, and registration is <c>TryAdd</c>, so the assertions would be measuring the wrong one.
    /// </summary>
    private NameValueCollection StoreAndDelegateByName(string schedulerName, string driverDelegateTypeName)
    {
        return new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = schedulerName,
            ["quartz.scheduler.instanceId"] = "one",
            ["quartz.jobStore.type"] = LocalTransactionJobStoreName,
            ["quartz.jobStore.driverDelegateType"] = driverDelegateTypeName,
            ["quartz.jobStore.dataSource"] = "default",
            ["quartz.jobStore.schemaProvisioning"] = nameof(SchemaProvisioning.CreateIfMissing),
            ["quartz.dataSource.default.provider"] = "SQLite-Microsoft",
            ["quartz.dataSource.default.connectionString"] = connectionString
        };
    }

    /// <summary>
    /// A dialect delegate of the kind a third party writes, counting the one hook that only runs when
    /// the store is really acquiring triggers through it.
    /// </summary>
    /// <remarks>
    /// It derives from <see cref="SQLiteDelegate" /> because the database under it is SQLite; every
    /// member it names is public, so nothing about it depends on this assembly being a friend.
    /// </remarks>
    public sealed class CountingSqliteDelegate : SQLiteDelegate
    {
        internal static int AcquisitionSqlAsked;

        protected override string GetSelectNextTriggerToAcquireSql(TriggerAcquisitionSqlShape shape)
        {
            Interlocked.Increment(ref AcquisitionSqlAsked);
            return base.GetSelectNextTriggerToAcquireSql(shape);
        }
    }

    /// <summary>
    /// Public with a public constructor, because the store hands the job factory nothing but the type
    /// name it read back out of <c>JOB_CLASS_NAME</c>.
    /// </summary>
    public sealed class SignallingJob : IJob
    {
        internal const string SignalKey = "fired";

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            ((TaskCompletionSource) context.Scheduler.Context[SignalKey]!).TrySetResult();
            return default;
        }
    }
}
