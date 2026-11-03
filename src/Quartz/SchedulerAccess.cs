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
/// How much an operation on a scheduler may change, which is what an operator role grants: every operation
/// the HTTP API, the dashboard and the dashboard agent carry needs one of these three. Added in 4.8.
/// </summary>
/// <remarks>
/// <para>
/// The levels are ordered, and each includes the one below it: an operator may view, and an administrator
/// may operate. The table of which operation needs which level is in the HTTP API's documentation, under
/// "Operator roles", and a test holds it to the routes.
/// </para>
/// <para>
/// Triggering a job now is <see cref="Operate" />, except with a non-empty job-data map, which is
/// <see cref="Administer" />: a stored <c>NativeJob</c> reads the command it runs from its job data, so
/// supplying data is naming what runs. Nothing below <see cref="Administer" /> can name a job type or
/// supply job data.
/// </para>
/// </remarks>
public enum SchedulerAccess
{
    /// <summary>
    /// Every read: the listings, the details, the history, the live events, and the three bulk fetches,
    /// which take a body of keys and change nothing.
    /// </summary>
    View = 0,

    /// <summary>
    /// What changes what a scheduler is doing without changing what it holds: start and stand-by, pausing
    /// and resuming, triggering a job now without job data, interrupting, resetting a trigger from its error
    /// state, and backfilling.
    /// </summary>
    Operate = 1,

    /// <summary>
    /// What changes what a scheduler holds, or ends it: adding, scheduling, rescheduling, editing and
    /// deleting jobs, triggers and calendars, the execution limits, clearing, shutting down, and triggering
    /// a job now with job data.
    /// </summary>
    Administer = 2,
}
