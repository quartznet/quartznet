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
/// Selects jobs, as <see cref="JobHeader" />s.
/// </summary>
public sealed record JobQuery : PagedQuery
{
    /// <summary>
    /// Limits the result to jobs whose group matches. Null matches every group.
    /// </summary>
    public GroupMatcher<JobKey>? Group { get; init; }

    /// <summary>
    /// Limits the result to jobs whose name matches. Null matches every name.
    /// </summary>
    /// <remarks>
    /// Combines with <see cref="Group" /> by AND, so the two together select a name pattern
    /// within a group pattern.
    /// </remarks>
    public NameMatcher<JobKey>? Name { get; init; }
}
