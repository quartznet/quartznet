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

using Quartz.Extensibility;

namespace Quartz.Impl.Triggers;

/// <summary>
/// Fires a stored trigger and the caller's copy of it, computing the next fire time once when the two
/// would compute the same one.
/// </summary>
/// <remarks>
/// <para>
/// <c>RAMJobStore.TriggersFired</c> advances two instances of one trigger: its own, and the copy the
/// caller acquired, which goes on into the firing's bundle. Advancing the copy is
/// <see cref="IOperableTrigger.Triggered" /> computed a second time from the same state.
/// </para>
/// <para>
/// The second computation is replaced by a copy of the first only where the result is provably the
/// same. Both instances must be exactly <see cref="CronTriggerImpl" /> — a derived type may override
/// <c>Triggered</c> and do anything in it, so it is called on both, as before. There must be no
/// calendar, because an <see cref="ICalendar" /> is the application's code and is consulted by each
/// call. And every field the computation reads must be equal on the two, to the offset, which is what
/// makes the copied fields exactly the ones the second call would have written. Anything else is fired
/// twice.
/// </para>
/// <para>
/// Cron alone, because it is the one computation worth skipping: <c>TriggerCopyFiringBenchmark</c>
/// reads 88 ns for two cron advances and 48 ns for one and the copy, while a
/// <see cref="SimpleTriggerImpl" /> advances in the time the comparison takes.
/// </para>
/// </remarks>
internal static class TriggerCopyFiring
{
    /// <summary>
    /// Calls <see cref="IOperableTrigger.Triggered" /> on <paramref name="stored" />, and brings
    /// <paramref name="copy" /> to the state the same call would leave it in.
    /// </summary>
    /// <returns>
    /// Whether one computation served both, rather than <see cref="IOperableTrigger.Triggered" /> being
    /// called on each.
    /// </returns>
    public static bool Triggered(IOperableTrigger stored, IOperableTrigger copy, ICalendar? calendar)
    {
        if (calendar is null
            && !ReferenceEquals(stored, copy)
            && stored.GetType() == typeof(CronTriggerImpl)
            && copy.GetType() == typeof(CronTriggerImpl)
            && TryFire((CronTriggerImpl) stored, (CronTriggerImpl) copy))
        {
            return true;
        }

        stored.Triggered(calendar);
        copy.Triggered(calendar);
        return false;
    }

    /// <summary>
    /// <see cref="CronTriggerImpl.Triggered" /> with no calendar moves the next fire time to the previous
    /// one and computes the next from it. The computation reads the start and end times and the cron
    /// expression, which carries the time zone and is immutable, and the clock only when there is no next
    /// fire time.
    /// </summary>
    private static bool TryFire(CronTriggerImpl stored, CronTriggerImpl copy)
    {
        if (stored.NextFireTimeUtc is not { } next
            || !Same(copy.NextFireTimeUtc, next)
            || stored.CronExpression is null
            || !ReferenceEquals(stored.CronExpression, copy.CronExpression)
            || !stored.StartTimeUtc.EqualsExact(copy.StartTimeUtc)
            || !Same(stored.EndTimeUtc, copy.EndTimeUtc))
        {
            return false;
        }

        stored.Triggered(null);

        copy.PreviousFireTimeUtc = stored.PreviousFireTimeUtc;
        copy.NextFireTimeUtc = stored.NextFireTimeUtc;
        return true;
    }

    /// <summary>
    /// Equal to the offset, not only as instants: a fire time computed from a start time carries the start
    /// time's offset, so two instants that compare equal can still produce fire times that do not.
    /// </summary>
    private static bool Same(DateTimeOffset? left, DateTimeOffset? right)
    {
        return left.HasValue == right.HasValue && (!left.HasValue || left.GetValueOrDefault().EqualsExact(right.GetValueOrDefault()));
    }
}
