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
/// typed input free of trim warnings and working in a trimmed or natively compiled application.
/// </para>
/// <para>
/// An application published with reflection-based serialization switched off declares its own payload
/// types through <see cref="SystemTextJsonConfigurationExtensions.AddJsonTypeInfoResolver" />, the same
/// call that declares its job data value types to the store's serializer; there is no second registration
/// to learn. A payload type nothing describes is refused with a <see cref="NotSupportedException" /> that
/// names it and that call, on the way in and on the way out.
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
    /// <see cref="Options" />, for a test to read the chain a container built and take the reflection
    /// resolver back out of it before first use, which is the chain a trimmed publish leaves behind.
    /// </summary>
    internal JsonSerializerOptions SerializerOptions => Options;

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
    /// <exception cref="NotSupportedException">
    /// Nothing in the resolver chain describes the input's type, which only a publish without reflection
    /// meets. The message names the type and the registration that answers for it.
    /// </exception>
    public string Serialize(object input)
    {
        ArgumentNullException.ThrowIfNull(input);

        JsonSerializerOptions current = Options;
        RequireMetadata(current, input.GetType());

        JsonTypeInfo typeInfo = current.GetTypeInfo(typeof(object));
        return JsonSerializer.Serialize(input, typeInfo);
    }

    /// <summary>
    /// Reads back what <see cref="Serialize" /> wrote.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// Nothing in the resolver chain describes <typeparamref name="TInput" />; see <see cref="Serialize" />.
    /// </exception>
    public TInput? Deserialize<TInput>(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        JsonTypeInfo<TInput> typeInfo = (JsonTypeInfo<TInput>) RequireMetadata(Options, typeof(TInput));

        try
        {
            return JsonSerializer.Deserialize(payload, typeInfo);
        }
        catch (Exception e) when (e is Quartz.JsonSerializationException or System.Text.Json.JsonException)
        {
            throw new Quartz.JsonSerializationException(
                $"Could not read a job input of type {typeof(TInput)} from JSON: {payload}", e);
        }
    }

    /// <summary>
    /// The metadata for <paramref name="type" />, or the failure that says how to provide it.
    /// </summary>
    /// <remarks>
    /// Asked with <see cref="JsonSerializerOptions.TryGetTypeInfo" /> rather than left to the writer,
    /// whose <see cref="NotSupportedException" /> names the type but not where a context is handed in —
    /// and a <see cref="NotSupportedException" /> is still what is thrown, as it was. With reflection on
    /// the answer is always yes.
    /// </remarks>
    private static JsonTypeInfo RequireMetadata(JsonSerializerOptions options, Type type)
    {
        if (options.TryGetTypeInfo(type, out JsonTypeInfo? typeInfo))
        {
            return typeInfo;
        }

        throw new NotSupportedException(MissingJsonMetadata.JobInput(type));
    }
}
