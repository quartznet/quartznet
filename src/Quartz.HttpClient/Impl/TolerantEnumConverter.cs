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

namespace Quartz.Impl;

/// <summary>
/// Reads an enum of the wire contract as <see cref="JsonStringEnumConverter{TEnum}" /> does, except that a
/// name this client has no member for is not a failure of the whole body.
/// </summary>
/// <remarks>
/// <para>
/// A host newer than this client may send a member added after it was built. What that name becomes is the
/// enum's to say, through the fallback the converter is constructed with:
/// </para>
/// <list type="bullet">
/// <item>with a fallback, the name reads as that member, wherever the enum appears;</item>
/// <item>without one, a nullable member reads as <see langword="null" />;</item>
/// <item>
/// without one, a member that cannot be absent raises <see cref="UnknownWireNameException" />, which the
/// reader of a listing catches to leave that one item out.
/// </item>
/// </list>
/// <para>
/// Everything else is the inner converter's: what is written, and what is read for every name and number it
/// accepts. So a body this client could already read reads the same, and nothing it sends changes. Only a
/// string the inner converter refuses is a name it does not know; a token of the wrong kind still fails as
/// before.
/// </para>
/// <para>
/// A factory over both <typeparamref name="TEnum" /> and its nullable form, because a nullable member is the
/// one place an unknown name can become <see langword="null" />: System.Text.Json's own nullable converter
/// only ever asks the inner one for a value. The typed <see cref="JsonStringEnumConverter{TEnum}" /> is
/// what is wrapped, which is what keeps this as trimming- and AOT-safe as the converter it replaces.
/// </para>
/// </remarks>
/// <typeparam name="TEnum">The enum.</typeparam>
internal sealed class TolerantEnumConverter<TEnum> : JsonConverterFactory where TEnum : struct, Enum
{
    private readonly UnknownWireNames unknownNames;
    private readonly TEnum? fallback;

    /// <param name="unknownNames">Where a name read as something else is reported.</param>
    /// <param name="fallback">
    /// The member an unknown name reads as, for an enum whose contract keeps one for a value it cannot name;
    /// <see langword="null" /> for one that has none.
    /// </param>
    public TolerantEnumConverter(UnknownWireNames unknownNames, TEnum? fallback = null)
    {
        this.unknownNames = unknownNames ?? throw new ArgumentNullException(nameof(unknownNames));
        this.fallback = fallback;
    }

    public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(TEnum) || typeToConvert == typeof(TEnum?);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        JsonConverter<TEnum> names = (JsonConverter<TEnum>) new JsonStringEnumConverter<TEnum>().CreateConverter(typeof(TEnum), options)!;

        return typeToConvert == typeof(TEnum)
            ? new Required(names, this)
            : new Optional(names, this);
    }

    /// <summary>
    /// Reads a value, answering <see langword="false" /> with the name when the name is one this client
    /// does not know.
    /// </summary>
    private static bool TryRead(JsonConverter<TEnum> names, ref Utf8JsonReader reader, JsonSerializerOptions options, out TEnum value, out string? unknown)
    {
        unknown = null;
        if (reader.TokenType != JsonTokenType.String)
        {
            value = names.Read(ref reader, typeof(TEnum), options);
            return true;
        }

        try
        {
            value = names.Read(ref reader, typeof(TEnum), options);
            return true;
        }
        catch (JsonException)
        {
            // The inner converter reads a string token without moving off it, so the name is still here.
            value = default;
            unknown = reader.GetString() ?? "";
            return false;
        }
    }

    /// <summary>
    /// A member that cannot be absent: an unknown name is the fallback, or the end of this item.
    /// </summary>
    private sealed class Required(JsonConverter<TEnum> names, TolerantEnumConverter<TEnum> owner) : JsonConverter<TEnum>
    {
        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (TryRead(names, ref reader, options, out TEnum value, out string? unknown))
            {
                return value;
            }

            if (owner.fallback is { } fallback)
            {
                owner.unknownNames.ReadAs(typeof(TEnum), unknown!, fallback.ToString());
                return fallback;
            }

            throw new UnknownWireNameException(typeof(TEnum), unknown!);
        }

        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
        {
            names.Write(writer, value, options);
        }
    }

    /// <summary>
    /// A nullable member: an unknown name is the fallback, or absent.
    /// </summary>
    private sealed class Optional(JsonConverter<TEnum> names, TolerantEnumConverter<TEnum> owner) : JsonConverter<TEnum?>
    {
        public override TEnum? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (TryRead(names, ref reader, options, out TEnum value, out string? unknown))
            {
                return value;
            }

            TEnum? readAs = owner.fallback;
            owner.unknownNames.ReadAs(typeof(TEnum), unknown!, readAs?.ToString() ?? "null");
            return readAs;
        }

        public override void Write(Utf8JsonWriter writer, TEnum? value, JsonSerializerOptions options)
        {
            // Never reached for null: System.Text.Json writes a null of a nullable value type itself.
            names.Write(writer, value!.Value, options);
        }
    }
}
