using Newtonsoft.Json;

namespace Quartz.Serialization.Newtonsoft;

/// <summary>
/// A retry policy travels as its stored string — the same spelling the <c>RETRY_POLICY</c> column
/// holds and the trigger serializers write.
/// </summary>
/// <remarks>
/// <para>
/// Without this, a trigger written as a plain object graph — which is what
/// <see cref="Quartz.Impl.NewtonsoftJsonObjectSerializer.RegisterTriggerConverters" /> being off
/// means, and off is the default — could not be read back at all. Json.NET's default contract writes
/// a <see cref="Quartz.RetryPolicy" /> as its public properties and then has nothing to rebuild it
/// with: the type has no public constructor, on purpose, so that a policy that could not be honoured
/// cannot be built. Deserialization failed outright with "unable to find a constructor to use", which
/// took the whole trigger with it.
/// </para>
/// <para>
/// This is the same shape of problem <see cref="TimeZoneInfoConverter" /> exists for, and it is
/// attached the same way: <c>QuartzContractResolver</c> puts it on members typed as a
/// <see cref="Quartz.RetryPolicy" /> rather than registering it on the serializer, because the
/// serializer's converter list is consulted for a value's runtime type wherever that value appears —
/// a policy held in a job data map would be written as a bare string and lose the <c>$type</c> that
/// path carries.
/// </para>
/// <para>
/// There is no older object form to keep reading: no released version ever wrote one, because the
/// property and this converter arrive together.
/// </para>
/// </remarks>
internal sealed class RetryPolicyConverter : JsonConverter
{
    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        writer.WriteValue(((RetryPolicy?) value)?.ToStoredString());
    }

    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        switch (reader.TokenType)
        {
            case JsonToken.Null:
                return null;

            case JsonToken.String:
                string stored = (string) reader.Value!;
                if (!RetryPolicy.TryParse(stored, out RetryPolicy? policy))
                {
                    throw new Quartz.JsonSerializationException($"Could not read a retry policy from '{stored}'");
                }

                return policy;

            default:
                throw new Quartz.JsonSerializationException($"Could not read a retry policy from a {reader.TokenType} token");
        }
    }

    public override bool CanConvert(Type objectType) => objectType == typeof(RetryPolicy);
}
