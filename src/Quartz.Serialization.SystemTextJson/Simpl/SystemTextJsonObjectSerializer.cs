using System.Text;
using System.Text.Json;

using Quartz.Calendars;
using Quartz.Converters;
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
        JsonSerializerOptions options = new();
        options.AddQuartzConverters();
        return options;
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

        using var ms = new MemoryStream();
        JsonSerializer.Serialize<object>(ms, obj, options);

        return ms.ToArray();
    }

    /// <summary>
    /// Deserializes object from byte array presentation.
    /// </summary>
    /// <param name="obj">Data to deserialize object from.</param>
    public T? DeSerialize<T>(byte[] obj) where T : class
    {
        if (options is null)
        {
            ThrowHelper.ThrowInvalidOperationException("The serializer hasn't been initialized, did you forget to call Initialize()?");
        }

        try
        {
            using var ms = new MemoryStream(obj);
            return JsonSerializer.Deserialize<T?>(ms, options);
        }
        catch (JsonSerializationException e)
        {
            var json = Encoding.UTF8.GetString(obj);
            throw new JsonSerializationException("could not deserialize JSON: " + json, e);
        }
    }

    public static void AddCalendarSerializer<TCalendar>(ICalendarSerializer serializer)
    {
        CalendarConverter.AddCalendarConverter<TCalendar>(serializer);
    }
}