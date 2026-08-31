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
/// Selects calendar names.
/// </summary>
public sealed record CalendarQuery : PagedQuery
{
    /// <summary>
    /// Limits the result to calendars whose name matches. Null matches every name.
    /// </summary>
    /// <remarks>
    /// A calendar is identified by a bare name rather than by a <see cref="Key{T}" />, so the filter
    /// is the arity-free <see cref="NameMatcher" /> rather than <see cref="NameMatcher{TKey}" />.
    /// The four comparisons, and the wire spellings they map to, are the same either way.
    /// </remarks>
    public NameMatcher? Name { get; init; }
}
