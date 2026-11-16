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

using Quartz.Impl.Triggers;

namespace Quartz.Impl;

/// <summary>
/// An <see cref="IScheduler" /> whose trigger listing arrives with each row's schedule described where the
/// triggers are stored, rather than through the reads <see cref="DescribedTriggerListing.Compose" /> otherwise
/// makes.
/// </summary>
/// <remarks>
/// <para>
/// For a scheduler in another process the composed form is a second request that carries every trigger of the
/// page back whole, data maps and all, to describe them on this side. <c>HttpScheduler</c> sends one request,
/// whose listing its host has already described.
/// </para>
/// <para>
/// Internal, like <see cref="IUpcomingFireTimesScheduler" />: <c>HttpScheduler</c> is the one implementation,
/// and <see cref="DescribedTriggerListing" /> is the one caller. A host older than 4.8 sends no descriptions,
/// and its rows come back without them.
/// </para>
/// </remarks>
internal interface IScheduleDescribingScheduler
{
    /// <summary>
    /// What <see cref="IScheduler.QueryTriggers" /> answers, with each row's schedule in words and the expression
    /// it is written in.
    /// </summary>
    /// <param name="query">Which triggers to list and which page of them.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<PagedResult<DescribedTriggerHeader>> QueryDescribedTriggers(TriggerQuery query, CancellationToken cancellationToken = default);
}

/// <summary>
/// One row of a trigger listing, with its schedule in words.
/// </summary>
/// <param name="Header">The row as <see cref="IScheduler.QueryTriggers" /> answers it.</param>
/// <param name="ScheduleDescription">
/// <see cref="TriggerScheduleDescriptionExtensions.DescribeSchedule" /> of the trigger, or <see langword="null" />
/// when the trigger could not be read, is of a kind that is not described, or was listed by a host that does not
/// describe.
/// </param>
/// <param name="ScheduleExpression">
/// The cron expression or recurrence rule the schedule is written in, or <see langword="null" /> for the kinds
/// built from numbers and on the rows <paramref name="ScheduleDescription" /> is missing from.
/// </param>
internal sealed record DescribedTriggerHeader(TriggerHeader Header, string? ScheduleDescription, string? ScheduleExpression);

/// <summary>
/// The trigger listing with its schedules described: the HTTP API's, the dashboard agent's and the dashboard's.
/// </summary>
internal static class DescribedTriggerListing
{
    /// <summary>
    /// Asks a scheduler that describes its own listing, and composes the description over any other.
    /// </summary>
    public static ValueTask<PagedResult<DescribedTriggerHeader>> Query(IScheduler scheduler, TriggerQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(query);

        return scheduler is IScheduleDescribingScheduler describing
            ? describing.QueryDescribedTriggers(query, cancellationToken)
            : Compose(scheduler, query, cancellationToken);
    }

    /// <summary>
    /// The header page, then the page's triggers in one read, each described.
    /// </summary>
    /// <remarks>
    /// The read is bounded by the page, as a detail page's read of one trigger is by the one. A page whose
    /// triggers cannot be read — a stored trigger of a type this process cannot load, say — is still listed,
    /// without descriptions: the listing was answerable before it was described, and stays so. For the same
    /// reason a trigger whose description throws leaves its own row without one.
    /// </remarks>
    public static async ValueTask<PagedResult<DescribedTriggerHeader>> Compose(IScheduler scheduler, TriggerQuery query, CancellationToken cancellationToken = default)
    {
        PagedResult<TriggerHeader> page = await scheduler.QueryTriggers(query, cancellationToken).ConfigureAwait(false);
        if (page.Items.Count == 0)
        {
            return new PagedResult<DescribedTriggerHeader>([], page.HasMore, page.TotalCount);
        }

        Dictionary<TriggerKey, ITrigger> triggers = [];
        try
        {
            List<ITrigger> read = await scheduler.GetTriggers(page.Items.Select(static x => x.Key).ToList(), cancellationToken).ConfigureAwait(false);
            foreach (ITrigger trigger in read)
            {
                triggers[trigger.Key] = trigger;
            }
        }
        catch (SchedulerException)
        {
            triggers.Clear();
        }

        List<DescribedTriggerHeader> rows = new(page.Items.Count);
        foreach (TriggerHeader header in page.Items)
        {
            rows.Add(Describe(header, triggers.GetValueOrDefault(header.Key)));
        }

        return new PagedResult<DescribedTriggerHeader>(rows, page.HasMore, page.TotalCount);
    }

    /// <summary>
    /// One row, described when its trigger was read and its description does not throw.
    /// </summary>
    /// <remarks>
    /// The shipped triggers never throw here. An <see cref="ITrigger" /> of an application's own can, from any of the
    /// members the description reads, and one such trigger is no reason to fail the page of every other.
    /// </remarks>
    private static DescribedTriggerHeader Describe(TriggerHeader header, ITrigger? trigger)
    {
        if (trigger is not null)
        {
            try
            {
                return new DescribedTriggerHeader(header, ScheduleDescriber.Describe(trigger), ScheduleDescriber.Expression(trigger));
            }
            catch (Exception)
            {
                // Listed without a description, as a row whose trigger could not be read is.
            }
        }

        return new DescribedTriggerHeader(header, ScheduleDescription: null, ScheduleExpression: null);
    }
}
