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
/// Which of the application's roles are Quartz's operator roles, for
/// <see cref="SchedulerRolesAuthorizationExtensions.RequireSchedulerRoles" />. Added in 4.8.
/// </summary>
/// <remarks>
/// <para>
/// A role is the application's: a <c>ClaimsPrincipal.IsInRole</c> answer, whatever issued the claim. A
/// caller in <see cref="Administrators" /> may do everything, one in <see cref="Operators" /> may view and
/// operate, and one in <see cref="Viewers" /> may view. A caller in none of them may do nothing at all,
/// reads included, so a role that should only look is still named here.
/// </para>
/// <para>
/// Which operation needs which level is the route table's, and <see cref="Operations" /> moves an operation
/// that changes something to the other level. Triggering a job with job data stays at
/// <see cref="SchedulerAccess.Administer" /> whatever <see cref="Operations" /> says about
/// <c>TriggerJob</c>: a stored <c>NativeJob</c> reads the command it runs from its job data.
/// </para>
/// </remarks>
public sealed class SchedulerRoles
{
    /// <summary>
    /// The roles that may view: every read, the live events and the history.
    /// </summary>
    public List<string> Viewers { get; } = [];

    /// <summary>
    /// The roles that may view and operate: start and stand-by, pause and resume, trigger now without job
    /// data, interrupt, reset from the error state and backfill.
    /// </summary>
    public List<string> Operators { get; } = [];

    /// <summary>
    /// The roles that may do everything: add, schedule, reschedule, edit and delete, the execution limits,
    /// clear, shut down, and trigger now with job data.
    /// </summary>
    public List<string> Administrators { get; } = [];

    /// <summary>
    /// Per-operation overrides of the route table, by the operation's name — <c>AddCalendar</c>,
    /// <c>InterruptJob</c> — ignoring case. Empty by default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only an operation that changes something may be named, and only at
    /// <see cref="SchedulerAccess.Operate" /> or <see cref="SchedulerAccess.Administer" />: a read is every
    /// role's, and a change is never a viewer's. A name the HTTP API does not publish is refused when the
    /// policy is built, since a misspelt override would otherwise grant or withhold nothing in silence.
    /// </para>
    /// <para>
    /// <c>Operations["AddCalendar"] = SchedulerAccess.Operate</c> lets operators maintain calendars;
    /// <c>Operations["InterruptJob"] = SchedulerAccess.Administer</c> keeps interrupting to administrators.
    /// </para>
    /// </remarks>
    public Dictionary<string, SchedulerAccess> Operations { get; } = new(StringComparer.OrdinalIgnoreCase);
}
