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
using System.Text.Json.Serialization.Metadata;

using Quartz.Extensibility;
using Quartz.Serialization.SystemTextJson;

namespace Quartz.Impl;

/// <summary>
/// The default <see cref="IJobInputSerializer" />: a job's input as JSON, written by
/// <see cref="JsonSerializer" />.
/// </summary>
/// <remarks>
/// <para>
/// Built exactly as <see cref="SystemTextJsonObjectSerializer" /> is, so the two agree about what a
/// scheduler's JSON means, and reading and writing go through the <c>GetTypeInfo</c> overloads that
/// carry neither <c>RequiresUnreferencedCode</c> nor <c>RequiresDynamicCode</c> — which is what keeps
/// typed input out of <c>TrimAnalysisBaseline.cs</c> and working in a trimmed or natively compiled
/// application.
/// </para>
/// <para>
/// An application published with reflection-based serialization switched off declares its own payload
/// types through <see cref="SystemTextJsonSerializerRegistry.AddTypeInfoResolver" />, exactly as it
/// declares a job data value type; there is no second registration to learn.
/// </para>
/// </remarks>
public sealed class SystemTextJsonJobInputSerializer : IJobInputSerializer
{
    private readonly SystemTextJsonSerializerRegistry registry;
    private readonly Lock optionsLock = new();
    private volatile JsonSerializerOptions? options;

    /// <summary>
    /// Creates a serializer that knows the built-in types only.
    /// </summary>
    public SystemTextJsonJobInputSerializer()
        : this(new SystemTextJsonSerializerRegistry())
    {
    }

    /// <summary>
    /// Creates a serializer that resolves payload metadata through the given registry, so a scheduler's
    /// own declared types are known to its own serializer and to no other.
    /// </summary>
    public SystemTextJsonJobInputSerializer(SystemTextJsonSerializerRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        this.registry = registry;
    }

    /// <summary>
    /// The options this serializer reads and writes with, built on first use.
    /// </summary>
    private JsonSerializerOptions Options
    {
        get
        {
            JsonSerializerOptions? current = options;
            if (current is not null)
            {
                return current;
            }

            lock (optionsLock)
            {
                return options ??= CreateSerializerOptions();
            }
        }
    }

    private JsonSerializerOptions CreateSerializerOptions()
    {
        JsonSerializerOptions created = new JsonSerializerOptions().AddQuartzConverters(registry, newtonsoftCompatibilityMode: true);
        created.UseQuartzContract(QuartzStoreJsonContext.Default, registry);
        return created;
    }

    /// <summary>
    /// Writes the input as JSON.
    /// </summary>
    /// <remarks>
    /// Written as <see cref="object" />, so the payload describes the runtime type rather than whatever
    /// static type the caller happened to hold — the same choice
    /// <see cref="SystemTextJsonObjectSerializer.Serialize{T}" /> makes, for the same reason.
    /// </remarks>
    public string Serialize(object input)
    {
        ArgumentNullException.ThrowIfNull(input);

        JsonTypeInfo typeInfo = Options.GetTypeInfo(typeof(object));
        return JsonSerializer.Serialize(input, typeInfo);
    }

    /// <summary>
    /// Reads back what <see cref="Serialize" /> wrote.
    /// </summary>
    public TInput? Deserialize<TInput>(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        try
        {
            JsonTypeInfo<TInput> typeInfo = (JsonTypeInfo<TInput>) Options.GetTypeInfo(typeof(TInput));
            return JsonSerializer.Deserialize(payload, typeInfo);
        }
        catch (Exception e) when (e is Quartz.JsonSerializationException or System.Text.Json.JsonException)
        {
            throw new Quartz.JsonSerializationException(
                $"Could not read a job input of type {typeof(TInput)} from JSON: {payload}", e);
        }
    }
}
