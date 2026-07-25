using Quartz.Serialization.Newtonsoft;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Simpl;
using Quartz.Spi;
using Quartz.Triggers;

namespace Quartz;

public static class JsonConfigurationExtensions
{
    /// <summary>
    /// Use Newtonsoft JSON as data serialization strategy.
    /// </summary>
    public static IPersistentStoreBuilder UseNewtonsoftJsonSerializer(
        this IPersistentStoreBuilder builder,
        Action<NewtonsoftJsonSerializerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new NewtonsoftJsonSerializerOptions();
        configure?.Invoke(options);
        builder.Services.AddSingleton<IObjectSerializer>(
            new NewtonsoftJsonObjectSerializer { RegisterTriggerConverters = options.RegisterTriggerConverters });
        return builder;
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
    /// Add serializer for custom trigger
    /// </summary>
    public NewtonsoftJsonSerializerOptions AddTriggerSerializer<TTrigger>(ITriggerSerializer serializer) where TTrigger : ITrigger
    {
        NewtonsoftJsonObjectSerializer.AddTriggerSerializer<TTrigger>(serializer);
        return this;
    }

    /// <summary>
    /// Add serializer for custom calendar
    /// </summary>
    public NewtonsoftJsonSerializerOptions AddCalendarSerializer<TCalendar>(ICalendarSerializer serializer)
    {
        NewtonsoftJsonObjectSerializer.AddCalendarSerializer<TCalendar>(serializer);
        return this;
    }
}