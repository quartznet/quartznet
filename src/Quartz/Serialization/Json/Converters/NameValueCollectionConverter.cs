using System.Collections.Specialized;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Quartz.Serialization.Json.Converters;

/// <summary>
/// Custom converter for (de)serializing <see cref="NameValueCollection" />.
/// </summary>
internal sealed class NameValueCollectionConverter : JsonConverter<NameValueCollection>
{
    public override void Write(Utf8JsonWriter writer, NameValueCollection value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (string? key in value.AllKeys)
        {
            if (key is null)
            {
                continue;
            }
            writer.WriteString(key, value.Get(key));
        }
        writer.WriteEndObject();
    }

    public override NameValueCollection Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        NameValueCollection nameValueCollection = new();
        string? key = "";
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                nameValueCollection = new NameValueCollection();
            }
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return nameValueCollection;
            }
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                key = reader.GetString();
            }
            if (reader.TokenType == JsonTokenType.String)
            {
                nameValueCollection.Add(key, reader.GetString());
            }
            else if (reader.TokenType == JsonTokenType.Null)
            {
                nameValueCollection.Add(key, value: null);
            }
        }
        return nameValueCollection;
    }
}