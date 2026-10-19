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

using System.Text.Json.Serialization;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Serialization.SystemTextJson;

namespace Quartz.Tests.Unit.Simpl;

/// <summary>
/// A job-data value of a type the application declared reads back as it was written: as the string map of
/// its members, a number, a boolean or null among them as its JSON text; and one that could not be read back
/// at all, a collection, is refused before it reaches the store (#4156).
/// </summary>
/// <remarks>
/// The store format reads an object back as a <c>Dictionary&lt;string, string&gt;</c> and nothing else, so it
/// can stay closed for a trimmed publish. Until #4156 a member that was not a string made that read throw, and
/// the job it was stored with could not be loaded again.
/// </remarks>
public sealed class DeclaredJobDataRoundTripTest
{
    private static readonly JobKey jobKey = new("report", "declared");

    private SqliteTestDatabase database = null!;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        database = new SqliteTestDatabase("declared-job-data");
    }

    [TearDown]
    public void DeleteDatabase()
    {
        database.Dispose();
    }

    /// <summary>
    /// The issue's repro: stored through a SQLite store, then the job read back.
    /// </summary>
    [Test]
    public async Task ARecordWithANumberReadsBackFromTheStoreAsAStringMap()
    {
        await using ServiceProvider container = Container();
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();

        await scheduler.AddJob(Job(new DeclaredCount("x", 3)), AddJobOptions.Replacing);
        IJobDetail? stored = await scheduler.GetJobDetail(jobKey);

        stored.Should().NotBeNull("the job was stored, and has to be readable again");
        stored!.JobDataMap["value"].Should().BeEquivalentTo(new Dictionary<string, string> { ["Name"] = "x", ["Count"] = "3" },
            "an object reads back as the string map of its members, and a number among them as its JSON text");
    }

    [Test]
    public void ABooleanAndANullMemberReadBackAsJsonTextAndNull()
    {
        IObjectSerializer serializer = Serializer();

        JobDataMap restored = serializer.Deserialize<JobDataMap>(serializer.Serialize(new JobDataMap { ["value"] = new DeclaredFlag(true, null) }))!;

        restored["value"].Should().BeEquivalentTo(new Dictionary<string, string?> { ["Enabled"] = "true", ["Note"] = null },
            "a boolean reads back as its JSON text and a null member as null, so whatever was written as an object can be read");
    }

    [Test]
    public void AnObjectMemberReadsBackAsItsJsonText()
    {
        IObjectSerializer serializer = Serializer();

        JobDataMap restored = serializer.Deserialize<JobDataMap>(serializer.Serialize(new JobDataMap { ["value"] = new DeclaredOuter("x", new DeclaredCount("y", 2)) }))!;

        restored["value"].Should().BeEquivalentTo(new Dictionary<string, string> { ["Name"] = "x", ["Inner"] = """{"Name":"y","Count":2}""" },
            "a member that is itself an object has no other string form in a string map");
    }

    [Test]
    public async Task ACollectionMemberIsRefusedBeforeAnythingIsStored()
    {
        await using ServiceProvider container = Container();
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();

        Func<Task> store = () => scheduler.AddJob(Job(new DeclaredList("x", ["a", "b"])), AddJobOptions.Replacing).AsTask();

        SchedulerException thrown = (await store.Should().ThrowAsync<SchedulerException>()).Which;
        Chain(thrown).OfType<JsonSerializationException>().Should().ContainSingle("the store's own failure, if it wraps one, carries the refusal")
            .Which.Message.Should().Contain("'value'", "the refusal names the entry")
            .And.Contain(typeof(DeclaredList).FullName!, "and the type")
            .And.Contain("member 'Items'", "and the member the reader could not have turned back into a string");
        (await scheduler.GetJobDetail(jobKey)).Should().BeNull("a refused value never reaches the store, where it would make the job unreadable");
    }

    [Test]
    public void ACollectionInsideAnObjectMemberIsRefusedByItsPath()
    {
        IObjectSerializer serializer = Serializer();

        Action write = () => serializer.Serialize(new JobDataMap { ["value"] = new DeclaredNested(new DeclaredList("x", ["a"])) });

        write.Should().Throw<JsonSerializationException>()
            .Which.Message.Should().Contain("member 'List.Items'", "an array at any depth has no place in the string map the reader builds");
    }

    [Test]
    public void ADeclaredCollectionIsRefusedNamingTheEntryAndTheType()
    {
        IObjectSerializer serializer = Serializer();

        Action write = () => serializer.Serialize(new JobDataMap { ["counts"] = new List<DeclaredCount> { new("x", 1) } });

        write.Should().Throw<JsonSerializationException>()
            .Which.Message.Should().Contain("'counts'", "the refusal names the entry")
            .And.Contain(typeof(List<DeclaredCount>).FullName!, "and the type")
            .And.Contain("is a collection", "and says what the reader cannot turn back");
    }

    private ServiceProvider Container()
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = $"declared-job-data-{Guid.NewGuid():N}");
            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                store.ProvisionSchema();
                store.UseSystemTextJsonSerializer(json => json.AddTypeInfoResolver(DeclaredJobDataContext.Default));
            });
        });

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// The store serializer of a scheduler handed the declared types, as a trimmed publish would hand them.
    /// </summary>
    private static IObjectSerializer Serializer()
    {
        SystemTextJsonSerializerRegistry registry = new();
        registry.AddTypeInfoResolver(DeclaredJobDataContext.Default);
        return new SystemTextJsonObjectSerializer(registry);
    }

    private static IEnumerable<Exception> Chain(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }

    private static IJobDetail Job(object value)
    {
        return JobBuilder.Create<NoOpJob>()
            .WithIdentity(jobKey)
            .StoreDurably()
            .UsingJobData(new JobDataMap { ["value"] = value })
            .Build();
    }

    public sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}

/// <summary>The issue's record: a string and a number.</summary>
public sealed record DeclaredCount(string Name, int Count);

/// <summary>A boolean and a member left null.</summary>
public sealed record DeclaredFlag(bool Enabled, string? Note);

/// <summary>An object among the members.</summary>
public sealed record DeclaredOuter(string Name, DeclaredCount Inner);

/// <summary>A collection among the members.</summary>
public sealed record DeclaredList(string Name, List<string> Items);

/// <summary>A collection one object down.</summary>
public sealed record DeclaredNested(DeclaredList List);

/// <summary>
/// What the application declares with <see cref="SystemTextJsonSerializerRegistry.AddTypeInfoResolver" />.
/// </summary>
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(DeclaredCount))]
[JsonSerializable(typeof(DeclaredFlag))]
[JsonSerializable(typeof(DeclaredOuter))]
[JsonSerializable(typeof(DeclaredList))]
[JsonSerializable(typeof(DeclaredNested))]
[JsonSerializable(typeof(List<DeclaredCount>))]
internal sealed partial class DeclaredJobDataContext : JsonSerializerContext;
