using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using Quartz.HttpApiContract;
using Quartz.Serialization.SystemTextJson;
using Quartz.Serialization.SystemTextJson.Converters;
using Quartz.Tests.Unit.Simpl;

namespace Quartz.Tests.Unit.HttpApi;

/// <summary>
/// The wire contract's closed shapes are answered from generated metadata, and its open ones from the
/// converters that know the scheduler's serializer registry.
/// </summary>
/// <remarks>
/// A contract type that is not listed in <c>HttpApiJsonContext</c> still serializes — the chain falls
/// through to reflection behind the context — so nothing else would notice it was left out. This is
/// what notices.
/// </remarks>
public class WireFormatSourceGenerationTest
{
    private static JsonSerializerOptions WireOptions()
    {
        return new JsonSerializerOptions(JsonSerializerDefaults.Web)
            .ConfigureWireFormat(new SystemTextJsonSerializerRegistry());
    }

    /// <summary>
    /// Every non-generic type in the contract's namespace, which is the set the API can put on the wire.
    /// </summary>
    private static IEnumerable<Type> ContractTypes()
    {
        return typeof(SchedulerDto).Assembly.GetTypes()
            .Where(type => type.Namespace == "Quartz.HttpApiContract")
            .Where(type => !type.IsNested && !type.IsInterface && !type.IsAbstract && !type.IsGenericTypeDefinition)
            .Where(type => type.GetCustomAttribute<CompilerGeneratedAttribute>() is null)
            .Where(type => type != typeof(HttpApiJsonContext))
            .OrderBy(type => type.Name, StringComparer.Ordinal);
    }

    /// <summary>
    /// The page envelope is generic, so it is on the wire once per thing it carries and each closed
    /// form has to be listed on its own.
    /// </summary>
    private static IEnumerable<Type> PagedBodies()
    {
        yield return typeof(PagedResultDto<FireInstanceDto>);
        yield return typeof(PagedResultDto<JobGroupDto>);
        yield return typeof(PagedResultDto<JobHeaderDto>);
        yield return typeof(PagedResultDto<string>);
        yield return typeof(PagedResultDto<TriggerGroupDto>);
        yield return typeof(PagedResultDto<TriggerHeaderDto>);
    }

    [TestCaseSource(nameof(ContractTypes))]
    public void ContractTypeIsAnsweredByTheGeneratedContext(Type contractType)
    {
        JsonTypeInfo typeInfo = WireOptions().GetTypeInfo(contractType);

        typeInfo.OriginatingResolver.Should().BeOfType<HttpApiJsonContext>(
            $"{contractType.Name} is part of the wire contract, so it needs a [JsonSerializable] entry in HttpApiJsonContext; without one it falls through to reflection and no longer survives trimming");
    }

    [TestCaseSource(nameof(PagedBodies))]
    public void PagedBodyIsAnsweredByTheGeneratedContext(Type pagedBody)
    {
        JsonTypeInfo typeInfo = WireOptions().GetTypeInfo(pagedBody);

        typeInfo.OriginatingResolver.Should().BeOfType<HttpApiJsonContext>(
            "a page of a listing is a body like any other, and the generator only sees the closed forms it is given");
    }

    [Test]
    public void TheGeneratedContextIsAskedFirst()
    {
        JsonSerializerOptions options = WireOptions();

        options.TypeInfoResolverChain[0].Should().BeOfType<HttpApiJsonContext>(
            "a contract type must never reach the reflection resolver, and the chain is consulted in order");
        options.TypeInfoResolverChain[^1].Should().BeOfType<DefaultJsonTypeInfoResolver>(
            "the values inside a JobDataMap are whatever the application put there, so the chain has to end in reflection");
    }

    /// <summary>
    /// The wire has the same open half the store format does — the values inside a
    /// <see cref="JobDataMap" /> — so it is given the same seam, assembled by the same method.
    /// </summary>
    [Test]
    public void TheSchedulersRegistryIsAskedBehindTheContract()
    {
        SystemTextJsonSerializerRegistry registry = new();
        registry.AddTypeInfoResolver(JobDataValueContext.Default);

        JsonSerializerOptions options = new JsonSerializerOptions(JsonSerializerDefaults.Web).ConfigureWireFormat(registry);

        options.TypeInfoResolverChain[2].Should().BeSameAs(JobDataValueContext.Default,
            "an application publishing trimmed answers for its own job-data value types once, and both formats read the same registry");
    }

    [Test]
    public void TypesOutsideTheContractStillReachReflection()
    {
        JsonTypeInfo typeInfo = WireOptions().GetTypeInfo(typeof(Uri));

        typeInfo.OriginatingResolver.Should().BeOfType<DefaultJsonTypeInfoResolver>(
            "putting the contract in front of the reflection resolver must not take the reflection resolver away");
    }

    [Test]
    public void ConfiguringTwiceDoesNotStackTheContext()
    {
        JsonSerializerOptions options = WireOptions().ConfigureWireFormat(new SystemTextJsonSerializerRegistry());

        options.TypeInfoResolverChain.Count(resolver => resolver is HttpApiJsonContext).Should().Be(1,
            "the options are the whole container's on the server, and asking twice must leave them as one ask does");
    }

    [TestCase(typeof(ITrigger), typeof(TriggerConverter))]
    [TestCase(typeof(ICalendar), typeof(CalendarConverter))]
    [TestCase(typeof(JobDataMap), typeof(JobDataMapConverter))]
    public void OpenTypesStillGoThroughTheirConverter(Type openType, Type converterType)
    {
        JsonTypeInfo typeInfo = WireOptions().GetTypeInfo(openType);

        typeInfo.Converter.Should().BeOfType(converterType,
            $"{openType.Name} is whatever the application made it, so it is read and written by the converter that consults the scheduler's serializer registry rather than by a contract the generator could write");
    }

    [Test]
    public void GeneratedMetadataObeysTheOptionsRatherThanTheContext()
    {
        SchedulerDto dto = new(
            SchedulerInstanceId: "NON_CLUSTERED",
            Name: "TestScheduler",
            Status: SchedulerStatus.Running,
            ThreadPool: new SchedulerThreadPoolDto("Quartz.Impl.DefaultThreadPool, Quartz", 10),
            JobStore: new SchedulerJobStoreDto("Quartz.Impl.RAMJobStore, Quartz", Clustered: false, Persistent: false),
            Statistics: new SchedulerStatisticsDto("1.2.3", RunningSince: null, JobsExecuted: 7, LocalExecutingJobs: 1));

        string json = JsonSerializer.Serialize(dto, WireOptions());

        json.Should().Be(
            """{"schedulerInstanceId":"NON_CLUSTERED","name":"TestScheduler","status":"Running","threadPool":{"type":"Quartz.Impl.DefaultThreadPool, Quartz","size":10},"jobStore":{"type":"Quartz.Impl.RAMJobStore, Quartz","clustered":false,"persistent":false},"statistics":{"version":"1.2.3","runningSince":null,"jobsExecuted":7,"localExecutingJobs":1}}""",
            "the generated metadata carries no naming policy and no enum spelling of its own - both come from the options in use, which is what keeps the wire snapshots where they were");
    }
}
