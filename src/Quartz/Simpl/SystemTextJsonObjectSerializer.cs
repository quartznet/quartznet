using System.Text;
using System.Text.Json;

using Quartz.Serialization.Json.Calendars;
using Quartz.Serialization.Json.Converters;
using Quartz.Serialization.Json.Triggers;
using Quartz.Spi;

namespace Quartz.Simpl;

/// <summary>
/// Default object serialization strategy that uses <see cref="JsonSerializer" /> under the hood.
/// </summary>
/// <author>Marko Lahma</author>
public class SystemTextJsonObjectSerializer : IObjectSerializer
{
    private JsonSerializerOptions options = null!;

    public void Initialize()
    {
        options = CreateSerializerOptions();
    }

    protected virtual JsonSerializerOptions CreateSerializerOptions()
    {
        return new JsonSerializerOptions().AddQuartzConverters(newtonsoftCompatibilityMode: true);
    }

    /// <summary>
    /// Serializes given object as bytes
    /// that can be stored to permanent stores.
    /// </summary>
    /// <param name="obj">Object to serialize.</param>
    public byte[] Serialize<T>(T obj) where T : class
    {
        if (options is null)
        {
            ThrowHelper.ThrowInvalidOperationException("The serializer hasn't been initialized, did you forget to call Initialize()?");
        }

        return JsonSerializer.SerializeToUtf8Bytes<object>(obj, options);
    }

    /// <summary>
    /// Deserializes object from byte array presentation.
    /// </summary>
    /// <param name="data">Data to deserialize object from.</param>
    public T? DeSerialize<T>(byte[] data) where T : class
    {
        if (options is null)
        {
            ThrowHelper.ThrowInvalidOperationException("The serializer hasn't been initialized, did you forget to call Initialize()?");
        }

        try
        {
            return JsonSerializer.Deserialize<T?>(data, options);
        }
        catch (JsonSerializationException e)
        {
            string json = Encoding.UTF8.GetString(data);
            throw new JsonSerializationException($"Could not deserialize JSON: {json}", e);
        }
    }

    public static void AddTriggerSerializer<TTrigger>(ITriggerSerializer serializer) where TTrigger : ITrigger
    {
        TriggerConverter.AddTriggerSerializer<TTrigger>(serializer);
    }

    public static void AddCalendarSerializer<TCalendar>(ICalendarSerializer serializer) where TCalendar : ICalendar
    {
        CalendarConverter.AddSerializer<TCalendar>(serializer);
    }
}