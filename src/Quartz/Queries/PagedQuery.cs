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

namespace Quartz;

/// <summary>
/// Base for job store queries whose results can be paged.
/// </summary>
/// <remarks>
/// <para>
/// Results are always ordered by group and then name (ordinal), so a page is deterministic
/// on every job store. <see cref="Skip" /> and <see cref="Take" /> are offsets into that
/// ordering; a UI page maps to <c>Skip = (page - 1) * pageSize, Take = pageSize</c>.
/// </para>
/// <para>
/// A filter is named for what it selects on. <c>Group</c> and <c>Name</c> are the result's own
/// identity — <see cref="JobQuery.Name" /> is the job's name, <see cref="JobGroupQuery.Name" /> is
/// the group's — and a filter on something the result merely refers to carries that thing's name:
/// <see cref="TriggerQuery.Job" />, <see cref="TriggerQuery.CalendarName" />,
/// <see cref="FireInstanceQuery.SchedulerInstanceId" />. That is why
/// <see cref="FireInstanceQuery" /> alone spells its trigger filters
/// <see cref="FireInstanceQuery.TriggerGroup" /> and <see cref="FireInstanceQuery.TriggerName" />: a
/// firing is identified by a fire instance id and not by a key, so the trigger it belongs to is a
/// reference like any other, and an unqualified <c>Name</c> there would leave a reader to guess
/// whether it meant the trigger's or the job's.
/// </para>
/// <para>
/// Every name filter is a matcher of the same family: <see cref="NameMatcher{TKey}" /> where the
/// name is half of a <see cref="Key{T}" />, and the arity-free <see cref="NameMatcher" /> where it
/// is a bare name — a calendar's, a group's. Every filter is nullable and null means "match
/// everything", so no filter needs an "any" spelling of its own.
/// </para>
/// </remarks>
public abstract record PagedQuery
{
    /// <summary>
    /// The page size a query has when <see cref="Take" /> is not set: 250. One value, defined
    /// in one place — the HTTP API applies it too when a request names no <c>take</c>.
    /// </summary>
    public const int DefaultTake = 250;

    /// <summary>
    /// The <see cref="Take" /> that asks for everything the filter matches, however many that is.
    /// </summary>
    /// <remarks>
    /// It is <see cref="int.MaxValue" />, which is what the documentation used to tell readers to
    /// type — a magic number for a decision worth stating out loud, and one that reads as an overflow
    /// guard rather than as an intention at a call site. Asking for everything is a real thing to
    /// want (a group-name list, an export, a migration) and a bad default, which is why
    /// <see cref="Take" /> starts at <see cref="DefaultTake" /> and this has to be written.
    /// </remarks>
    public const int All = int.MaxValue;

    /// <summary>
    /// The number of matching items to skip before the first returned item.
    /// </summary>
    public int Skip
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    }

    /// <summary>
    /// The maximum number of items to return. Defaults to <see cref="DefaultTake" /> so an
    /// unpaged call cannot accidentally materialize an unbounded result;
    /// <see cref="PagedResult{T}.HasMore" /> reports whether anything was left out. Ask for
    /// everything explicitly with <see cref="All" />. Zero is valid and returns no
    /// items — combined with <see cref="IncludeTotalCount" /> it turns the query into a count.
    /// </summary>
    public int Take
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    } = DefaultTake;

    /// <summary>
    /// Whether to also compute <see cref="PagedResult{T}.TotalCount" />, the number of items
    /// that match the query regardless of paging. Off by default because it costs a second
    /// query on persistent stores.
    /// </summary>
    public bool IncludeTotalCount { get; init; }
}
