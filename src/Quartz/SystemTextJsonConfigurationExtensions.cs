using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Configuration;
using Quartz.Serialization.SystemTextJson;
using Quartz.Serialization.SystemTextJson.Converters;
using Quartz.Impl;

namespace Quartz;

/// <summary>
/// Choosing System.Text.Json as a persistent store's serializer, registering the serializers for
/// trigger and calendar types of the application's own, and handing a scheduler the metadata for the
/// application's own job-data and input types.
/// </summary>
public static class SystemTextJsonConfigurationExtensions
{
    /// <summary>
    /// Hands the scheduler metadata for the application's own job-data value and job input types, which
    /// a trimmed or native AOT publish needs because it serializes nothing by reflection.
    /// </summary>
    /// <param name="builder">The scheduler being configured.</param>
    /// <param name="resolver">
    /// A <see cref="JsonSerializerContext" /> the application generated — a partial class deriving from
    /// it with a <c>[JsonSerializable(typeof(T))]</c> attribute per type — or any other
    /// <see cref="IJsonTypeInfoResolver" />. Its <c>Default</c> instance is what to hand in.
    /// </param>
    /// <remarks>
    /// <para>
    /// One registration for both of the scheduler's System.Text.Json serializers: the persistent store's,
    /// which writes job data maps, and the one that writes an <see cref="IJob{TInput}" />'s input, which
    /// every store uses, the in-memory one included. With reflection on — every untrimmed application —
    /// the resolver chain still ends in reflection and this changes nothing, so call it unconditionally.
    /// </para>
    /// <para>
    /// The chain is Quartz's own contract, then the registry the serializer reads — its trigger and
    /// calendar types and anything handed to <see cref="SystemTextJsonSerializerRegistry.AddTypeInfoResolver" />
    /// — then the resolvers handed in here, in the order they were added, then reflection where a publish
    /// has left any. A resolver that does not know a type returns nothing and the next one is asked.
    /// </para>
    /// <para>
    /// It reaches the serializers Quartz builds: the default one, both forms of
    /// <see cref="UseSystemTextJsonSerializer" />, and the job input serializer. A serializer built by the
    /// application — <c>UseSerializer(…)</c> — reads whatever registry it was given, and the HTTP API
    /// reads the container's <see cref="SystemTextJsonSerializerRegistry" />.
    /// </para>
    /// <para>
    /// What belongs to one scheduler belongs to it alone: under <c>AddQuartz("reporting", …)</c> the
    /// resolver reaches <c>reporting</c>'s serializers and no other scheduler's.
    /// </para>
    /// </remarks>
    public static IQuartzBuilder AddJsonTypeInfoResolver(this IQuartzBuilder builder, IJsonTypeInfoResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(resolver);

        return builder.ConfigureOptions<JsonTypeInfoResolverOptions>(options => options.Resolvers.Add(resolver));
    }

    /// <summary>
    /// Use System.Text.Json as data serialization strategy.
    /// </summary>
    /// <param name="builder">The persistent store being configured.</param>
    /// <param name="configure">
    /// Optional registration of serializers for custom trigger and calendar types, on the same
    /// <see cref="SystemTextJsonSerializerRegistry" /> the serializer resolves through. What the
    /// callback registers belongs to this scheduler alone — it is not shared with any other
    /// scheduler in the process.
    /// </param>
    /// <remarks>
    /// Either way, the serializer also reads what the scheduler's builder was handed through
    /// <see cref="AddJsonTypeInfoResolver" />, behind the registry's own resolvers.
    /// </remarks>
    public static IPersistentStoreBuilder UseSystemTextJsonSerializer(
        this IPersistentStoreBuilder builder,
        Action<SystemTextJsonSerializerRegistry>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        string schedulerName = builder.SchedulerName;

        if (configure is null)
        {
            // Nothing scheduler-specific was asked for, so the serializer reads the container's registry
            // — the same set the HTTP API and dashboard see.
            return builder.UseSerializer(provider => new SystemTextJsonObjectSerializer(
                JsonTypeInfoResolverOptions.Apply(provider, schedulerName, provider.GetRequiredService<SystemTextJsonSerializerRegistry>())));
        }

        // The registry the callback filled is captured here rather than published to the container, which
        // is what keeps two schedulers in one container from sharing each other's custom serializers.
        var registry = new SystemTextJsonSerializerRegistry();
        configure(registry);
        return builder.UseSerializer(provider => new SystemTextJsonObjectSerializer(
            JsonTypeInfoResolverOptions.Apply(provider, schedulerName, registry)));
    }

    internal static JsonSerializerOptions AddQuartzConverters(
        this JsonSerializerOptions options,
        SystemTextJsonSerializerRegistry registry,
        bool newtonsoftCompatibilityMode)
    {
        options.Converters.Add(new CalendarConverter(registry, newtonsoftCompatibilityMode));
        options.Converters.Add(new CronExpressionConverter());
        options.Converters.Add(new JobDataMapConverter(registry));
        options.Converters.Add(new JobKeyConverter());
        options.Converters.Add(new TriggerKeyConverter());
        options.Converters.Add(new NameValueCollectionConverter());
        options.Converters.Add(new TriggerConverter(registry));
        return options;
    }
}
