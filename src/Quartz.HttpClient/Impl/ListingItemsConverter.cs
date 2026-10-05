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

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Quartz.Impl;

/// <summary>
/// Reads the items of a listing one at a time, leaving out an item that carries a name this client does
/// not know rather than failing the listing.
/// </summary>
/// <remarks>
/// <para>
/// Registered for the item arrays of the listings whose items have a member that cannot be absent and an
/// enum with no member to read an unknown name as: a trigger's state, a firing's state, a node's state, a
/// misfire's reason, a status's last result. One trigger in a state a newer host added is then one trigger
/// missing from the page, with a line in the log, rather than a page that cannot be shown at all. A total
/// count still counts it, because the host counted it.
/// </para>
/// <para>
/// Only <see cref="UnknownWireNameException" /> leaves an item out. Any other failure fails the listing as
/// it always did: a body that is malformed is not one a newer host meant, and quietly answering an empty
/// page for it would hide a broken contract behind a short one.
/// </para>
/// <para>
/// Each item is read with the generated metadata the options answer for <typeparamref name="T" />, as the
/// array's own converter would read it, so this adds no reflection. The whole array is in the reader's
/// buffer by the time a converter is asked for it, which is what lets an item be read, and on failure read
/// again from its start and skipped.
/// </para>
/// </remarks>
/// <typeparam name="T">The item.</typeparam>
internal sealed class ListingItemsConverter<T> : JsonConverter<T[]>
{
    private readonly UnknownWireNames unknownNames;
    private readonly string item;

    /// <param name="unknownNames">Where an item left out is reported.</param>
    /// <param name="item">What the listing lists, as an operator would say it: <c>"trigger"</c>.</param>
    public ListingItemsConverter(UnknownWireNames unknownNames, string item)
    {
        this.unknownNames = unknownNames ?? throw new ArgumentNullException(nameof(unknownNames));
        this.item = item;
    }

    public override T[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException($"Expected a JSON array of {typeof(T).Name}, found {reader.TokenType}.");
        }

        JsonTypeInfo<T> itemFormat = (JsonTypeInfo<T>) options.GetTypeInfo(typeof(T));
        List<T> items = [];

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            Utf8JsonReader start = reader;
            try
            {
                items.Add(JsonSerializer.Deserialize(ref reader, itemFormat)!);
            }
            catch (UnknownWireNameException unknown)
            {
                unknownNames.ItemLeftOut(item, unknown);

                // Back to the item's first token, then past its last, so the next read starts at the next item.
                reader = start;
                if (!reader.TrySkip())
                {
                    throw;
                }
            }
        }

        return items.ToArray();
    }

    public override void Write(Utf8JsonWriter writer, T[] value, JsonSerializerOptions options)
    {
        JsonTypeInfo<T> itemFormat = (JsonTypeInfo<T>) options.GetTypeInfo(typeof(T));

        writer.WriteStartArray();
        foreach (T one in value)
        {
            JsonSerializer.Serialize(writer, one, itemFormat);
        }

        writer.WriteEndArray();
    }
}
