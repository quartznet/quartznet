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
/// How a calendar is added to the scheduler.
/// </summary>
/// <remarks>
/// Defaults are the conservative ones: nothing is replaced, and no trigger is touched.
/// </remarks>
/// <seealso cref="IScheduler.AddCalendar" />
public sealed record AddCalendarOptions
{
    /// <summary>
    /// Whether an already registered calendar with the same name is over-written. When false,
    /// adding a calendar whose name already exists throws <see cref="ObjectAlreadyExistsException" />.
    /// </summary>
    public bool Replace { get; init; }

    /// <summary>
    /// Whether triggers that reference a calendar of this name have their next fire time
    /// re-computed against the new calendar.
    /// </summary>
    public bool UpdateTriggers { get; init; }
}
