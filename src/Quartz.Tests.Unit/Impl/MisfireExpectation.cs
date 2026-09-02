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
using Quartz.Impl.Triggers;

namespace Quartz.Tests.Impl;

/// <summary>
/// What a store must leave behind once it has applied a trigger's misfire policy, worked out by
/// running <see cref="IOperableTrigger.UpdateAfterMisfire" /> on a detached copy of the very trigger
/// that was stored.
/// </summary>
/// <remarks>
/// <para>
/// The trigger's own arithmetic is the specification; a store's only job on top of it is the state
/// rule — a trigger left with no fire time is <see cref="TriggerState.Complete" />, anything else is
/// <see cref="TriggerState.Normal" />. Both stores are asserted against one instance of this, which is
/// what makes it a parity assertion rather than two independent ones that happen to agree.
/// </para>
/// <para>
/// Every outcome is an exact instant, "fire now" included. The policies that reschedule to now read
/// the trigger's own clock, and a trigger keeps the clock it was built with — so a detached copy built
/// on the store's clock, which does not move on its own, computes the very instant the store's own copy
/// does. The rule that sorts a "now" from a scheduled instant is
/// <see cref="TriggerBase.FireNowMisfireDetectionThresholdMs" /> — the same one both stores use to
/// decide whether a misfire earned a recorded original fire time.
/// </para>
/// </remarks>
public sealed class MisfireExpectation
{
    private readonly DateTimeOffset? nextFireTimeUtc;
    private readonly bool firesNow;

    private MisfireExpectation(DateTimeOffset? nextFireTimeUtc, bool firesNow)
    {
        this.nextFireTimeUtc = nextFireTimeUtc;
        this.firesNow = firesNow;
    }

    /// <summary>The state a store must report for the trigger afterwards.</summary>
    public TriggerState State => nextFireTimeUtc.HasValue ? TriggerState.Normal : TriggerState.Complete;

    /// <summary>
    /// The expectation for a trigger a store must not have touched at all: it is still waiting, on the
    /// very fire time it was stored with.
    /// </summary>
    public static MisfireExpectation Untouched(DateTimeOffset scheduledFireTimeUtc)
    {
        return new MisfireExpectation(scheduledFireTimeUtc, firesNow: false);
    }

    /// <summary>
    /// Applies the misfire policy to <paramref name="detached" /> — a copy no store has ever seen —
    /// and captures what came out.
    /// </summary>
    /// <param name="detached">The copy to run the policy on. It must hold <paramref name="clock" />.</param>
    /// <param name="calendar">The calendar the policy consults, or <see langword="null" />.</param>
    /// <param name="clock">
    /// The clock the copy reads, which is the store's. Taken rather than read off the trigger so that a
    /// copy accidentally left on some other clock produces a wrong expectation here rather than a
    /// self-consistent one.
    /// </param>
    public static MisfireExpectation From(IOperableTrigger detached, ICalendar calendar, TimeProvider clock)
    {
        DateTimeOffset now = clock.GetUtcNow();

        detached.UpdateAfterMisfire(calendar);

        DateTimeOffset? after = detached.NextFireTimeUtc;
        bool firesNow = after.HasValue
            && Math.Abs((after.Value - now).TotalMilliseconds) < TriggerBase.FireNowMisfireDetectionThresholdMs;

        return new MisfireExpectation(after, firesNow);
    }

    /// <summary>
    /// Asserts that a store's stored state and next fire time are the ones this expectation names.
    /// </summary>
    /// <param name="storeName">The store, as it reads in a failure message.</param>
    /// <param name="cell">What the case under test is, as it reads in a failure message.</param>
    /// <param name="state">The state the store reports.</param>
    /// <param name="actual">The next fire time read back out of the store.</param>
    public void AssertAgainst(
        string storeName,
        string cell,
        TriggerState state,
        DateTimeOffset? actual)
    {
        state.Should().Be(State,
            "{0} must park '{1}' in the state its remaining fire times call for, and UpdateAfterMisfire left it {2}",
            storeName, cell, nextFireTimeUtc.HasValue ? "with one" : "with none");

        if (!nextFireTimeUtc.HasValue)
        {
            actual.Should().BeNull(
                "'{0}' has nothing left to fire after its misfire policy ran, so {1} must not have invented a fire time",
                cell, storeName);
            return;
        }

        if (firesNow)
        {
            actual.Should().NotBeNull("'{0}' reschedules to now, so {1} must have given it a fire time", cell, storeName);
            actual.Should().Be(nextFireTimeUtc,
                "'{0}' reschedules to the store's own reading of now, which is {1} and does not move on its "
                + "own — so {2} must have written that instant and not the machine's",
                cell, Format(nextFireTimeUtc.Value), storeName);
            return;
        }

        actual.Should().Be(nextFireTimeUtc,
            "'{0}' reschedules to a scheduled instant, and a detached copy of the same trigger computed {1} — "
            + "{2} must arrive at the same one",
            cell, Format(nextFireTimeUtc.Value), storeName);
    }

    /// <summary>The instant this expectation names, for a test that wants to reason about it.</summary>
    public DateTimeOffset? NextFireTimeUtc => nextFireTimeUtc;

    /// <summary>Whether the policy rescheduled to "now" rather than to a scheduled instant.</summary>
    public bool FiresNow => firesNow;

    public override string ToString()
    {
        if (!nextFireTimeUtc.HasValue)
        {
            return "complete";
        }

        return firesNow ? "fires now" : "fires at " + Format(nextFireTimeUtc.Value);
    }

    private static string Format(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
}
