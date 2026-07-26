using Quartz.Serialization.Newtonsoft;

using Quartz.Simpl;
using Quartz.Triggers;

namespace Quartz;

public static class JsonConfigurationExtensions
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

        var options = new NewtonsoftJsonSerializerOptions();
        configure?.Invoke(options);

        // The registry the callback filled is captured here rather than published to the container, which
        // is what keeps two schedulers in one container from sharing each other's custom serializers.
        var serializer = new NewtonsoftJsonObjectSerializer(options.Registry)
        {
            RegisterTriggerConverters = options.RegisterTriggerConverters
        };
        serializer.Initialize();
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
    public NewtonsoftJsonSerializerOptions AddCalendarSerializer<TCalendar>(ICalendarSerializer serializer)
    {
        Registry.AddCalendarSerializer<TCalendar>(serializer);
        return this;
    }
}
