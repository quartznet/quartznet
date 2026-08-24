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
/// What went wrong, and — where the scheduler knew it — what it went wrong for.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ISchedulerListener.SchedulerError" /> used to take a message and an exception, which said
/// what happened but never which trigger or job it happened to. Most of the places that raise the event
/// know: a job that threw is reported from its execution context, and a misfire notification that failed
/// is reported from the trigger that misfired. Those facts arrive here rather than only inside the
/// message text, so a listener can act on them instead of parsing prose.
/// </para>
/// <para>
/// The three keys are optional because some errors genuinely have no subject — a job store retrying a
/// failed operation, or a scan for the next trigger to fire that never got as far as a trigger. A
/// listener should treat a null as "the scheduler could not say", not as "this concerns no trigger".
/// </para>
/// </remarks>
/// <seealso cref="ISchedulerListener.SchedulerError" />
public sealed record SchedulerErrorContext
{
    /// <summary>
    /// A description of what went wrong, written for a human reading a log.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// The error itself.
    /// </summary>
    public required SchedulerException Exception { get; init; }

    /// <summary>
    /// The trigger the error concerns, or <see langword="null" /> when the scheduler could not say.
    /// </summary>
    public TriggerKey? TriggerKey { get; init; }

    /// <summary>
    /// The job the error concerns, or <see langword="null" /> when the scheduler could not say.
    /// </summary>
    public JobKey? JobKey { get; init; }

    /// <summary>
    /// The firing the error concerns, matching <see cref="IJobExecutionContext.FireInstanceId" />, or
    /// <see langword="null" /> when the error happened outside a firing.
    /// </summary>
    public string? FireInstanceId { get; init; }
}
