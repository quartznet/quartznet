using System.Text;

using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

using Quartz.Converters;
using Quartz.Serialization.Newtonsoft;
using Quartz.Spi;
using Quartz.Triggers;

namespace Quartz.Simpl;

/// <summary>
/// Object serialization strategy that uses <see cref="JsonSerializer" /> under the hood.
/// </summary>
/// <author>Marko Lahma</author>
public class NewtonsoftJsonObjectSerializer : IObjectSerializer
{
    private JsonSerializer serializer = null!;

    public void Initialize()
    {
        serializer = JsonSerializer.Create(CreateSerializerSettings());
    }

    public bool RegisterTriggerConverters { get; set; }

    protected virtual JsonSerializerSettings CreateSerializerSettings()
    {
        var settings = new JsonSerializerSettings
        {
            Converters = new List<JsonConverter>
            {
                new NameValueCollectionConverter(),
                new StringKeyDirtyFlagMapConverter(),
                new CronExpressionConverter(),
                new CalendarConverter()
            },
            ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
            TypeNameHandling = TypeNameHandling.Auto,
            ContractResolver = new DefaultContractResolver
            {
                IgnoreSerializableInterface = true
            },
            NullValueHandling = NullValueHandling.Ignore,
            DateParseHandling = DateParseHandling.DateTimeOffset
        };

        if (RegisterTriggerConverters)
        {
            settings.Converters.Add(new TriggerConverter());
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
        if (serializer is null)
        {
            ThrowHelper.ThrowInvalidOperationException("The serializer hasn't been initialized, did you forget to call Initialize()?");
        }

        using MemoryStream ms = new();
        using (StreamWriter sw = new(ms))
        {
            serializer.Serialize(sw, obj, typeof(object));
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Deserializes object from byte array presentation.
    /// </summary>
    /// <param name="data">Data to deserialize object from.</param>
    public T? DeSerialize<T>(byte[] data) where T : class
    {
        if (serializer is null)
        {
            ThrowHelper.ThrowInvalidOperationException("The serializer hasn't been initialized, did you forget to call Initialize()?");
        }

        try
        {
            using MemoryStream ms = new(data);
            using StreamReader sr = new(ms);
            return (T?) serializer.Deserialize(sr, typeof(T));
        }
        catch (JsonSerializationException e)
        {
            string json = Encoding.UTF8.GetString(data);
            throw new JsonSerializationException($"Could not deserialize JSON: {json}", e);
        }
    }

    public static void AddCalendarSerializer<TCalendar>(ICalendarSerializer serializer)
    {
        CalendarConverter.AddCalendarConverter<TCalendar>(serializer);
    }

    public static void AddTriggerSerializer<TTrigger>(ITriggerSerializer serializer) where TTrigger : ITrigger
    {
        TriggerConverter.AddTriggerSerializer<TTrigger>(serializer);
    }
}