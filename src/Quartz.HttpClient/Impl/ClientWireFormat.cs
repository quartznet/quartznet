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

using Quartz.HttpApiContract;
using Quartz.Serialization.SystemTextJson;

namespace Quartz.Impl;

/// <summary>
/// The wire format as a client reads it: the server's, plus tolerance for a host newer than the client.
/// </summary>
/// <remarks>
/// <para>
/// From 4.4 a client reads a name it does not know rather than failing on it, so a host can add a member
/// to an enum on the wire without hiding it from 4.4 clients. 4.3 clients predate this and still need the
/// host to hide new names from them, which is why the server is unchanged: its own options stay strict, and
/// a request naming a value the host does not know is still a <c>400</c>.
/// </para>
/// <para>
/// Each enum of the contract is decided here once, and the decisions are the table in the HTTP client's
/// documentation. An enum whose contract keeps a member for a value it cannot name reads an unknown name as
/// that member; any other reads it as <see langword="null" /> where the member may be absent, and otherwise
/// leaves the item it is on out of its listing. A single read of something with no such member still fails,
/// with an exception that says what the host sent.
/// </para>
/// <para>
/// A member the client does not know is skipped, as System.Text.Json does by default. It is set here rather
/// than left to the default, because the options are the caller's and a caller that disallowed unmapped
/// members would otherwise fail on every member a newer host adds.
/// </para>
/// </remarks>
internal static class ClientWireFormat
{
    /// <summary>
    /// Teaches <paramref name="options" /> the wire contract, as
    /// <see cref="HttpApiJson.ConfigureWireFormat" /> does, reading names a newer host may send.
    /// </summary>
    /// <param name="options">The options, which the caller owns and has already copied.</param>
    /// <param name="registry">The trigger and calendar serializers to understand.</param>
    /// <param name="unknownNames">Where a name read as something else, or an item left out, is reported.</param>
    public static JsonSerializerOptions ConfigureClientWireFormat(
        this JsonSerializerOptions options,
        SystemTextJsonSerializerRegistry registry,
        UnknownWireNames unknownNames)
    {
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip;

        // Ahead of the server's converters for the same enums, which ConfigureWireFormat adds below: the
        // first converter that can convert a type is the one that does.

        // "Could not be determined" is a member of the contract, and a state this client cannot name is that.
        options.Converters.Add(new TolerantEnumConverter<SchedulerStatus>(unknownNames, SchedulerStatus.Unknown));

        // As a trigger body already reads an overlap policy it does not know.
        options.Converters.Add(new TolerantEnumConverter<OverlapPolicy>(unknownNames, OverlapPolicy.Default));

        // No member to read an unknown name as: null where the member may be absent, and otherwise the
        // item is left out of its listing by the converters below.
        options.Converters.Add(new TolerantEnumConverter<TriggerState>(unknownNames));
        options.Converters.Add(new TolerantEnumConverter<ContinuationCondition>(unknownNames));
        options.Converters.Add(new TolerantEnumConverter<FireInstanceState>(unknownNames));
        options.Converters.Add(new TolerantEnumConverter<ExecutionLimitScope>(unknownNames));
        options.Converters.Add(new TolerantEnumConverter<ClusterNodeState>(unknownNames));
        options.Converters.Add(new TolerantEnumConverter<SchedulerOrigin>(unknownNames));
        options.Converters.Add(new TolerantEnumConverter<SchedulerEventKind>(unknownNames));
        options.Converters.Add(new TolerantEnumConverter<TriggerConflict>(unknownNames));
        options.Converters.Add(new TolerantEnumConverter<ScheduleOutcome>(unknownNames));
        options.Converters.Add(new TolerantEnumConverter<MisfireReason>(unknownNames));
        options.Converters.Add(new TolerantEnumConverter<JobRunResult>(unknownNames));

        // The listings whose items carry one of those where it cannot be absent.
        options.Converters.Add(new ListingItemsConverter<TriggerHeaderDto>(unknownNames, "trigger"));
        options.Converters.Add(new ListingItemsConverter<FireInstanceDto>(unknownNames, "firing"));
        options.Converters.Add(new ListingItemsConverter<ClusterNodeDto>(unknownNames, "cluster node"));
        options.Converters.Add(new ListingItemsConverter<MisfireHistoryEntryDto>(unknownNames, "misfire"));
        options.Converters.Add(new ListingItemsConverter<JobRunStatusDto>(unknownNames, "job run status"));
        options.Converters.Add(new ListingItemsConverter<SchedulerHeaderDto>(unknownNames, "scheduler"));

        return options.ConfigureWireFormat(registry);
    }
}
