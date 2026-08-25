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

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

using Quartz.Serialization.SystemTextJson;

namespace Quartz.HttpApiContract;

/// <summary>
/// The single place that decides how the HTTP API's contract renders as JSON, read by the server that
/// writes the wire and by the client that reads it.
/// </summary>
internal static class HttpApiJson
{
    /// <summary>
    /// Teaches <paramref name="options" /> the wire contract: Quartz's trigger, calendar, key and
    /// job-data-map converters, the contract's enums as their names, and the generated metadata for the
    /// contract's own shapes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every enum the API puts on the wire goes out as its name, so that one value has one spelling
    /// everywhere: a trigger body already says <c>"repeatIntervalUnit": "Hour"</c>, written by Quartz's
    /// own converters, and a scheduler or trigger state has no business saying <c>1</c> beside it. Names
    /// also survive an enum gaining members, which ordinals only do by accident.
    /// </para>
    /// <para>
    /// The converters are named per enum type rather than added as one blanket
    /// <see cref="JsonStringEnumConverter" />, because on the server these options belong to the whole
    /// application: a host's own endpoints must keep rendering their own enums the way they always did.
    /// The typed form is also the trimming- and AOT-safe one.
    /// </para>
    /// <para>
    /// <see cref="HttpApiJsonContext" /> goes in front of whatever resolver the options already had, so
    /// a contract type is answered from generated metadata and everything else — the host's own bodies
    /// on the server, the values inside a <see cref="JobDataMap" /> everywhere — still reaches the
    /// resolver behind it. Converters registered above win over both: generated metadata for a type the
    /// options carry a converter for is metadata that defers to that converter, which is what keeps an
    /// <see cref="ITrigger" /> going through <c>TriggerConverter</c> either way.
    /// </para>
    /// </remarks>
    public static JsonSerializerOptions ConfigureWireFormat(
        this JsonSerializerOptions options,
        SystemTextJsonSerializerRegistry registry)
    {
        options.AddQuartzConverters(registry, newtonsoftCompatibilityMode: false);
        options.Converters.Add(new JsonStringEnumConverter<TriggerState>());
        options.Converters.Add(new JsonStringEnumConverter<SchedulerStatus>());
        options.Converters.Add(new JsonStringEnumConverter<FireInstanceState>());
        options.Converters.Add(new JsonStringEnumConverter<ExecutionLimitScope>());

        // Asking twice must leave the chain as asking once does: on the server these options belong to
        // the whole container, and every AddQuartzHttpApi call wants the same contract in front of it.
        IList<IJsonTypeInfoResolver> resolvers = options.TypeInfoResolverChain;
        if (!resolvers.Contains(HttpApiJsonContext.Default))
        {
            if (resolvers.Count == 0)
            {
                // Options carrying no resolver of their own fall back to reflection lazily, but only for
                // as long as the chain stays empty - and putting the contract in it ends that. So the
                // fallback has to be named here, or the values inside a JobDataMap, whose types the
                // contract cannot know, would stop resolving at all.
                DefaultJsonTypeInfoResolver? reflection = ReflectionResolver();
                if (reflection is not null)
                {
                    resolvers.Add(reflection);
                }
            }

            resolvers.Insert(0, HttpApiJsonContext.Default);
        }

        return options;
    }

    /// <summary>
    /// The reflection-based resolver the wire's open half needs, or <see langword="null" /> where
    /// reflection-based serialization is switched off.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The chain has to end in reflection because the contract does: a <see cref="JobDataMap" /> holds
    /// whatever the application put in it, and no generated metadata can describe that. The same guard
    /// <c>Microsoft.AspNetCore.Http.Json.JsonOptions</c> builds its own default resolver behind is what
    /// makes naming <see cref="DefaultJsonTypeInfoResolver" /> here safe: a trimmed publish sets
    /// <c>System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault</c> to false — the SDK does it by
    /// default, as the trim canary's runtimeconfig shows — so the trimmer substitutes the property,
    /// drops this branch and never sees the resolver. What such an application is left with is the
    /// generated contract, which is more than it had: options carrying no resolver at all threw on the
    /// first body either way.
    /// </para>
    /// <para>
    /// A native AOT publish is the same publish: it implies <c>PublishTrimmed</c>, so it sets the same
    /// switch to false and ILCompiler substitutes the same property. That is why the AOT warning is
    /// answered here rather than recorded — the resolver this branch would construct does not exist in
    /// an AOT application to need constructing.
    /// </para>
    /// <para>
    /// The suppressions therefore hide nothing an application is not told. The reflection they silence is
    /// already reported against
    /// <c>Quartz.Serialization.SystemTextJson.Utf8JsonWriterExtensions</c>, which every caller of this
    /// method reaches through <c>JobDataMapConverter</c>, and which is deliberately not suppressed for
    /// consumers — as trimming-unsafe and as AOT-unsafe both.
    /// </para>
    /// </remarks>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Guarded by IsReflectionEnabledByDefault, which a trimmed publish substitutes away along with this branch. See the remarks.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Guarded by IsReflectionEnabledByDefault, which an AOT publish substitutes away along with this branch. See the remarks.")]
    private static DefaultJsonTypeInfoResolver? ReflectionResolver()
    {
        return JsonSerializer.IsReflectionEnabledByDefault ? new DefaultJsonTypeInfoResolver() : null;
    }
}
