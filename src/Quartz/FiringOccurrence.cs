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
/// Which occurrence a firing serves, and why it runs: what
/// <see cref="JobExecutionContextOccurrenceExtensions.GetOccurrence" /> answers.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="OccurrenceUtc" /> and <see cref="Origin" /> together name the occurrence, and stay the same on
/// every retry of it, on a misfire catch-up of it and on a recovery of it, except a recovery of a retry or a
/// catch-up (see <see cref="JobExecutionContextOccurrenceExtensions.GetOccurrence" />). That is what a consumer
/// that deduplicates per occurrence derives its key from. Quartz formats no key: the format is the consumer's.
/// </para>
/// <para>
/// Two backfills of one slot have the same <see cref="OccurrenceUtc" />, and so does the scheduled firing
/// the slot stands in for, which <see cref="Origin" /> tells apart from them. The two backfills also share
/// <see cref="ITrigger.Key" />; <see cref="IJobExecutionContext.ScheduledFireTimeUtc" />, when each backfill
/// started its firing, tells them apart.
/// </para>
/// </remarks>
/// <param name="OccurrenceUtc">
/// The occurrence, in UTC: the fire time the schedule named for <see cref="FiringOrigin.Schedule" />, when the
/// run was requested for <see cref="FiringOrigin.Manual" />, and the slot for <see cref="FiringOrigin.Backfill" />.
/// </param>
/// <param name="Origin">Why the firing runs.</param>
/// <seealso cref="FiringOrigin" />
public readonly record struct FiringOccurrence(DateTimeOffset OccurrenceUtc, FiringOrigin Origin)
{
    /// <summary>
    /// How many times the occurrence has already been retried: <c>0</c> on its first attempt, <c>n</c> on the
    /// <c>n</c>-th retry. <see cref="IJobExecutionContext.RetryAttempt" />.
    /// </summary>
    public int RetryAttempt { get; init; }

    /// <summary>
    /// Whether the firing runs again an execution a failed or stopped scheduler left behind.
    /// <see cref="IJobExecutionContext.Recovering" />.
    /// </summary>
    public bool Recovering { get; init; }
}
