using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Quartz.Serialization.Newtonsoft;

using Quartz.Impl;

namespace Quartz;

public static class NewtonsoftJsonConfigurationExtensions
{
    /// <summary>
    /// Use Newtonsoft JSON as data serialization strategy.
    /// </summary>
    /// <param name="builder">The persistent store being configured.</param>
    /// <param name="configure">
    /// Optional registration of serializers for custom trigger and calendar types, on the same
    /// <see cref="NewtonsoftJsonSerializerRegistry" /> the serializer resolves through. What the
    /// callback registers belongs to this scheduler alone — it is not shared with any other
    /// scheduler in the process.
    /// </param>
    /// <param name="registerTriggerConverters">
    /// Whether to register optimized default trigger converters for persistence storage. These are
    /// compatible with the System.Text.Json serializer, but might not work if you have existing data
    /// in database which has been serialized with old behavior. Defaults to false.
    /// </param>
    public static IPersistentStoreBuilder UseNewtonsoftJsonSerializer(
        this IPersistentStoreBuilder builder,
        Action<NewtonsoftJsonSerializerRegistry>? configure = null,
        bool registerTriggerConverters = false)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (configure is null)
        {
            // Nothing scheduler-specific was asked for, so the serializer reads the container's registry,
            // the same way the System.Text.Json path does. Without this branch an application that
            // registers a NewtonsoftJsonSerializerRegistry of its own — the pattern the docs teach — has it
            // silently ignored in favour of a private built-ins-only one.
            builder.Services.TryAddSingleton<NewtonsoftJsonSerializerRegistry>();
            return builder.UseSerializer(provider => new NewtonsoftJsonObjectSerializer(provider.GetRequiredService<NewtonsoftJsonSerializerRegistry>())
            {
                RegisterTriggerConverters = registerTriggerConverters
            });
        }

        // The registry the callback filled is captured here rather than published to the container, which
        // is what keeps two schedulers in one container from sharing each other's custom serializers.
        var registry = new NewtonsoftJsonSerializerRegistry();
        configure(registry);
        var serializer = new NewtonsoftJsonObjectSerializer(registry)
        {
            RegisterTriggerConverters = registerTriggerConverters
        };
        return builder.UseSerializer(_ => serializer);
    }
}
