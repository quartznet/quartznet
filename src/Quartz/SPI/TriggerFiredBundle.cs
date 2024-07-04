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

using Quartz.Core;

namespace Quartz.Spi;

/// <summary>
/// A simple class (structure) used for returning execution-time data from the
/// JobStore to the <see cref="QuartzSchedulerThread" />.
/// </summary>
/// <seealso cref="QuartzScheduler" />
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
[Serializable]
public sealed class TriggerFiredBundle
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TriggerFiredBundle"/> class.
    /// </summary>
    /// <param name="job">The job.</param>
    /// <param name="trigger">The trigger.</param>
    /// <param name="cal">The calendar.</param>
    /// <param name="jobIsRecovering">if set to <c>true</c> [job is recovering].</param>
    /// <param name="fireTimeUtc">The fire time.</param>
    /// <param name="scheduledFireTimeUtc">The scheduled fire time.</param>
    /// <param name="prevFireTimeUtc">The previous fire time.</param>
    /// <param name="nextFireTimeUtc">The next fire time.</param>
    public TriggerFiredBundle(
        IJobDetail job,
        IOperableTrigger trigger,
        ICalendar? cal,
        bool jobIsRecovering,
        DateTimeOffset fireTimeUtc,
        DateTimeOffset? scheduledFireTimeUtc,
        DateTimeOffset? prevFireTimeUtc,
        DateTimeOffset? nextFireTimeUtc)
    {
        JobDetail = job;
        Trigger = trigger;
        Calendar = cal;
        Recovering = jobIsRecovering;
        FireTimeUtc = fireTimeUtc;
        ScheduledFireTimeUtc = scheduledFireTimeUtc;
        PrevFireTimeUtc = prevFireTimeUtc;
        NextFireTimeUtc = nextFireTimeUtc;
    }

    /// <summary>
    /// Gets the job detail.
    /// </summary>
    /// <value>The job detail.</value>
    public IJobDetail JobDetail { get; }

    /// <summary>
    /// Gets the trigger.
    /// </summary>
    /// <value>The trigger.</value>
    public IOperableTrigger Trigger { get; }

    /// <summary>
    /// Gets the calendar.
    /// </summary>
    /// <value>The calendar.</value>
    public ICalendar? Calendar { get; }

    /// <summary>
    /// Gets a value indicating whether this <see cref="TriggerFiredBundle"/> is recovering.
    /// </summary>
    /// <value><c>true</c> if recovering; otherwise, <c>false</c>.</value>
    public bool Recovering { get; }

    /// <returns>
    /// Returns the UTC fire time.
    /// </returns>
    public DateTimeOffset FireTimeUtc { get; }

    /// <summary>
    /// Gets the next UTC fire time.
    /// </summary>
    /// <value>The next fire time.</value>
    /// <returns> Returns the nextFireTimeUtc.</returns>
    public DateTimeOffset? NextFireTimeUtc { get; }

    /// <summary>
    /// Gets the previous UTC fire time.
    /// </summary>
    /// <value>The previous fire time.</value>
    /// <returns> Returns the previous fire time. </returns>
    public DateTimeOffset? PrevFireTimeUtc { get; }

    /// <returns>
    /// Returns the scheduled UTC fire time.
    /// </returns>
    public DateTimeOffset? ScheduledFireTimeUtc { get; }
}