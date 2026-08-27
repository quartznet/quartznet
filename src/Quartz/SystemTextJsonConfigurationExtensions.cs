using System.Text.Json;

using Quartz.Serialization.SystemTextJson;
using Quartz.Serialization.SystemTextJson.Converters;
using Quartz.Impl;

namespace Quartz;

public static class SystemTextJsonConfigurationExtensions
{
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
    public static IPersistentStoreBuilder UseSystemTextJsonSerializer(
        this IPersistentStoreBuilder builder,
        Action<SystemTextJsonSerializerRegistry>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (configure is null)
        {
            // Nothing scheduler-specific was asked for, so the serializer reads the container's registry
            // — the same set the HTTP API and dashboard see.
            return builder.UseSerializer<SystemTextJsonObjectSerializer>();
        }

        // The registry the callback filled is captured here rather than published to the container, which
        // is what keeps two schedulers in one container from sharing each other's custom serializers.
        var registry = new SystemTextJsonSerializerRegistry();
        configure(registry);
        return builder.UseSerializer(_ =>
        {
            var serializer = new SystemTextJsonObjectSerializer(registry);
            return serializer;
        });
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
