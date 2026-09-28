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
/// Reads which slot a backfilled firing stands in for.
/// </summary>
/// <remarks>
/// An extension rather than a member of <see cref="IJobExecutionContext" />, so that every hand-written
/// context — a test double, an adapter — keeps compiling.
/// </remarks>
/// <seealso cref="SchedulerBackfillExtensions" />
public static class JobExecutionContextBackfillExtensions
{
    /// <summary>
    /// The fire time of the original trigger this firing was backfilled for, or <see langword="null" /> when
    /// the firing is not a backfill.
    /// </summary>
    /// <remarks>
    /// A job that works on "the period ending at its fire time" reads its period from here when it is set,
    /// because a backfilled firing runs now: <see cref="IJobExecutionContext.ScheduledFireTimeUtc" /> is when
    /// the backfill started it, not the slot. The value is read from the trigger's own
    /// <see cref="JobDataMap" />, under <see cref="SchedulerConstants.BackfillOriginalFireTime" />.
    /// </remarks>
    /// <param name="context">The firing.</param>
    public static DateTimeOffset? GetBackfillSlot(this IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Trigger.JobDataMap.TryGetDateTimeOffset(SchedulerConstants.BackfillOriginalFireTime, out DateTimeOffset slot)
            ? slot
            : null;
    }
}
