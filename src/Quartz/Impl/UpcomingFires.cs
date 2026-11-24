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

using System.Globalization;

using Quartz.Extensibility;

namespace Quartz.Impl;

/// <summary>
/// The one computation of every fire of every trigger in a window, earliest first, which the HTTP API's route,
/// the dashboard agent and the dashboard's client each run: the dashboard's Agenda.
/// </summary>
/// <remarks>
/// <para>
/// It reads through members every scheduler has: <see cref="IScheduler.QueryTriggers" />,
/// <see cref="IScheduler.GetTriggers" />, <see cref="IScheduler.GetCalendar" /> and
/// <see cref="IScheduler.TimeProvider" />. Each trigger's schedule advances on a clone from its stored next fire
/// time, in its own time zone, through its calendar and up to its end time, as
/// <see cref="TriggerFireTimes.ComputeBetween(IOperableTrigger, ICalendar?, DateTimeOffset, DateTimeOffset)" />
/// does, with the window's end excluded.
/// </para>
/// <para>
/// The cost is bounded three ways. Only the triggers due before the window ends are listed, and more than
/// <see cref="MaxTriggers" /> of them is refused. The triggers are read in order of their next fire time and only
/// while one could still fire before the earliest fire not yet listed, so a short answer over many triggers reads
/// few of them. And every walk shares <see cref="StepBudget" /> steps, after which the answer is cut.
/// </para>
/// <para>
/// It lists the schedules' times. It does not simulate what the scheduler would do with them: an overdue trigger's
/// misfire handling, a job that disallows concurrent execution, execution-group limits, standby, or which node
/// fires. An overdue trigger is listed at its schedule's times after the window starts.
/// </para>
/// </remarks>
internal static class UpcomingFires
{
    /// <summary>
    /// The most fires one call answers, and how many it answers when it does not say.
    /// </summary>
    public const int MaxTake = 1000;

    /// <summary>
    /// The most triggers due before a window ends that one call computes.
    /// </summary>
    public const int MaxTriggers = 10_000;

    /// <summary>
    /// How many trigger headers one listing page reads.
    /// </summary>
    public const int HeaderPage = 1_000;

    /// <summary>
    /// How many triggers one read loads.
    /// </summary>
    public const int TriggerBatch = 100;

    /// <summary>
    /// How many schedule steps one call takes in all.
    /// </summary>
    public const long StepBudget = 1_000_000;

    /// <summary>
    /// The longest window: the Timeline's.
    /// </summary>
    public static readonly TimeSpan MaxWindow = TimeSpan.FromDays(7);

    /// <summary>
    /// How far ahead a window may end.
    /// </summary>
    public static readonly TimeSpan Horizon = TimeSpan.FromDays(30);

    /// <summary>
    /// Why <paramref name="query" /> is refused at <paramref name="now" />, or <see langword="null" /> when it is not.
    /// </summary>
    /// <param name="now">The scheduler's current time.</param>
    /// <param name="query">The query.</param>
    /// <param name="maxTake">The most fires the caller allows, at most <see cref="MaxTake" />.</param>
    public static string? Refusal(DateTimeOffset now, UpcomingFiresQuery query, int maxTake = MaxTake)
    {
        if (query.Take < 1 || query.Take > maxTake)
        {
            return $"take must be from 1 to {maxTake}, was {query.Take.ToString(CultureInfo.InvariantCulture)}.";
        }

        if (query.To <= now)
        {
            return $"The window ends at {Readable(query.To)}, which is not after the scheduler's current time, {Readable(now)}. The agenda lists what will fire; History lists what ran.";
        }

        if (query.From is { } from && from >= query.To)
        {
            return $"The window must end after it starts: {Readable(query.To)} is not after {Readable(from)}.";
        }

        if (query.To > now + Horizon)
        {
            return $"The window ends at {Readable(query.To)}, more than {Horizon.TotalDays.ToString(CultureInfo.InvariantCulture)} days after the scheduler's current time, {Readable(now)}.";
        }

        DateTimeOffset start = Start(now, query);
        if (query.To - start > MaxWindow)
        {
            return $"The window from {Readable(start)} to {Readable(query.To)} is longer than {MaxWindow.TotalDays.ToString(CultureInfo.InvariantCulture)} days: narrow it.";
        }

        return null;
    }

    /// <summary>
    /// An instant as a refusal says it, in UTC and to the minute, or to the second when it has seconds:
    /// <c>2026-11-21 10:00 UTC</c>. Every carrier relays the words as they are, so they are read by people.
    /// </summary>
    internal static string Readable(DateTimeOffset value)
    {
        DateTimeOffset utc = value.ToUniversalTime();
        string format = utc.Second == 0 && utc.Millisecond == 0 ? "yyyy-MM-dd HH:mm" : "yyyy-MM-dd HH:mm:ss";
        return utc.ToString(format, CultureInfo.InvariantCulture) + " UTC";
    }

    /// <summary>
    /// The fires, computed in one call where the scheduler is when it can, and otherwise composed here.
    /// </summary>
    public static ValueTask<UpcomingFiresOutcome> Query(IScheduler scheduler, UpcomingFiresQuery query, CancellationToken cancellationToken)
    {
        return scheduler is IUpcomingFiresScheduler computing
            ? computing.QueryUpcomingFires(query, cancellationToken)
            : Compose(scheduler, query, cancellationToken);
    }

    /// <summary>
    /// The fires composed from members every scheduler has, or why the query is refused.
    /// </summary>
    /// <remarks>
    /// What <see cref="Query" /> runs for a scheduler that does not compute them in one call of its own, and what
    /// such a scheduler falls back to when the other end cannot.
    /// </remarks>
    public static async ValueTask<UpcomingFiresOutcome> Compose(IScheduler scheduler, UpcomingFiresQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(query);

        DateTimeOffset now = scheduler.TimeProvider.GetUtcNow();
        if (Refusal(now, query) is { } refusal)
        {
            return new UpcomingFiresOutcome(null, refusal);
        }

        DateTimeOffset from = Start(now, query).ToUniversalTime();
        DateTimeOffset to = query.To.ToUniversalTime();

        List<TriggerHeader>? headers = await DueHeaders(scheduler, query.GroupContains, to, cancellationToken).ConfigureAwait(false);
        if (headers is null)
        {
            return new UpcomingFiresOutcome(null,
                $"More than {MaxTriggers.ToString("N0", CultureInfo.InvariantCulture)} triggers are due before {Readable(to)}: narrow the window or filter by trigger group.");
        }

        Merge merge = new(scheduler, headers, from, to, cancellationToken);
        return new UpcomingFiresOutcome(await merge.Run(query.Take).ConfigureAwait(false), null);
    }

    /// <summary>
    /// The window's start: the query's, or <paramref name="now" /> when it names none or an earlier one.
    /// </summary>
    private static DateTimeOffset Start(DateTimeOffset now, UpcomingFiresQuery query)
    {
        return query.From is { } from && from > now ? from : now;
    }

    /// <summary>
    /// Every trigger due before <paramref name="to" />, in any state, or <see langword="null" /> when there are
    /// more than <see cref="MaxTriggers" />.
    /// </summary>
    /// <remarks>
    /// Paged by group and name, the one order every store has: the store cannot order by next fire time, so the
    /// merge sorts them.
    /// </remarks>
    private static async ValueTask<List<TriggerHeader>?> DueHeaders(IScheduler scheduler, string? groupContains, DateTimeOffset to, CancellationToken cancellationToken)
    {
        GroupMatcher<TriggerKey>? group = string.IsNullOrEmpty(groupContains) ? null : GroupMatcher<TriggerKey>.GroupContains(groupContains);

        List<TriggerHeader> headers = [];
        int skip = 0;
        while (true)
        {
            PagedResult<TriggerHeader> page = await scheduler.QueryTriggers(new TriggerQuery
            {
                Group = group,
                NextFireTimeBefore = to,
                Skip = skip,
                Take = HeaderPage
            }, cancellationToken).ConfigureAwait(false);

            foreach (TriggerHeader header in page.Items)
            {
                if (header.NextFireTimeUtc is not null)
                {
                    headers.Add(header);
                }
            }

            if (headers.Count > MaxTriggers)
            {
                return null;
            }

            if (!page.HasMore || page.Items.Count == 0)
            {
                return headers;
            }

            skip += page.Items.Count;
        }
    }

    /// <summary>
    /// The fires a simple trigger that measures its interval from completion is known to have: its next one, when
    /// that is in the window. Each later run waits for the one before it to complete, which nothing knows ahead.
    /// </summary>
    private static IEnumerable<DateTimeOffset> OnlyTheNext(ITrigger trigger, DateTimeOffset from, DateTimeOffset to)
    {
        if (trigger.NextFireTimeUtc is { } next && next >= from && next < to)
        {
            yield return next;
        }
    }

    /// <summary>
    /// A fire's place in the answer: earliest first, ties by trigger group, then name.
    /// </summary>
    private readonly record struct FireKey(DateTimeOffset Time, string Group, string Name);

    private sealed class FireKeyComparer : IComparer<FireKey>
    {
        public static readonly FireKeyComparer Instance = new();

        public int Compare(FireKey x, FireKey y)
        {
            int byTime = x.Time.UtcTicks.CompareTo(y.Time.UtcTicks);
            if (byTime != 0)
            {
                return byTime;
            }

            int byGroup = string.CompareOrdinal(x.Group, y.Group);
            return byGroup != 0 ? byGroup : string.CompareOrdinal(x.Name, y.Name);
        }
    }

    /// <summary>
    /// One loaded trigger's walk through the window, at its next fire not yet listed.
    /// </summary>
    private sealed record Cursor(TriggerHeader Header, IEnumerator<DateTimeOffset> Times, string? Description, bool OnlyTheNext);

    /// <summary>
    /// The merge of every due trigger's walk, earliest fire first.
    /// </summary>
    /// <remarks>
    /// The headers are sorted by the earliest a trigger can fire in the window: its next fire time, or the window's
    /// start for one that is overdue. A trigger is read only once that bound is not after the earliest fire not yet
    /// listed, so every trigger left unread fires after everything listed.
    /// </remarks>
    private sealed class Merge
    {
        private readonly IScheduler scheduler;
        private readonly List<TriggerHeader> headers;
        private readonly DateTimeOffset from;
        private readonly DateTimeOffset to;
        private readonly CancellationToken cancellationToken;
        private readonly FireTimeStepBudget budget = new(StepBudget);
        private readonly PriorityQueue<Cursor, FireKey> queue = new(FireKeyComparer.Instance);
        private readonly Dictionary<string, ICalendar?> calendars = new(StringComparer.Ordinal);
        private int pending;
        private int notComputed;

        public Merge(IScheduler scheduler, List<TriggerHeader> headers, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
        {
            this.scheduler = scheduler;
            this.headers = headers;
            this.from = from;
            this.to = to;
            this.cancellationToken = cancellationToken;

            headers.Sort((x, y) => FireKeyComparer.Instance.Compare(LowerBound(x), LowerBound(y)));
        }

        public async ValueTask<UpcomingFiresResult> Run(int take)
        {
            List<UpcomingFire> fires = [];
            bool cut = false;
            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    while (pending < headers.Count && !budget.IsSpent && (queue.Count == 0 || IsDue(headers[pending])))
                    {
                        await LoadBatch().ConfigureAwait(false);
                    }

                    if (budget.IsSpent || (queue.Count > 0 && fires.Count >= take))
                    {
                        cut = true;
                        break;
                    }

                    if (!queue.TryDequeue(out Cursor? cursor, out FireKey key))
                    {
                        break;
                    }

                    fires.Add(new UpcomingFire(
                        key.Time,
                        cursor.Header.Key,
                        cursor.Header.JobKey,
                        cursor.Header.State,
                        cursor.Header.ExecutionGroup,
                        cursor.Description,
                        cursor.OnlyTheNext));

                    Enqueue(cursor);
                }
            }
            finally
            {
                while (queue.TryDequeue(out Cursor? left, out _))
                {
                    left.Times.Dispose();
                }
            }

            DateTimeOffset? cutAt = cut ? fires.Count > 0 ? fires[^1].FireTimeUtc : from : null;
            return new UpcomingFiresResult(from, to, fires, cutAt, notComputed);
        }

        private FireKey LowerBound(TriggerHeader header)
        {
            DateTimeOffset next = header.NextFireTimeUtc.GetValueOrDefault();
            return new FireKey(next < from ? from : next, header.Key.Group, header.Key.Name);
        }

        /// <summary>
        /// Whether the trigger could fire at or before the earliest fire not yet listed, so it must be read first.
        /// </summary>
        private bool IsDue(TriggerHeader header)
        {
            queue.TryPeek(out _, out FireKey earliest);
            return FireKeyComparer.Instance.Compare(LowerBound(header), earliest) <= 0;
        }

        /// <summary>
        /// Reads the next <see cref="TriggerBatch" /> triggers in one call and starts the walk of each.
        /// </summary>
        /// <remarks>
        /// A trigger gone since it was listed is passed over: the answer is as of the read. A trigger whose
        /// calendar is missing, or that cannot be advanced, is counted and passed over, so one bad trigger does
        /// not fail the rest.
        /// </remarks>
        private async ValueTask LoadBatch()
        {
            int count = Math.Min(TriggerBatch, headers.Count - pending);
            TriggerKey[] keys = new TriggerKey[count];
            for (int i = 0; i < count; i++)
            {
                keys[i] = headers[pending + i].Key;
            }

            List<ITrigger> triggers = await scheduler.GetTriggers(keys, cancellationToken).ConfigureAwait(false);
            Dictionary<TriggerKey, ITrigger> byKey = new(triggers.Count);
            foreach (ITrigger trigger in triggers)
            {
                byKey.TryAdd(trigger.Key, trigger);
            }

            int first = pending;
            pending += count;

            for (int i = first; i < first + count; i++)
            {
                TriggerHeader header = headers[i];
                if (!byKey.TryGetValue(header.Key, out ITrigger? trigger))
                {
                    continue;
                }

                if (trigger is not IOperableTrigger operable)
                {
                    notComputed++;
                    continue;
                }

                ICalendar? calendar = null;
                if (UpcomingFireTimes.CalendarNameOf(trigger) is { } calendarName)
                {
                    if (!calendars.TryGetValue(calendarName, out calendar))
                    {
                        calendar = await scheduler.GetCalendar(calendarName, cancellationToken).ConfigureAwait(false);
                        calendars[calendarName] = calendar;
                    }

                    if (calendar is null)
                    {
                        notComputed++;
                        continue;
                    }
                }

                bool onlyTheNext = trigger is ISimpleTrigger { MeasureIntervalFromCompletion: true };
                IEnumerable<DateTimeOffset> times = onlyTheNext
                    ? OnlyTheNext(trigger, from, to)
                    : TriggerFireTimes.EnumerateBetween(operable, calendar, from, to, budget);

                Enqueue(new Cursor(header, times.GetEnumerator(), trigger.DescribeSchedule(), onlyTheNext));
                if (budget.IsSpent)
                {
                    return;
                }
            }
        }

        /// <summary>
        /// Moves the walk on to its next fire and queues it there, or ends it.
        /// </summary>
        private void Enqueue(Cursor cursor)
        {
            if (cursor.Times.MoveNext())
            {
                queue.Enqueue(cursor, new FireKey(cursor.Times.Current.ToUniversalTime(), cursor.Header.Key.Group, cursor.Header.Key.Name));
            }
            else
            {
                cursor.Times.Dispose();
            }
        }
    }
}
