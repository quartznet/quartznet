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

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Serialization.SystemTextJson;

namespace Quartz.Tests.Unit.Simpl;

/// <summary>
/// <see cref="SystemTextJsonConfigurationExtensions.AddJsonTypeInfoResolver" />: one registration on the
/// scheduler's builder that both of its System.Text.Json serializers read, and the failures that name it.
/// </summary>
/// <remarks>
/// <para>
/// A test host always has reflection on, so the serializers here are the ones the container built, with
/// the reflection resolver taken back out of their chain before first use — the chain a trimmed publish
/// leaves behind, exactly as <see cref="StoreFormatSourceGenerationTest" /> does it.
/// </para>
/// <para>
/// <c>Quartz.Trimming.Canary</c> proves the same out of a trimmed and a native publish, through a SQLite
/// store and a typed job.
/// </para>
/// </remarks>
public sealed class ApplicationJsonTypeInfoResolverTest
{
    private static readonly ApplicationReport Report = new("monthly", "finance@example.com");

    [Test]
    public void OneRegistrationReachesTheStoreSerializerAndTheInputSerializer()
    {
        using ServiceProvider container = Container(q => q.AddJsonTypeInfoResolver(ApplicationTypesContext.Default));

        IObjectSerializer store = WithoutReflection(container.GetRequiredService<IObjectSerializer>());
        IJobInputSerializer input = WithoutReflection(container.GetRequiredService<IJobInputSerializer>());

        JobDataMap restoredMap = store.Deserialize<JobDataMap>(store.Serialize(new JobDataMap { { "report", Report } }))!;
        restoredMap["report"].Should().BeEquivalentTo(new Dictionary<string, string> { ["Title"] = "monthly", ["Recipient"] = "finance@example.com" },
            "the store's serializer writes a type only the application's context knows, and the store format hands an object back as a string map");

        input.Deserialize<ApplicationReport>(input.Serialize(Report)).Should().Be(Report,
            "the same registration reaches the job input serializer, which the store-level callback never did");
    }

    [Test]
    public void TheStoreCallbackKeepsWorkingAndItsResolversAreAskedFirst()
    {
        using ServiceProvider container = Container(q =>
        {
            q.UsePersistentStore(store => store.UseSystemTextJsonSerializer(registry => registry.AddTypeInfoResolver(JobDataValueContext.Default)));
            q.AddJsonTypeInfoResolver(ApplicationTypesContext.Default);
        });

        SystemTextJsonObjectSerializer store = (SystemTextJsonObjectSerializer) container.GetRequiredService<IObjectSerializer>();
        IList<IJsonTypeInfoResolver> chain = store.SerializerOptions.TypeInfoResolverChain;

        chain.Should().HaveCount(5, "the contract, the registry's own types, one resolver from each registration, and reflection");
        chain[0].Should().BeOfType<QuartzStoreJsonContext>("a type Quartz names must never reach an application's resolver");
        chain.Skip(2).Should().Equal([JobDataValueContext.Default, ApplicationTypesContext.Default, chain[^1]],
            "the callback's resolvers are the registry's own and come first, then the builder's");
        chain[^1].Should().BeOfType<DefaultJsonTypeInfoResolver>("reflection stays last where a publish has left it");

        WithoutReflection(store);
        JobDataMap restored = store.Deserialize<JobDataMap>(store.Serialize(new JobDataMap { { "mode", JobDataValue.Fast } }))!;
        restored["mode"].Should().Be((int) JobDataValue.Fast,
            "the store-level registration still answers for a value only its context knows");
    }

    [Test]
    public void TheStoreCallbacksRegistryDoesNotReachTheInputSerializer()
    {
        using ServiceProvider container = Container(q =>
        {
            q.UsePersistentStore(store => store.UseSystemTextJsonSerializer(registry => registry.AddTypeInfoResolver(JobDataValueContext.Default)));
            q.AddJsonTypeInfoResolver(ApplicationTypesContext.Default);
        });

        SystemTextJsonJobInputSerializer input = (SystemTextJsonJobInputSerializer) container.GetRequiredService<IJobInputSerializer>();

        input.SerializerOptions.TypeInfoResolverChain.Should().Contain(ApplicationTypesContext.Default,
                "the builder's registration is the one both serializers read")
            .And.NotContain(JobDataValueContext.Default,
                "the callback's registry is the store serializer's own, which is why the builder's registration exists");
    }

    [Test]
    public void ResolversAreAskedInTheOrderTheyWereAdded()
    {
        using ServiceProvider container = Container(q =>
        {
            q.UsePersistentStore(store => store.UseSystemTextJsonSerializer());
            q.AddJsonTypeInfoResolver(ApplicationTypesContext.Default);
            q.AddJsonTypeInfoResolver(JobDataValueContext.Default);
        });

        IList<IJsonTypeInfoResolver> storeChain = ((SystemTextJsonObjectSerializer) container.GetRequiredService<IObjectSerializer>()).SerializerOptions.TypeInfoResolverChain;
        IList<IJsonTypeInfoResolver> inputChain = ((SystemTextJsonJobInputSerializer) container.GetRequiredService<IJobInputSerializer>()).SerializerOptions.TypeInfoResolverChain;

        storeChain.Skip(2).Should().Equal([ApplicationTypesContext.Default, JobDataValueContext.Default, storeChain[^1]],
            "UseSystemTextJsonSerializer without a callback reads the container's registry and the builder's resolvers, in the order added, before reflection");
        inputChain.Skip(2).Should().Equal([ApplicationTypesContext.Default, JobDataValueContext.Default, inputChain[^1]],
            "the input serializer reads the same resolvers in the same order");
    }

    [Test]
    public void ANamedSchedulersResolversReachNoOtherScheduler()
    {
        ServiceCollection services = new();
        services.AddQuartz();
        services.AddQuartz("reporting", q => q.AddJsonTypeInfoResolver(ApplicationTypesContext.Default));
        using ServiceProvider container = services.BuildServiceProvider();

        SystemTextJsonJobInputSerializer reporting = (SystemTextJsonJobInputSerializer) container.GetRequiredKeyedService<IJobInputSerializer>("reporting");
        SystemTextJsonJobInputSerializer defaultScheduler = (SystemTextJsonJobInputSerializer) container.GetRequiredService<IJobInputSerializer>();

        reporting.SerializerOptions.TypeInfoResolverChain.Should().Contain(ApplicationTypesContext.Default,
            "the resolver was handed to reporting's builder");
        defaultScheduler.SerializerOptions.TypeInfoResolverChain.Should().NotContain(ApplicationTypesContext.Default,
            "both schedulers read the container's registry, and what one builder was handed must not reach the other through it");
        container.GetRequiredService<SystemTextJsonSerializerRegistry>().TypeInfoResolvers.Should().NotContain(ApplicationTypesContext.Default,
            "the container's registry is shared with the HTTP API and every other scheduler, so it is read, never written");
    }

    [Test]
    public void AnInputWithNoMetadataIsRefusedNamingTheTypeAndTheFix()
    {
        IJobInputSerializer input = WithoutReflection(new SystemTextJsonJobInputSerializer());

        Action write = () => input.Serialize(Report);
        Action read = () => input.Deserialize<ApplicationReport>("""{"Title":"monthly","Recipient":"finance@example.com"}""");

        foreach ((string direction, Action act) in new[] { ("writing", write), ("reading", read) })
        {
            act.Should().Throw<NotSupportedException>(
                    "{0} an input of a type nothing describes is what System.Text.Json refuses without reflection, and the exception type stays the one it threw", direction)
                .Which.Message.Should().Contain(typeof(ApplicationReport).FullName!, "the failure has to name the type")
                .And.Contain("[JsonSerializable(typeof(ApplicationReport))]", "and the attribute that generates its metadata")
                .And.Contain("AddJsonTypeInfoResolver(MyContext.Default)", "and where the generated context is handed in, which System.Text.Json's own message cannot say");
        }
    }

    [Test]
    public void AnEnumWithNoMetadataIsRefusedNamingTheEntryAndTheFix()
    {
        IObjectSerializer store = WithoutReflection(new SystemTextJsonObjectSerializer());

        Action write = () => store.Serialize(new JobDataMap { { "priority", ApplicationPriority.High } });

        JsonSerializationException refusal = write.Should().Throw<JsonSerializationException>(
            "an enum is accepted by rule, so with reflection off it reaches the writer with nothing to describe it").Which;

        refusal.InnerException.Should().BeOfType<NotSupportedException>("System.Text.Json's own exception is kept inside");
        refusal.Message.Should().Contain("'priority'", "the failure says which entry it is about rather than 'Failed to serialize JobDataMap'")
            .And.Contain(typeof(ApplicationPriority).FullName!, "and the type")
            .And.Contain("[JsonSerializable(typeof(ApplicationPriority))]", "and the attribute")
            .And.Contain("AddJsonTypeInfoResolver(MyContext.Default)", "and where the context goes");
    }

    [Test]
    public void AnUndeclaredJobDataValueIsRefusedNamingTheSchedulerRegistration()
    {
        IObjectSerializer store = new SystemTextJsonObjectSerializer();

        Action write = () => store.Serialize(new JobDataMap { { "recipients", new List<string> { "a", "b" } } });

        write.Should().Throw<JsonSerializationException>(
                "a value the reader cannot turn back into itself is refused whether or not reflection is on")
            .Which.Message.Should().Contain("AddJsonTypeInfoResolver", "the way to declare a type is the scheduler-level registration")
            .And.Contain("SystemTextJsonSerializerRegistry", "and the HTTP API, which no builder reaches, reads the container's registry");
    }

    [Test]
    public void AGenericInputIsNamedAsSourceWritesIt()
    {
        IJobInputSerializer input = WithoutReflection(new SystemTextJsonJobInputSerializer());

        Action write = () => input.Serialize(new List<ApplicationReport> { Report });

        write.Should().Throw<NotSupportedException>()
            .Which.Message.Should().Contain("[JsonSerializable(typeof(List<ApplicationReport>))]",
                "the attribute is written as it has to be typed, not as List`1");
    }

    [Test]
    public void ATypeNestedInAGenericOneIsNamedWithoutAnArity()
    {
        MissingJsonMetadata.SourceName(typeof(Dictionary<string, int>.KeyCollection)).Should().Be("KeyCollection<String, Int32>",
            "a nested type is generic over its parent's arguments but has no arity of its own to strip");
    }

    private static ServiceProvider Container(Action<IQuartzBuilder> configure)
    {
        ServiceCollection services = new();
        services.AddQuartz(configure);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Takes the reflection resolver out of a serializer's chain before it is first used, which is the
    /// chain a trimmed publish leaves behind.
    /// </summary>
    private static T WithoutReflection<T>(T serializer)
    {
        JsonSerializerOptions options = serializer switch
        {
            SystemTextJsonObjectSerializer store => store.SerializerOptions,
            SystemTextJsonJobInputSerializer input => input.SerializerOptions,
            _ => throw new ArgumentException($"{serializer} is not one of Quartz's System.Text.Json serializers.", nameof(serializer))
        };

        IList<IJsonTypeInfoResolver> chain = options.TypeInfoResolverChain;
        for (int i = chain.Count - 1; i >= 0; i--)
        {
            if (chain[i] is DefaultJsonTypeInfoResolver)
            {
                chain.RemoveAt(i);
            }
        }

        return serializer;
    }
}

/// <summary>A record of the application's own, used both as a job data value and as a job's input.</summary>
public sealed record ApplicationReport(string Title, string Recipient);

/// <summary>An enum of the application's own, which Quartz accepts by rule but cannot describe.</summary>
public enum ApplicationPriority
{
    Low,
    High
}

/// <summary>
/// The metadata an application hands to <see cref="SystemTextJsonConfigurationExtensions.AddJsonTypeInfoResolver" />.
/// </summary>
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ApplicationReport))]
internal sealed partial class ApplicationTypesContext : JsonSerializerContext;
