using Microsoft.Extensions.DependencyInjection.Extensions;

using Quartz.Serialization.Newtonsoft;

using Quartz.Impl;
using Quartz.Serialization.Newtonsoft.Triggers;

namespace Quartz;

public static class NewtonsoftJsonConfigurationExtensions
{
    /// <summary>
    /// Use Newtonsoft JSON as data serialization strategy.
    /// </summary>
    /// <param name="builder">The persistent store being configured.</param>
    /// <param name="configure">
    /// Optional serializer settings and registration of serializers for custom trigger and calendar
    /// types. What the callback registers belongs to this scheduler alone — it is not shared with any
    /// other scheduler in the process.
    /// </param>
    public static IPersistentStoreBuilder UseNewtonsoftJsonSerializer(
        this IPersistentStoreBuilder builder,
        Action<NewtonsoftJsonSerializerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (configure is null)
        {
            // Nothing scheduler-specific was asked for, so the serializer reads the container's registry,
            // the same way the System.Text.Json path does. Without this branch an application that
            // registers a NewtonsoftJsonSerializerRegistry of its own — the pattern the docs teach — has it
            // silently ignored in favour of a private built-ins-only one.
            builder.Services.TryAddSingleton<NewtonsoftJsonSerializerRegistry>();
            return builder.UseSerializer<NewtonsoftJsonObjectSerializer>();
        }

        var options = new NewtonsoftJsonSerializerOptions();
        configure.Invoke(options);

        // The registry the callback filled is captured here rather than published to the container, which
        // is what keeps two schedulers in one container from sharing each other's custom serializers.
        var serializer = new NewtonsoftJsonObjectSerializer(options.Registry)
        {
            RegisterTriggerConverters = options.RegisterTriggerConverters
        };
        return builder.UseSerializer(_ => serializer);
    }
}

public class NewtonsoftJsonSerializerOptions
{
    /// <summary>
    /// Whether to register optimized default trigger converters for persistence storage. These are compatible with STJ
    /// serializer, but might not work if you have existing data in database which has been serialized with old behavior.
    /// Defaults to false.
    /// </summary>
    public bool RegisterTriggerConverters { get; set; }

    /// <summary>
    /// The serializers registered so far, seeded with the built-in trigger and calendar types.
    /// </summary>
    internal NewtonsoftJsonSerializerRegistry Registry { get; } = new();

    /// <summary>
    /// Add serializer for custom trigger
    /// </summary>
    public NewtonsoftJsonSerializerOptions AddTriggerSerializer<TTrigger>(ITriggerSerializer serializer) where TTrigger : ITrigger
    {
        Registry.AddTriggerSerializer<TTrigger>(serializer);
        return this;
    }

    /// <summary>
    /// Add serializer for custom calendar
    /// </summary>
    public NewtonsoftJsonSerializerOptions AddCalendarSerializer<TCalendar>(ICalendarSerializer serializer) where TCalendar : ICalendar
    {
        Registry.AddCalendarSerializer<TCalendar>(serializer);
        return this;
    }
}
