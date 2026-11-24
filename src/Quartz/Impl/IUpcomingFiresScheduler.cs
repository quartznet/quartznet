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
/// An <see cref="IScheduler" /> that lists every fire of every trigger in a window in one call where the
/// scheduler is, rather than through the reads <see cref="UpcomingFires.Compose" /> otherwise makes.
/// </summary>
/// <remarks>
/// <para>
/// For a scheduler in another process the composed form is a page of trigger headers per thousand triggers, a
/// read per hundred triggers and one per calendar, and it computes on this side with the triggers and calendars
/// as this process reads them back. A proxy that implements this sends one request, and the host computes with
/// what it stores.
/// </para>
/// <para>
/// The answers are <see cref="UpcomingFires.Compose" />'s, a refusal included. Internal, like
/// <see cref="IUpcomingFireTimesScheduler" />: <c>HttpScheduler</c> is the one implementation,
/// <see cref="DelegatingScheduler" /> forwards it, and <see cref="UpcomingFires.Query" /> is the one caller.
/// </para>
/// </remarks>
internal interface IUpcomingFiresScheduler
{
    /// <summary>
    /// What <see cref="UpcomingFires.Compose" /> answers, computed where the scheduler is.
    /// </summary>
    /// <param name="query">The window, the trigger group filter and the most fires to answer.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<UpcomingFiresOutcome> QueryUpcomingFires(UpcomingFiresQuery query, CancellationToken cancellationToken = default);
}

/// <summary>
/// Which fires to list: those in [<see cref="From" />, <see cref="To" />) of the triggers whose group contains
/// <see cref="GroupContains" />, at most <see cref="Take" /> of them.
/// </summary>
/// <param name="From">The window's start; the scheduler's now when null or earlier.</param>
/// <param name="To">The window's end, excluded.</param>
/// <param name="GroupContains">Only triggers whose group contains this; every trigger when null or empty.</param>
/// <param name="Take">The most fires to answer, from 1 to <see cref="UpcomingFires.MaxTake" />.</param>
internal sealed record UpcomingFiresQuery(DateTimeOffset? From, DateTimeOffset To, string? GroupContains, int Take);

/// <summary>
/// One fire of one trigger, with what a listing shows of the trigger.
/// </summary>
/// <param name="FireTimeUtc">When it fires, in UTC.</param>
/// <param name="TriggerKey">The trigger.</param>
/// <param name="JobKey">The job it fires.</param>
/// <param name="State">The trigger's state when it was read: a paused trigger's fires are listed too.</param>
/// <param name="ExecutionGroup">The trigger's execution group, if any.</param>
/// <param name="ScheduleDescription">
/// The trigger's schedule in words, in its own zone, or <see langword="null" /> for a kind it cannot phrase.
/// </param>
/// <param name="OnlyTheNext">
/// The trigger measures its interval from completion, so only its next fire is known.
/// </param>
internal sealed record UpcomingFire(
    DateTimeOffset FireTimeUtc,
    TriggerKey TriggerKey,
    JobKey JobKey,
    TriggerState State,
    string? ExecutionGroup,
    string? ScheduleDescription,
    bool OnlyTheNext);

/// <summary>
/// The fires in a window, earliest first and ties by trigger group then name, and where the list stops early.
/// </summary>
/// <param name="FromUtc">The window's start used: the query's, or the scheduler's now when that was later.</param>
/// <param name="ToUtc">The window's end, excluded.</param>
/// <param name="Fires">The fires, earliest first.</param>
/// <param name="CutAtUtc">
/// When the list stops early, the time of the last fire it lists, or the window's start when it lists none: fires
/// at or after it may be missing. <see langword="null" /> when it lists every fire in the window.
/// </param>
/// <param name="TriggersNotComputed">
/// Triggers due in the window whose fires could not be computed: a missing calendar, or a type that cannot be advanced.
/// </param>
internal sealed record UpcomingFiresResult(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    List<UpcomingFire> Fires,
    DateTimeOffset? CutAtUtc,
    int TriggersNotComputed);

/// <summary>
/// The fires, or why the query is refused, as <see cref="BackfillOutcome" /> is a backfill's.
/// </summary>
internal readonly record struct UpcomingFiresOutcome(UpcomingFiresResult? Result, string? Refusal);
