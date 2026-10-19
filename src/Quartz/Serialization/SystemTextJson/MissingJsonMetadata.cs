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

namespace Quartz.Serialization.SystemTextJson;

/// <summary>
/// What every failure over an application type with no System.Text.Json metadata says: the type, and
/// the one registration that answers for it.
/// </summary>
/// <remarks>
/// System.Text.Json's own message names the type and a <c>JsonSerializableAttribute</c>, but not where
/// to hand the context once it exists, which is the half only Quartz can say. The wording lives here so
/// the job data writer and the job input serializer cannot come to name different fixes.
/// </remarks>
internal static class MissingJsonMetadata
{
    /// <summary>
    /// Why the type has no metadata.
    /// </summary>
    private const string Why =
        "this publish serializes nothing by reflection (PublishTrimmed and PublishAot turn System.Text.Json's off)";

    /// <summary>
    /// Where the HTTP API, which no scheduler's builder reaches, takes the same declaration.
    /// </summary>
    internal const string HttpApi =
        "The HTTP API reads the container's SystemTextJsonSerializerRegistry, whose AddTypeInfoResolver takes the same context.";

    /// <summary>
    /// The message for a job input of <paramref name="type" />, which the input serializer can neither
    /// write nor read.
    /// </summary>
    public static string JobInput(Type type)
    {
        return $"Job input type {type.FullName} has no System.Text.Json metadata, and {Why}. {Declare(type)}";
    }

    /// <summary>
    /// The message for a job data entry whose value Quartz accepts but cannot write.
    /// </summary>
    public static string JobDataValue(string key, Type type)
    {
        return $"Job data entry '{key}' holds a {type.FullName}, which has no System.Text.Json metadata, and {Why}. {Declare(type)} {HttpApi}";
    }

    /// <summary>
    /// The fix, naming the type as the attribute has to.
    /// </summary>
    public static string Declare(Type type)
    {
        return $"Declare it with [JsonSerializable(typeof({SourceName(type)}))] on a JsonSerializerContext "
               + "and hand that context to the scheduler with AddJsonTypeInfoResolver(MyContext.Default).";
    }

    /// <summary>
    /// The type as C# source writes it inside <see langword="typeof" />: <c>List&lt;String&gt;</c> rather than
    /// <c>List`1</c>.
    /// </summary>
    internal static string SourceName(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.Name;
        }

        // A type nested in a generic one is generic without an arity of its own, so the split, not an
        // index, is what finds the name.
        string bare = type.Name.Split('`')[0];
        return $"{bare}<{string.Join(", ", type.GetGenericArguments().Select(SourceName))}>";
    }
}
