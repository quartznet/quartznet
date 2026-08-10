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
/// Results are always ordered by group and then name (ordinal), so a page is deterministic
/// on every job store. <see cref="Skip" /> and <see cref="Take" /> are offsets into that
/// ordering; a UI page maps to <c>Skip = (page - 1) * pageSize, Take = pageSize</c>.
/// </remarks>
public abstract record PagedQuery
{
    /// <summary>
    /// The page size a query has when <see cref="Take" /> is not set: 250. One value, defined
    /// in one place — the HTTP API applies it too when a request names no <c>take</c>.
    /// </summary>
    public const int DefaultTake = 250;

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
    /// everything explicitly with <see cref="int.MaxValue" />. Zero is valid and returns no
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
