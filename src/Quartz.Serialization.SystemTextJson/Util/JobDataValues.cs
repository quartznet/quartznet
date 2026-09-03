using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

using Quartz.Serialization.SystemTextJson;

namespace Quartz.Util;

/// <summary>
/// What a job data value may be on the way in: what <see cref="Refuse" /> lets past is what
/// <c>SerializationExtensions.GetJobDataMap</c> can hand back on the way out.
/// </summary>
/// <remarks>
/// <para>
/// The read side has always been closed — a stored value comes back as a string, a bool, an int, a
/// long, a double, null or a <c>Dictionary&lt;string, string&gt;</c>, and there is no polymorphic
/// <see cref="object" /> deserialization. The write side was not closed to match: it wrote whatever
/// the runtime could serialize, so a <c>List&lt;string&gt;</c> went into the database happily and threw
/// on the way back out — by which time the blob was already stored, and unreadable, and every later
/// acquisition of the trigger failed on it (#3495). A writer that consults the same declaration cannot
/// drift from the reader again.
/// </para>
/// <para>
/// The rule is what the reader cannot hand back, and it is not a list of blessed types: a value is
/// refused when its JSON is an array, or an object of any shape but the
/// <c>Dictionary&lt;string, string&gt;</c> the reader produces. A string, a number, a boolean and a null
/// all come back — as themselves, or as what the <see cref="JobDataMap" /> accessors coerce them into,
/// which is the store format rather than an oversight. So a <c>short</c>, a <c>byte[]</c>, a
/// <see cref="Uri" /> and a <see cref="Version" /> are written today and are written still: each is a
/// number or a string in the column, and a list that named the reader's own types would have refused
/// them and turned working applications into failing ones over an upgrade.
/// </para>
/// <para>
/// <see cref="Accepted" /> is a fast path over that rule rather than the rule itself. It answers the
/// types an ordinary job data map is made of without writing anything; everything else is written once,
/// on its own, and judged by the token that came out — which is the same question the reader will ask
/// of the stored bytes, asked before there are any.
/// </para>
/// <para>
/// Past that, a value is the application's own choice, and a <see cref="JsonConverter" /> of its own
/// added to the options — through an override of
/// <c>SystemTextJsonObjectSerializer.CreateSerializerOptions</c> — is how it declares one. A type an
/// application has already answered for is a type this refuses nothing about.
/// </para>
/// </remarks>
internal static class JobDataValues
{
    /// <summary>
    /// The types answered without writing anything: every one of them is a string, a number or a
    /// boolean in the column, so the probe below would only agree at the cost of a serialization.
    /// </summary>
    private static readonly HashSet<Type> Accepted = new HashSet<Type>
    {
        // Read back as themselves.
        typeof(string),
        typeof(bool),
        typeof(int),
        typeof(long),
        typeof(double),
        typeof(Dictionary<string, string>),

        // Written as a number, and read back as an int, a long or a double.
        typeof(sbyte),
        typeof(byte),
        typeof(short),
        typeof(ushort),
        typeof(uint),
        typeof(ulong),
        typeof(float),
        typeof(decimal),

        // Written as a string, and read back as one; the JobDataMap accessors coerce.
        typeof(char),
        typeof(DateTime),
        typeof(DateTimeOffset),
        typeof(TimeSpan),
        typeof(Guid),
        typeof(Uri),
        typeof(byte[]),
#if NET6_0_OR_GREATER
        typeof(DateOnly),
        typeof(TimeOnly)
#endif
    };

    /// <summary>
    /// Throws unless <paramref name="value" /> is one the reader will hand back.
    /// </summary>
    /// <remarks>
    /// Runs before a single byte reaches the writer, because a blob already in the column is the
    /// failure this exists to prevent. The scalars are answered by name; anything else is written on
    /// its own and answered by the token it wrote as.
    /// </remarks>
    /// <exception cref="JsonSerializationException">
    /// The value writes as a JSON array, or as an object of a shape the reader turns into something
    /// else, and the application has not declared a converter for it.
    /// </exception>
    public static void Refuse(string key, object? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            return;
        }

        Type type = value.GetType();
        if (Accepted.Contains(type) || type.IsEnum || HasApplicationConverter(type, options))
        {
            return;
        }

        // A map is answered without the probe. The reader hands an object back as a
        // Dictionary<string, string> and as nothing else, so a map of anything else cannot come back
        // whatever it happens to hold today — and writing one to find out would re-enter this check
        // through Quartz's own map converters, which a map holding itself would do until the stack ran
        // out. That is also what keeps a JobDataMap out of a JobDataMap.
        if (value is IDictionary && !IsStringMap(value))
        {
            throw Refusal(key, type, innerException: null);
        }

        JsonTokenType token;
        try
        {
            token = RootTokenOf(value, type, options);
        }
        catch (Exception e)
        {
            // A value that cannot be written at all is one no reader will ever hand back, and this is
            // the message that says what to do about it.
            throw Refusal(key, type, e);
        }

        switch (token)
        {
            case JsonTokenType.String:
            case JsonTokenType.Number:
            case JsonTokenType.True:
            case JsonTokenType.False:
            case JsonTokenType.Null:
                return;

            // The one object the reader produces. An object of any other shape comes back as a string
            // map the job that stored it cannot use, or - once a value in it is not a string - as an
            // exception on every later read of the trigger.
            case JsonTokenType.StartObject when IsStringMap(value):
                return;

            default:
                throw Refusal(key, type, innerException: null);
        }
    }

    /// <summary>
    /// The token a single value writes as, which is the whole of the question: the reader decides what
    /// to hand back by the token it reads and by nothing else.
    /// </summary>
    private static JsonTokenType RootTokenOf(object value, Type type, JsonSerializerOptions options)
    {
        byte[] written = JsonSerializer.SerializeToUtf8Bytes(value, type, options);

        Utf8JsonReader reader = new Utf8JsonReader(written);
        return reader.Read() ? reader.TokenType : JsonTokenType.None;
    }

    /// <summary>
    /// Whether the value is the string-to-string map an object is read back as. The type says so
    /// rather than the JSON, because a map of strings and an object whose properties are all strings
    /// are the same bytes and only one of them comes back as what went in.
    /// </summary>
    private static bool IsStringMap(object value)
    {
        return value is IEnumerable<KeyValuePair<string, string>>;
    }

    private static JsonSerializationException Refusal(string key, Type type, Exception? innerException)
    {
        return new JsonSerializationException(
            $"Job data entry '{key}' holds a {type.FullName}, which a persistent store cannot read back. " +
            "A stored value comes back as a string, a number, a boolean, null or a Dictionary<string, string>, " +
            "so a value written as a JSON array - or as an object of any other shape - is a blob the next read of this job fails on. " +
            "Serialize it in the job and store the result as a string, or declare a JsonConverter for it by overriding SystemTextJsonObjectSerializer.CreateSerializerOptions.",
            innerException);
    }

    /// <summary>
    /// Whether the application added a converter of its own for the type. Quartz's own converters do
    /// not count: they answer for a <see cref="JobDataMap" /> because a map is what they write, and a
    /// map nested inside a map is exactly a value the reader hands back as something else.
    /// </summary>
    private static bool HasApplicationConverter(Type type, JsonSerializerOptions options)
    {
        foreach (JsonConverter converter in options.Converters)
        {
            if (converter.GetType().Assembly != typeof(JobDataValues).Assembly && converter.CanConvert(type))
            {
                return true;
            }
        }

        return false;
    }
}
