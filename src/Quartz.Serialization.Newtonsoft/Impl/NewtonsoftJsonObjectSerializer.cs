using System.Text;

using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

using Quartz.Serialization.Newtonsoft;
using Quartz.Extensibility;

namespace Quartz.Impl;

/// <summary>
/// Object serialization strategy that uses <see cref="JsonSerializer" /> under the hood.
/// </summary>
/// <author>Marko Lahma</author>
public class NewtonsoftJsonObjectSerializer : IObjectSerializer
{
    private readonly Lock serializerLock = new();
    private volatile JsonSerializer? serializer;
    private bool registerTriggerConverters;

    /// <summary>
    /// Creates a serializer that knows the built-in trigger and calendar types only.
    /// </summary>
    public NewtonsoftJsonObjectSerializer()
        : this(new NewtonsoftJsonSerializerRegistry())
    {
    }

    /// <summary>
    /// Creates a serializer that resolves trigger and calendar serializers through the given registry,
    /// so a scheduler's custom types are known to its own serializer and to no other.
    /// </summary>
    public NewtonsoftJsonObjectSerializer(NewtonsoftJsonSerializerRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        Registry = registry;
    }

    /// <summary>
    /// The trigger and calendar serializers this serializer's converters resolve through.
    /// </summary>
    protected NewtonsoftJsonSerializerRegistry Registry { get; }

    /// <summary>
    /// The serializer this instance reads and writes with, built on first use so that
    /// <see cref="CreateSerializerSettings" /> is not called from the constructor.
    /// </summary>
    private JsonSerializer Serializer
    {
        get
        {
            var current = serializer;
            if (current is not null)
            {
                return current;
            }

            lock (serializerLock)
            {
                return serializer ??= JsonSerializer.Create(CreateSerializerSettings());
            }
        }
    }

    /// <summary>
    /// Whether trigger converters are registered with the underlying serializer.
    /// </summary>
    /// <remarks>
    /// The serializer is built on first use, so setting this afterwards would otherwise have no
    /// effect; assigning it discards the built serializer so the next use picks the change up.
    /// </remarks>
    public bool RegisterTriggerConverters
    {
        get => registerTriggerConverters;
        set
        {
            lock (serializerLock)
            {
                registerTriggerConverters = value;
                serializer = null;
            }
        }
    }

    protected virtual JsonSerializerSettings CreateSerializerSettings()
    {
        var settings = new JsonSerializerSettings
        {
            Converters = new List<JsonConverter>
            {
                new NameValueCollectionConverter(),
                new StringKeyDirtyFlagMapConverter(),
                new CronExpressionConverter(),
                new CalendarConverter(Registry)
            },
            ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
            TypeNameHandling = TypeNameHandling.Auto,
            ContractResolver = new ImmutableKeyContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
            DateParseHandling = DateParseHandling.DateTimeOffset
        };

        if (RegisterTriggerConverters)
        {
            settings.Converters.Add(new TriggerConverter(Registry));
        }

        return settings;
    }

    /// <summary>
    /// Serializes given object as bytes
    /// that can be stored to permanent stores.
    /// </summary>
    /// <param name="obj">Object to serialize.</param>
    public byte[] Serialize<T>(T obj) where T : class
    {
        using MemoryStream ms = new();
        using (StreamWriter sw = new(ms))
        {
            Serializer.Serialize(sw, obj, typeof(object));
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Deserializes object from byte array presentation.
    /// </summary>
    /// <param name="data">Data to deserialize object from.</param>
    public T? Deserialize<T>(byte[] data) where T : class
    {
        try
        {
            using MemoryStream ms = new(data);
            using StreamReader sr = new(ms);
            return (T?) Serializer.Deserialize(sr, typeof(T));
        }
        // Every name below is qualified on purpose. This file lives in Quartz.Impl, so an unqualified
        // JsonSerializationException binds to Quartz's type - the enclosing namespace beats the
        // "using Newtonsoft.Json" above it - and Newtonsoft's parse failures would sail straight past
        // a catch that only looked like it named them.
        catch (Exception e) when (e is Newtonsoft.Json.JsonSerializationException
                                      or Newtonsoft.Json.JsonReaderException
                                      or Quartz.JsonSerializationException)
        {
            string json = Encoding.UTF8.GetString(data);

            // Quartz's type, not Newtonsoft's: this is the exception callers catch, the HTTP API's
            // exception handler maps and the dashboard special-cases, and it must not differ between
            // the two serializers.
            throw new Quartz.JsonSerializationException($"Could not deserialize JSON: {json}", e);
        }
    }
}