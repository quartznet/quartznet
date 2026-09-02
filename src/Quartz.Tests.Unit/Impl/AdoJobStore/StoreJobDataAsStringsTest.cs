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
using System.Text.Json;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Extensibility;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// <c>StoreJobDataAsStrings</c> — what <c>tutorial/job-stores.md</c> calls "the recommended
/// configuration" — against a real database, and the conversion it turns on.
/// </summary>
/// <remarks>
/// <para>
/// The setting was exercised by exactly one test before this one, on SQL Server, in a container leg
/// (<c>AdoJobStoreSmokeTest.ShouldBeAbleToUseMixedProperties</c>). SQLite is a file, so the whole
/// round trip — a job written through <see cref="StdAdoDelegate.SerializeJobData" />'s properties
/// branch, and read back through <c>GetMapFromProperties</c> — costs a temporary path and runs in
/// this process, which is what puts the recommended configuration in the leg that gates coverage.
/// </para>
/// <para>
/// Each case has its control: the same map through a store with the setting off, so that what is
/// asserted is the setting rather than something true of every store.
/// </para>
/// </remarks>
public sealed class StoreJobDataAsStringsTest
{
    private string databaseFile = null!;
    private string connectionString = null!;
    private ServiceProvider? container;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        databaseFile = Path.Combine(Path.GetTempPath(), $"quartz-job-data-strings-{Guid.NewGuid():N}.db");
        connectionString = $"Data Source={databaseFile}";
    }

    [TearDown]
    public async Task DeleteDatabase()
    {
        if (container is not null)
        {
            await container.DisposeAsync();
            container = null;
        }

        SqliteConnection.ClearAllPools();

        if (File.Exists(databaseFile))
        {
            File.Delete(databaseFile);
        }
    }

    /// <summary>
    /// The round trip the page promises: every value goes in a string and comes back a string, with
    /// nothing about the application's own types in between.
    /// </summary>
    [Test]
    public async Task AStringOnlyJobDataMapRoundTripsThroughTheNameValuePairColumn()
    {
        IScheduler scheduler = await GetScheduler(storeJobDataAsStrings: true);

        JobKey jobKey = new("strings", "data");
        await scheduler.AddJob(
            JobBuilder.Create<DataCarryingJob>()
                .WithIdentity(jobKey)
                .StoreDurably()
                .UsingJobData("where", "home")
                .UsingJobData("count", "5")
                .Build());

        IJobDetail? readBack = await scheduler.GetJobDetail(jobKey);

        readBack.Should().NotBeNull();
        readBack!.JobDataMap["where"].Should().Be("home");
        readBack.JobDataMap["count"].Should().BeOfType<string>()
            .And.Be("5",
                "a value that was written as a string comes back a string — the point of the setting is "
                + "that nothing between the two ever decided it looked like a number");
    }

    /// <summary>
    /// And the column holds name-value pairs rather than a serialized object, which is the sentence
    /// the page uses to explain why the setting avoids type versioning problems.
    /// </summary>
    [Test]
    public async Task TheStoredColumnHoldsNameValuePairsRatherThanASerializedObject()
    {
        IScheduler scheduler = await GetScheduler(storeJobDataAsStrings: true);

        await scheduler.AddJob(
            JobBuilder.Create<DataCarryingJob>()
                .WithIdentity("pairs", "data")
                .StoreDurably()
                .UsingJobData("count", "5")
                .Build());

        using JsonDocument stored = JsonDocument.Parse(await ReadJobData("pairs", "data"));

        stored.RootElement.GetProperty("count").ValueKind.Should().Be(JsonValueKind.String,
            "the store wrote a NameValueCollection, whose every value is a string — a column holding a "
            + "JSON number here would be the serialized-object form the setting exists to avoid");
    }

    /// <summary>
    /// The control for both cases above. With the setting off the same map is written through the
    /// object serializer, which keeps the value's type — so the two round trips genuinely differ.
    /// </summary>
    [Test]
    public async Task WithTheSettingOffAValueKeepsTheTypeItWasWrittenWith()
    {
        IScheduler scheduler = await GetScheduler(storeJobDataAsStrings: false);

        JobKey jobKey = new("objects", "data");
        await scheduler.AddJob(
            JobBuilder.Create<DataCarryingJob>()
                .WithIdentity(jobKey)
                .StoreDurably()
                .UsingJobData("count", 5)
                .Build());

        IJobDetail? readBack = await scheduler.GetJobDetail(jobKey);

        readBack!.JobDataMap["count"].Should().Be(5,
            "without the setting the map is serialized as an object graph, types and all, which is the "
            + "versioning exposure the recommended configuration trades away");

        using JsonDocument stored = JsonDocument.Parse(await ReadJobData("objects", "data"));
        stored.RootElement.GetProperty("count").ValueKind.Should().Be(JsonValueKind.Number,
            "and the column says so");
    }

    /// <summary>
    /// The refusal that makes the promise keepable: a value that is not a string cannot be written as
    /// a name-value pair, so it is refused at the moment it is stored, naming the entry.
    /// </summary>
    /// <remarks>
    /// Naming the key is the whole of the diagnostic's value. A map has as many entries as the
    /// application put in it, and "values must be strings" without one of them named leaves the reader
    /// to find it by bisection.
    /// </remarks>
    [Test]
    public async Task ANonStringValueIsRefusedAndTheOffendingKeyIsNamed()
    {
        IScheduler scheduler = await GetScheduler(storeJobDataAsStrings: true);

        Func<Task> act = async () => await scheduler.AddJob(
            JobBuilder.Create<DataCarryingJob>()
                .WithIdentity("refused", "data")
                .StoreDurably()
                .UsingJobData("where", "home")
                .UsingJobData("count", 5)
                .Build());

        SchedulerException failure = (await act.Should().ThrowAsync<SchedulerException>(
                "a value the properties format cannot carry has to fail where it was written, not read "
                + "back later as something else"))
            .Which;

        failure.Message.Should()
            .Contain("must be strings", "the reader is told what the format can carry")
            .And.Contain("count",
                "and which entry broke it — a map has as many entries as the application put in it, and "
                + "finding the offender by bisection is not a diagnostic");

        (await scheduler.GetJobDetail(new JobKey("refused", "data"))).Should().BeNull(
            "the refusal happens before the insert, so nothing half-written is left behind");
    }

    /// <summary>
    /// The two halves of the conversion, held to each other directly: what goes into the column is a
    /// <see cref="NameValueCollection" /> with the same pairs, and what comes back out is a map with
    /// the same pairs again.
    /// </summary>
    /// <remarks>
    /// <c>ConvertToProperty</c> and <c>ConvertFromProperty</c> are <see langword="protected" /> and
    /// <see langword="virtual" /> because a delegate for a store that keeps job data somewhere of its
    /// own replaces them. Reaching them through the same seam a subclass would is what makes this a
    /// test of the extension point rather than of a private detail.
    /// </remarks>
    [Test]
    public void TheTwoHalvesOfTheConversionAreEachOthersInverse()
    {
        PropertyConversionDelegate driverDelegate = CreateDelegate(useProperties: true);

        Dictionary<string, object?> original = new()
        {
            ["where"] = "home",
            ["count"] = "5",
            ["nothing"] = null,
        };

        NameValueCollection properties = driverDelegate.ToProperty(original);

        properties["where"].Should().Be("home");
        properties["count"].Should().Be("5");
        properties["nothing"].Should().BeEmpty(
            "a null has no name-value pair form, so it is written as the empty string rather than "
            + "dropping the key and changing what the job reads back");

        Dictionary<string, object?> restored = driverDelegate.FromProperty(properties);

        restored.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["where"] = "home",
            ["count"] = "5",
            ["nothing"] = "",
        });
    }

    /// <summary>
    /// And the same refusal at the level it is written, so a delegate subclass that overrides one half
    /// of the conversion knows which contract it is keeping.
    /// </summary>
    [Test]
    public void ConvertToPropertyRefusesANonStringValueByName()
    {
        PropertyConversionDelegate driverDelegate = CreateDelegate(useProperties: true);

        Action act = () => driverDelegate.ToProperty(new Dictionary<string, object?>
        {
            ["where"] = "home",
            ["count"] = 5,
        });

        act.Should().Throw<ArgumentException>()
            .WithMessage("*count*", "the key of the offending value is what the application has to fix")
            .And.Message.Should().Contain("useProperties",
                "and the setting to turn off is the other half of the answer — under its flat-key name, "
                + "which is what an application migrating from 3.x has in its configuration");
    }

    private static PropertyConversionDelegate CreateDelegate(bool useProperties)
    {
        PropertyConversionDelegate driverDelegate = new();
        driverDelegate.Initialize(new DriverDelegateContext
        {
            UseProperties = useProperties,
            TablePrefix = AdoConstants.DefaultTablePrefix,
            SchedulerName = "properties",
            InstanceId = "one",
            DbProvider = new DbProvider(TestConstants.DefaultSqlServerProvider, ""),
            TypeLoader = new SimpleTypeLoader(),
            ObjectSerializer = new SystemTextJsonObjectSerializer(),
        });

        return driverDelegate;
    }

    private async Task<string> ReadJobData(string name, string group)
    {
        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT JOB_DATA FROM QRTZ_JOB_DETAILS WHERE JOB_NAME = @name AND JOB_GROUP = @group";
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@group", group);

        object? data = await command.ExecuteScalarAsync();

        data.Should().BeOfType<byte[]>("the column is the job data blob the store wrote");

        return System.Text.Encoding.UTF8.GetString((byte[]) data!);
    }

    private async Task<IScheduler> GetScheduler(bool storeJobDataAsStrings)
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = storeJobDataAsStrings ? "as-strings" : "as-objects";
                options.InstanceId = "one";
            });

            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, connectionString);
                store.ProvisionSchema();
                store.ConfigureStore(options => options.StoreJobDataAsStrings = storeJobDataAsStrings);
            });
        });

        container = services.BuildServiceProvider();

        // Never started: what is under test is what reaches the column and what comes back out of it,
        // and a running scheduler would only add a thread that has nothing to acquire.
        return await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
    }

    /// <summary>
    /// A delegate that exposes the two conversion members, which are the seam a store keeping job data
    /// in a format of its own overrides.
    /// </summary>
    private sealed class PropertyConversionDelegate : StdAdoDelegate
    {
        internal NameValueCollection ToProperty(IDictionary<string, object?> data) => ConvertToProperty(data);

        internal Dictionary<string, object?> FromProperty(NameValueCollection properties) => ConvertFromProperty(properties);
    }

    /// <summary>
    /// Public with a public constructor, because the store hands the job factory nothing but the type
    /// name it read back out of <c>JOB_CLASS_NAME</c>.
    /// </summary>
    public sealed class DataCarryingJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
