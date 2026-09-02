using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using Quartz.Serialization.SystemTextJson;
using Quartz.Extensibility;

namespace Quartz.Impl;

/// <summary>
/// Default object serialization strategy that uses <see cref="JsonSerializer" /> under the hood.
/// </summary>
/// <author>Marko Lahma</author>
public class SystemTextJsonObjectSerializer : IObjectSerializer
{
    private readonly Lock optionsLock = new();
    private volatile JsonSerializerOptions? options;

    /// <summary>
    /// Creates a serializer that knows the built-in trigger and calendar types only.
    /// </summary>
    public SystemTextJsonObjectSerializer()
        : this(new SystemTextJsonSerializerRegistry())
    {
    }

    /// <summary>
    /// Creates a serializer that resolves trigger and calendar serializers through the given registry,
    /// so a scheduler's custom types are known to its own serializer and to no other.
    /// </summary>
    public SystemTextJsonObjectSerializer(SystemTextJsonSerializerRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        Registry = registry;
    }

    /// <summary>
    /// The trigger and calendar serializers this serializer's converters resolve through.
    /// </summary>
    protected SystemTextJsonSerializerRegistry Registry { get; }

    /// <summary>
    /// The options this serializer reads and writes with, built on first use so that
    /// <see cref="CreateSerializerOptions" /> is not called from the constructor.
    /// </summary>
    private JsonSerializerOptions Options
    {
        get
        {
            var current = options;
            if (current is not null)
            {
                return current;
            }

            lock (optionsLock)
            {
                return options ??= CreateSerializerOptions();
            }
        }
    }

    /// <summary>
    /// Builds the options this serializer reads and writes with: Quartz's converters, and the resolver
    /// chain that lets them work where reflection-based serialization is switched off.
    /// </summary>
    /// <remarks>
    /// The chain is Quartz's own generated
    /// <see cref="System.Text.Json.Serialization.JsonSerializerContext" />, then the scheduler's
    /// registry — the trigger and calendar types registered with it, and whatever the application handed to
    /// <see cref="SystemTextJsonSerializerRegistry.AddTypeInfoResolver" /> — then reflection, where a
    /// publish has left any. A resolver decides only how a type is answered; the payload is still
    /// written by the converters, byte for byte as every earlier version of Quartz wrote it.
    /// </remarks>
    protected virtual JsonSerializerOptions CreateSerializerOptions()
    {
        JsonSerializerOptions options = new JsonSerializerOptions().AddQuartzConverters(Registry, newtonsoftCompatibilityMode: true);
        options.UseQuartzContract(QuartzStoreJsonContext.Default, Registry);
        return options;
    }

    /// <summary>
    /// Serializes given object as bytes
    /// that can be stored to permanent stores.
    /// </summary>
    /// <param name="obj">Object to serialize.</param>
    /// <remarks>
    /// Written as <see cref="object" /> so that the payload names the runtime type, which is what every
    /// stored blob has always said. Asking the options for that type's metadata and handing the result
    /// to <see cref="JsonSerializer" /> is the same call the overload taking
    /// <see cref="JsonSerializerOptions" /> would make internally, minus that overload's blanket
    /// <c>RequiresUnreferencedCode</c> — it is what a trimmed application needs and what keeps this
    /// class out of the trim-analysis baseline.
    /// </remarks>
    public byte[] Serialize<T>(T obj) where T : class
    {
        JsonTypeInfo typeInfo = Options.GetTypeInfo(typeof(object));
        return JsonSerializer.SerializeToUtf8Bytes(obj, typeInfo);
    }

    /// <summary>
    /// Deserializes object from byte array presentation.
    /// </summary>
    /// <param name="data">Data to deserialize object from.</param>
    public T? Deserialize<T>(byte[] data) where T : class
    {
        try
        {
            JsonTypeInfo<T> typeInfo = (JsonTypeInfo<T>) Options.GetTypeInfo(typeof(T));
            return JsonSerializer.Deserialize(data, typeInfo);
        }
        // Quartz's exception comes from Quartz's own converters; System.Text.Json's comes from the
        // reader, which rejects a payload whose very first token is malformed before any converter is
        // reached. Both are the same failure to a caller, and both have to arrive as Quartz's type.
        catch (Exception e) when (e is Quartz.JsonSerializationException or System.Text.Json.JsonException)
        {
            string json = Encoding.UTF8.GetString(data);
            throw new Quartz.JsonSerializationException($"Could not deserialize JSON: {json}", e);
        }
    }
}