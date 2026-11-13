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

namespace Quartz;

/// <summary>
/// Reads which occurrence a firing serves, and why it runs.
/// </summary>
/// <remarks>
/// An extension rather than a member of <see cref="IJobExecutionContext" />, so that every hand-written
/// context — a test double, an adapter — keeps compiling, and reads the answer from what it already has.
/// </remarks>
/// <seealso cref="JobExecutionContextBackfillExtensions" />
public static class JobExecutionContextOccurrenceExtensions
{
    /// <summary>
    /// The occurrence this firing serves, why it runs, and which attempt at it this is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first that applies decides the origin:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <see cref="FiringOrigin.Backfill" /> when <see cref="JobExecutionContextBackfillExtensions.GetBackfillSlot" />
    /// answers a slot. The occurrence is the slot. A backfill of a firing that was itself asked for by hand is a
    /// backfill.
    /// </description></item>
    /// <item><description>
    /// <see cref="FiringOrigin.Manual" /> when the trigger carries <see cref="SchedulerConstants.ManualTrigger" />,
    /// as the one <see cref="IScheduler.TriggerJob" /> stores does. The occurrence is
    /// <see cref="IJobExecutionContext.ScheduledFireTimeUtc" />, which for such a trigger is when the run was
    /// asked for.
    /// </description></item>
    /// <item><description>
    /// <see cref="FiringOrigin.Schedule" /> otherwise. The occurrence is
    /// <see cref="IJobExecutionContext.ScheduledFireTimeUtc" />: the same on every retry, and the missed time
    /// on a misfire catch-up.
    /// </description></item>
    /// </list>
    /// <para>
    /// A recovery keeps the origin and occurrence of the firing it runs again: the recovery trigger carries
    /// that firing's job data, and is scheduled for the time that firing was, so its
    /// <see cref="IJobExecutionContext.ScheduledFireTimeUtc" /> is the original's. One gap: when the firing
    /// recovered was itself a retry or a misfire catch-up, the job store recorded the time that attempt was
    /// due rather than the occurrence, and the recovery serves that time, with
    /// <see cref="FiringOccurrence.RetryAttempt" /> back at <c>0</c>.
    /// </para>
    /// <para>
    /// A firing with no scheduled fire time is taken to be due when it fired.
    /// </para>
    /// <para>
    /// The markers are read from <see cref="IJobExecutionContext.Trigger" />'s own
    /// <see cref="JobDataMap" />, never the merged one, so a job whose own data happened to carry one does
    /// not make every firing of it look manual or backfilled.
    /// </para>
    /// <para>
    /// Two backfills of one slot answer the same, and their triggers share a key, because a backfill names its
    /// trigger after the slot. What tells them apart is <see cref="IJobExecutionContext.ScheduledFireTimeUtc" />:
    /// the time each backfill started its firing, the same on every retry of it.
    /// </para>
    /// </remarks>
    /// <param name="context">The firing.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context" /> is <see langword="null" />.</exception>
    public static FiringOccurrence GetOccurrence(this IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        FiringOrigin origin;
        DateTimeOffset occurrence;
        if (context.GetBackfillSlot() is { } slot)
        {
            origin = FiringOrigin.Backfill;
            occurrence = slot;
        }
        else
        {
            origin = ManualFiring.IsMarked(context.Trigger) ? FiringOrigin.Manual : FiringOrigin.Schedule;
            occurrence = context.ScheduledFireTimeUtc ?? context.FireTimeUtc;
        }

        return new FiringOccurrence(occurrence.ToUniversalTime(), origin)
        {
            RetryAttempt = context.RetryAttempt,
            Recovering = context.Recovering
        };
    }
}
