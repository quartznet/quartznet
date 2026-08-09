using System.Text;
using System.Text.Json;

using Quartz.Serialization.Json;
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

    protected virtual JsonSerializerOptions CreateSerializerOptions()
    {
        return new JsonSerializerOptions().AddQuartzConverters(Registry, newtonsoftCompatibilityMode: true);
    }

    /// <summary>
    /// Serializes given object as bytes
    /// that can be stored to permanent stores.
    /// </summary>
    /// <param name="obj">Object to serialize.</param>
    public byte[] Serialize<T>(T obj) where T : class
    {
        return JsonSerializer.SerializeToUtf8Bytes<object>(obj, Options);
    }

    /// <summary>
    /// Deserializes object from byte array presentation.
    /// </summary>
    /// <param name="data">Data to deserialize object from.</param>
    public T? Deserialize<T>(byte[] data) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T?>(data, Options);
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