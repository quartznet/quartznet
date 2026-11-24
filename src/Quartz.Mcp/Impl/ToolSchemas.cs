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

using System.Buffers;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;

namespace Quartz.Mcp.Impl;

/// <summary>
/// A tool's input schema, written by <see cref="JsonSchemaExporter" /> from the generated metadata its arguments are
/// read with, and the reading of those arguments.
/// </summary>
/// <remarks>
/// One source for both, so the schema a client is shown and the reader that holds it to the schema cannot drift:
/// every object says <c>additionalProperties: false</c> and the reader refuses a property it does not name, a
/// required one that is missing and a value of the wrong type, naming the argument in words rather than in .NET
/// types.
/// </remarks>
internal static class ToolSchemas
{
    private static readonly Dictionary<string, string> Descriptions = new(StringComparer.Ordinal)
    {
        ["scheduler"] = "The scheduler's key, as list_schedulers gives it: target/name for one behind a target (w1/QuartzScheduler, prod/reporting), or the bare name of one of this process.",
        ["group"] = "The group half of the key.",
        ["name"] = "The name half of the key.",
        ["groupContains"] = "Only keys whose group contains this text.",
        ["nameContains"] = "Only keys whose name contains this text.",
        ["skip"] = "How many rows to skip, for the next page.",
        ["take"] = $"How many rows to answer: {McpLimits.DefaultTake} when absent, at most {McpLimits.MaxTake}.",
        ["state"] = "Only triggers in this state.",
        ["job"] = "Only rows of this job.",
        ["trigger"] = "Only rows of this trigger.",
        ["nextFireBefore"] = "Only triggers whose next fire time is before this instant.",
        ["results"] = "Only runs with one of these results.",
        ["firedFrom"] = "Only runs fired at or after this instant.",
        ["firedBefore"] = "Only runs fired before this instant.",
        ["failedFinally"] = "true for only the occurrences that gave up after their retries; false for the rest.",
        ["reason"] = $"Why, from {McpLimits.MinReasonLength} to {McpLimits.MaxReasonLength} characters. Written on the trigger with the caller's name and shown to whoever looks at it next.",
        ["fireInstanceId"] = "The running firing to interrupt, as list_executing gives it.",
        ["cronExpression"] = "The new schedule, as a Quartz cron expression with seconds: \"0 0 3 * * ?\" is every day at 03:00.",
        ["timeZone"] = "The time zone the expression is read in, an IANA or Windows id. Absent: the trigger's own when it is a cron trigger, otherwise UTC.",
        ["confirmationToken"] = "The token a preview of this call answered with. Absent, the call changes nothing and answers a preview.",
    };

    /// <summary>
    /// The input schema of a tool whose arguments are <paramref name="typeInfo" />.
    /// </summary>
    public static JsonElement For(JsonTypeInfo typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);

        JsonSchemaExporterOptions options = new()
        {
            TreatNullObliviousAsNonNullable = true,
            TransformSchemaNode = Transform,
        };

        JsonNode schema = typeInfo.GetJsonSchemaAsNode(options);
        using JsonDocument document = JsonDocument.Parse(schema.ToJsonString());
        return document.RootElement.Clone();
    }

    private static JsonNode Transform(JsonSchemaExporterContext context, JsonNode schema)
    {
        if (schema is not JsonObject node)
        {
            return schema;
        }

        if (context.TypeInfo.Kind == JsonTypeInfoKind.Object)
        {
            // What the reader enforces anyway, stated for the client.
            node["additionalProperties"] = false;
        }

        if (context.PropertyInfo is not { } property)
        {
            return node;
        }

        if (Descriptions.TryGetValue(property.Name, out string? description))
        {
            node["description"] = description;
        }

        switch (property.Name)
        {
            case "take":
                node["minimum"] = 1;
                node["maximum"] = McpLimits.MaxTake;
                break;
            case "skip":
                node["minimum"] = 0;
                break;
            case "reason":
                node["minLength"] = McpLimits.MinReasonLength;
                node["maxLength"] = McpLimits.MaxReasonLength;
                break;
        }

        return node;
    }

    /// <summary>
    /// Reads a call's arguments into <typeparamref name="T" />, or says what is wrong with them.
    /// </summary>
    /// <returns>The arguments, or <see langword="null" /> with <paramref name="problem" /> saying why.</returns>
    public static T? Read<T>(IDictionary<string, JsonElement>? arguments, JsonTypeInfo<T> typeInfo, out string? problem) where T : class
    {
        ArgumentNullException.ThrowIfNull(typeInfo);

        problem = null;
        arguments ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        HashSet<string> known = new(StringComparer.Ordinal);
        foreach (JsonPropertyInfo property in typeInfo.Properties)
        {
            known.Add(property.Name);
        }

        foreach (string name in arguments.Keys)
        {
            if (!known.Contains(name))
            {
                problem = known.Count == 0
                    ? $"Unknown argument '{name}': this tool takes none."
                    : $"Unknown argument '{name}'. This tool takes: {string.Join(", ", known.Order(StringComparer.Ordinal))}.";
                return null;
            }
        }

        foreach (JsonPropertyInfo property in typeInfo.Properties)
        {
            if (property.AssociatedParameter is { HasDefaultValue: false } && !arguments.ContainsKey(property.Name))
            {
                problem = $"'{property.Name}' is required.";
                return null;
            }
        }

        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            foreach ((string name, JsonElement value) in arguments)
            {
                writer.WritePropertyName(name);
                value.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        try
        {
            T? read = JsonSerializer.Deserialize(buffer.WrittenSpan, typeInfo);
            if (read is null)
            {
                problem = "The arguments must be an object.";
            }

            return read;
        }
        catch (JsonException exception)
        {
            problem = $"'{ArgumentOf(exception.Path)}' has a value of the wrong type, or is null where a value is required.";
            return null;
        }
    }

    /// <summary>
    /// The argument a reader's path names — <c>$.job.group</c> is <c>job.group</c>.
    /// </summary>
    private static string ArgumentOf(string? path)
    {
        if (string.IsNullOrEmpty(path) || path == "$")
        {
            return "(the arguments)";
        }

        return path.StartsWith("$.", StringComparison.Ordinal) ? path[2..] : path.TrimStart('$');
    }

    /// <summary>
    /// The SHA-256 of what <paramref name="arguments" /> say, written by their own metadata: the same arguments
    /// always give the same digest, whatever order or spacing the client sent them in.
    /// </summary>
    public static string Digest<T>(T arguments, JsonTypeInfo<T> typeInfo)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(arguments, typeInfo);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
    }

    internal static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);
}
