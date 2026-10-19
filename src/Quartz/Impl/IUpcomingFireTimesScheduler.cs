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

namespace Quartz.Impl;

/// <summary>
/// An <see cref="IScheduler" /> that computes a trigger's upcoming fire times in one call where the scheduler
/// is, rather than through the reads <see cref="SchedulerFireTimesExtensions.GetUpcomingFireTimes" /> otherwise
/// composes.
/// </summary>
/// <remarks>
/// <para>
/// For a scheduler in another process the composed form is two requests, and computes on this side with the
/// trigger and calendar as this process reads them back: a calendar of a type it has no serializer for cannot
/// be read at all. A proxy that implements this sends one request, and the host computes with what it stores.
/// </para>
/// <para>
/// The answers are the extension's: the same list, <see langword="null" /> for a trigger that does not exist,
/// and a missing calendar raised as the same <see cref="ObjectDoesNotExistException" />. Internal, like
/// <see cref="IBackfillingScheduler" />: <c>HttpScheduler</c> is the one implementation, and the extension is
/// the one caller.
/// </para>
/// </remarks>
internal interface IUpcomingFireTimesScheduler
{
    /// <summary>
    /// What <see cref="SchedulerFireTimesExtensions.GetUpcomingFireTimes" /> does, done where the scheduler is.
    /// </summary>
    /// <param name="triggerKey">The trigger whose fire times to compute.</param>
    /// <param name="count">How many fire times to answer at most; the extension has checked the range.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<List<DateTimeOffset>?> GetUpcomingFireTimes(TriggerKey triggerKey, int count, CancellationToken cancellationToken = default);
}
