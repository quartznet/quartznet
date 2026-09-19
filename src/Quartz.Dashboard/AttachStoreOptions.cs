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
/// How a store attached to the dashboard is watched, beyond the recipe that says how to reach it.
/// </summary>
/// <seealso cref="QuartzDashboardOptions.AttachStore" />
public sealed class AttachStoreOptions
{
    /// <summary>
    /// How often the database is asked again which schedulers are in it. Defaults to one minute;
    /// <see langword="null" /> asks once, at start-up, and never again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The set of schedulers in a shared database is not fixed — a tenant is onboarded, a new service
    /// starts scheduling into the same tables — and a dashboard that asked once would go on showing
    /// whatever was there the minute it started. The query is three indexed reads of one column and
    /// takes no lock, so a minute is cheap; make it longer for a database with many schedulers, or
    /// <see langword="null" /> for one whose set never changes.
    /// </para>
    /// <para>
    /// Rediscovery only ever adds. A scheduler whose rows have been deleted keeps its window, showing
    /// an empty schedule, because taking a page away from an operator who is reading it is worse than
    /// a page that says there is nothing left.
    /// </para>
    /// </remarks>
    public TimeSpan? RediscoveryInterval { get; set; } = TimeSpan.FromMinutes(1);
}
