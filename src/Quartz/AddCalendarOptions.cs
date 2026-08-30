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
/// Defaults are the conservative ones: nothing is replaced, and no trigger is touched. So
/// <see langword="default"/> — which is what omitting the argument gives — is "register it, change
/// nothing else", and there is no third state between "not given" and "all defaults" for an
/// implementer to have to guess about.
/// </remarks>
/// <seealso cref="IScheduler.AddCalendar" />
public readonly record struct AddCalendarOptions
{
    /// <summary>
    /// Over-write an already registered calendar with the same name, leaving the triggers that
    /// reference it alone. The name for <c>new AddCalendarOptions { Replace = true }</c>.
    /// </summary>
    public static AddCalendarOptions Replacing => new() { Replace = true };

    /// <summary>
    /// Over-write an already registered calendar with the same name and re-compute the next fire
    /// time of every trigger that references it. The name for
    /// <c>new AddCalendarOptions { Replace = true, UpdateTriggers = true }</c>, which is what
    /// replacing a calendar whose exclusions have actually moved calls for.
    /// </summary>
    public static AddCalendarOptions ReplacingAndUpdatingTriggers => new() { Replace = true, UpdateTriggers = true };

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
