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
/// One page of results for a <see cref="PagedQuery" />.
/// </summary>
/// <param name="Items">The items on this page, ordered by group and then name (ordinal).</param>
/// <param name="HasMore">Whether more items match beyond this page. Stores determine this by
/// reading one item past <see cref="PagedQuery.Take" />, so it is exact and costs nothing extra.</param>
/// <param name="TotalCount">The total number of matching items regardless of paging; populated
/// only when the query set <see cref="PagedQuery.IncludeTotalCount" />.</param>
public sealed record PagedResult<T>(List<T> Items, bool HasMore, int? TotalCount = null);
