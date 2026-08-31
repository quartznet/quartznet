#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Quartz.Serialization.SystemTextJson;

/// <summary>
/// What a job data value may be, in both directions: the set <see cref="Refuse" /> lets past on the
/// way in, and the shapes <see cref="Read" /> hands back on the way out.
/// </summary>
/// <remarks>
/// <para>
/// The two halves live here together because they were apart, and disagreed. The read side has always
/// been closed — a stored value comes back as a string, a bool, an int, a long, a double, null or a
/// <c>Dictionary&lt;string, string&gt;</c>, and there is no polymorphic <see cref="object" />
/// deserialization, which is what keeps a trimmed publish honest. The write side was not closed to
/// match: it wrote whatever the runtime could serialize, so a <c>List&lt;string&gt;</c> went into the
/// database happily and threw on the way back out — by which time the blob was already stored, and
/// unreadable. A writer that consults the same declaration cannot drift from the reader again.
/// </para>
/// <para>
/// <see cref="Accepted" /> is the set <see cref="DataMapExtensions" /> declares an accessor for, plus
/// the one object shape the reader produces. Several of them are not read back as themselves — a
/// <see cref="char" /> is written as a string, a <see cref="decimal" /> as a number — and that is the
/// store format rather than an oversight: the accessors coerce, which is what they are for. Enums are
/// accepted by rule rather than by name, because they are written as their number and read back as
/// one, and no list can enumerate an application's own.
/// </para>
/// <para>
/// Past those, a value is the application's own choice, and
/// <see cref="SystemTextJsonSerializerRegistry.AddTypeInfoResolver" /> is how it declares one. That is
/// the same escape hatch a trimmed publish needs, so a type an application has already answered for is
/// a type this refuses nothing about.
/// </para>
/// <para>
/// The gate lives in <c>JobDataMapConverter</c>, which <c>AddQuartzConverters</c> registers for the
/// store serializer and for the HTTP API alike — so the refusal reaches a job data map on its way into
/// an HTTP response too, and deliberately: the client's reader is this same closed one, so a value the
/// server could not read back is one the client cannot either. That is why the message says "Quartz's
/// JSON format" rather than naming a database, and why the way out it names —
/// <see cref="SystemTextJsonSerializerRegistry.AddTypeInfoResolver" /> — is the right one on both.
/// </para>
/// </remarks>
internal static class JobDataValues
{
    /// <summary>
    /// The property name Json.NET writes a value's type under, and the one name a stored string map
    /// cannot use for an entry of its own.
    /// </summary>
    /// <remarks>
    /// It is metadata rather than data on every path that reads it: <c>Quartz.Serialization.Newtonsoft</c>
    /// consumes it to decide what to build, and this reader drops it (see <see cref="Read" />), so a blob
    /// written before string maps went out plain does not hand a type name back as if the application had
    /// stored one. That makes the name reserved, which is what <see cref="RefuseTypeMarker" /> says at the
    /// one moment anyone can still be told.
    /// </remarks>
    internal const string TypeMarker = "$type";

    /// <summary>
    /// The value types a persistent store's JSON accepts by name. Every one of them is answered by
    /// <see cref="QuartzStoreJsonContext" /> too, so a trimmed publish can write them all;
    /// <c>StoreFormatSourceGenerationTest</c> is what fails when the two stop agreeing.
    /// </summary>
    /// <remarks>
    /// <c>Quartz.Serialization.Newtonsoft</c> refuses against this very field rather than a list of its
    /// own, so the two serializers cannot come to accept different things: a blob written by one has to
    /// be readable by the other, and a second list meaning to say the same thing is how the write and
    /// read sides of *this* one came to disagree.
    /// </remarks>
    internal static readonly FrozenSet<Type> Accepted = FrozenSet.ToFrozenSet(
    [
        // Read back as themselves.
        typeof(string),
        typeof(bool),
        typeof(int),
        typeof(long),
        typeof(double),
        typeof(Dictionary<string, string>),

        // Written as a string or a number, and read back as one; the DataMapExtensions accessors coerce.
        typeof(char),
        typeof(float),
        typeof(decimal),
        typeof(DateTime),
        typeof(DateTimeOffset),
        typeof(TimeSpan),
        typeof(Guid),
        typeof(DateOnly),
        typeof(TimeOnly)
    ]);

    /// <summary>
    /// Throws unless <paramref name="value" /> is one the reader will accept back.
    /// </summary>
    /// <remarks>
    /// The check is by runtime type rather than by trying the write and inspecting what came out,
    /// because refusing has to happen before a single byte reaches the column.
    /// </remarks>
    /// <exception cref="JsonSerializationException">
    /// The value is of a type no reader can turn back into it, and the application has not declared one.
    /// </exception>
    public static void Refuse(string key, object? value, JsonSerializerOptions options, SystemTextJsonSerializerRegistry registry)
    {
        if (value is null)
        {
            return;
        }

        Type type = value.GetType();
        if (Accepted.Contains(type) || type.IsEnum || registry.DeclaresJobDataValueType(type, options))
        {
            RefuseTypeMarker(key, value);
            return;
        }

        throw new JsonSerializationException(
            $"Job data entry '{key}' holds a {type.FullName}, which Quartz's JSON format cannot read back. " +
            "A job data value has to be one of the types JobDataMap declares an accessor for " +
            "(string, bool, char, int, long, float, double, decimal, DateTime, DateTimeOffset, TimeSpan, Guid, DateOnly, TimeOnly or an enum), " +
            "a Dictionary<string, string>, or a type the application declares through SystemTextJsonSerializerRegistry.AddTypeInfoResolver. " +
            "Anything with structure of its own has to be serialized by the job and stored as a string.");
    }

    /// <summary>
    /// Throws when a string map holds an entry named <see cref="TypeMarker" />, which no reader will hand
    /// back as one.
    /// </summary>
    /// <remarks>
    /// The name belongs to Json.NET, which writes a value's type under it and reads it as metadata, so a
    /// map carrying it has never survived a crossing: <c>Quartz.Serialization.Newtonsoft</c> wrote the name
    /// twice and could not read its own blob back, and a map this serializer wrote was one that serializer
    /// could not load at all. Refusing it on the way in is what turns a blob nobody can read into a failure
    /// the application that wrote it hears about — and it is what lets both readers treat the name as
    /// metadata without ever dropping an entry an application meant.
    /// </remarks>
    /// <exception cref="JsonSerializationException">The map holds an entry under the reserved name.</exception>
    public static void RefuseTypeMarker(string key, object value)
    {
        if (value is Dictionary<string, string> stringMap && stringMap.ContainsKey(TypeMarker))
        {
            throw new JsonSerializationException(
                $"Job data entry '{key}' holds a string map with an entry named '{TypeMarker}'. " +
                $"'{TypeMarker}' is the name Json.NET writes a value's type under, so Quartz's JSON format reads it as metadata " +
                "rather than as an entry and no reader can hand it back. Store that entry under a name of its own.");
        }
    }

    /// <summary>
    /// Turns one stored entry back into the value a job reads, which is the whole of what a job data
    /// blob can say.
    /// </summary>
    public static object? Read(JsonElement value, JsonSerializerOptions options)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                return value.GetString();

            case JsonValueKind.True:
                return true;

            case JsonValueKind.False:
                return false;

            case JsonValueKind.Null:
                return null;

            case JsonValueKind.Number:
                if (value.TryGetInt32(out int intValue))
                {
                    return intValue;
                }

                if (value.TryGetInt64(out long longValue))
                {
                    return longValue;
                }

                return value.GetDouble();

            case JsonValueKind.Object:
                // The one shape past the primitives a job data value comes back as, and the reason
                // Dictionary<string, string> is both named in QuartzStoreJsonContext and accepted above.
                Dictionary<string, string>? stringMap = value.Deserialize((JsonTypeInfo<Dictionary<string, string>>) options.GetTypeInfo(typeof(Dictionary<string, string>)));

                // A blob Quartz.Serialization.Newtonsoft wrote before string maps went out plain carries
                // the type it wrote the map under. That is metadata to the reader that wrote it, and it has
                // to be metadata here too: handing it back would put an assembly-qualified type name in the
                // application's own key space, under a name RefuseTypeMarker keeps anything from storing.
                stringMap?.Remove(TypeMarker);
                return stringMap;

            default:
                throw new JsonException($"Unsupported value kind: {value.ValueKind}");
        }
    }
}
