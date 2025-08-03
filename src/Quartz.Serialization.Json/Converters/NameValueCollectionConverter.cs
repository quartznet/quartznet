using System;
using System.Collections.Specialized;

using Newtonsoft.Json;

namespace Quartz.Converters;

/// <summary>
/// Custom converter for (de)serializing <see cref="NameValueCollection" />.
/// </summary>
public class NameValueCollectionConverter : JsonConverter
{
    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        var collection = value as NameValueCollection;
        if (collection == null)
        {
            return;
        }

        writer.WriteStartObject();
        foreach (var key in collection.AllKeys)
        {
            writer.WritePropertyName(key);
            writer.WriteValue(collection.Get(key));
        }

        writer.WriteEndObject();
    }

    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        var nameValueCollection = new NameValueCollection();
        var key = "";
        while (reader.Read())
        {
            if (reader.TokenType == JsonToken.StartObject)
            {
                nameValueCollection = new NameValueCollection();
            }

            if (reader.TokenType == JsonToken.EndObject)
            {
                return nameValueCollection;
            }

            if (reader.TokenType == JsonToken.PropertyName)
            {
                key = reader.Value!.ToString()!;
            }

            if (reader.TokenType is JsonToken.String or JsonToken.Null)
            {
                nameValueCollection.Add(key, reader.Value?.ToString());
            }
            else if (reader.TokenType == JsonToken.Date)
            {
                // we expect that date was interpreted from string in ISO 8601 format and converted by Newtonsoft.Json
                if (reader.Value is DateTimeOffset dto)
                {
                    nameValueCollection.Add(key, dto.ToString("O"));
                }
                else if (reader.Value is DateTime dt)
                {
                    nameValueCollection.Add(key, dt.ToString("O"));
                }
                else
                {
                    nameValueCollection.Add(key, reader.Value?.ToString());
                }
            }
        }

        return nameValueCollection;
    }

    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(NameValueCollection);
    }
}