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
    /// job-data-map converters, plus the contract's enums as their names.
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
    /// </remarks>
    public static JsonSerializerOptions ConfigureWireFormat(
        this JsonSerializerOptions options,
        SystemTextJsonSerializerRegistry registry)
    {
        options.AddQuartzConverters(registry, newtonsoftCompatibilityMode: false);
        options.Converters.Add(new JsonStringEnumConverter<TriggerState>());
        options.Converters.Add(new JsonStringEnumConverter<SchedulerStatus>());
        options.Converters.Add(new JsonStringEnumConverter<FireInstanceState>());
        return options;
    }
}
