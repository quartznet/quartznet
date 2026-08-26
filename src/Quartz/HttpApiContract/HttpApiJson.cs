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
    /// <para>
    /// The registry goes in behind the contract, which is what puts the wire and the store format on the
    /// same footing: a custom trigger or calendar type is answered because
    /// <c>AddTriggerSerializer&lt;TTrigger&gt;</c> knew the type, and an application's own job-data value
    /// types are answered by whatever it handed to
    /// <see cref="SystemTextJsonSerializerRegistry.AddTypeInfoResolver" />. The two formats have the same
    /// open half, so they are assembled by the same method.
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
        options.Converters.Add(new JsonStringEnumConverter<ClusterNodeState>());

        options.UseQuartzContract(HttpApiJsonContext.Default, registry);

        return options;
    }
}
