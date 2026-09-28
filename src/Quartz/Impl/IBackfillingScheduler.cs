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
/// An <see cref="IScheduler" /> that backfills in one call where the scheduler is, rather than through the
/// reads and writes <see cref="SchedulerBackfillExtensions.Backfill" /> otherwise composes.
/// </summary>
/// <remarks>
/// <para>
/// For a scheduler in another process the composed backfill is a request per slot, each audited on the
/// host as a <c>ScheduleJob</c> of its own. A proxy that implements this sends one request instead: the
/// host backfills, its own clock decides what "now" is, and its audit records one backfill.
/// </para>
/// <para>
/// The answers are the extension's: the same <see cref="BackfillResult" />, and a refusal or a missing
/// trigger raised as the same <see cref="ArgumentException" /> or <see cref="ObjectDoesNotExistException" />.
/// Internal, like <see cref="IProxyScheduler" />: <c>HttpScheduler</c> is the one implementation, and
/// the extension is the one caller.
/// </para>
/// </remarks>
internal interface IBackfillingScheduler
{
    /// <summary>
    /// What <see cref="SchedulerBackfillExtensions.Backfill" /> does, done where the scheduler is.
    /// </summary>
    /// <param name="triggerKey">The trigger whose schedule to backfill.</param>
    /// <param name="from">The start of the range, included.</param>
    /// <param name="until">The end of the range, excluded.</param>
    /// <param name="options">How many slots may be scheduled, their spacing and their execution group.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<BackfillResult> Backfill(
        TriggerKey triggerKey,
        DateTimeOffset from,
        DateTimeOffset until,
        BackfillOptions options,
        CancellationToken cancellationToken = default);
}
