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

using Microsoft.Extensions.Time.Testing;

using Quartz.Extensibility;
using Quartz.Impl.Calendar;
using Quartz.Tests.Impl;

namespace Quartz.Tests.Integration.Impl;

/// <summary>
/// A trigger that misfires onto a day its calendar excludes has to keep going past it. Every trigger
/// family carries that loop in its "reschedule to the next slot" branch; this asserts that the loop is
/// actually reached through a store, which means the store found the calendar, handed it to
/// <c>UpdateAfterMisfire</c>, and stored what came back.
/// </summary>
/// <remarks>
/// The calendar the store hands over is not the one the test holds — the ADO store serializes it into
/// <c>QRTZ_CALENDARS</c> and reads it back — so a calendar whose time zone did not survive the round
/// trip would fail here rather than silently exclude a different day.
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class MisfireCalendarExclusionTest : MisfireThroughAStoreTestBase
{
    private const string CalendarName = "excludes-the-slot-the-misfire-would-pick";

    public static IEnumerable<MisfireMatrixCase> Cases() => MisfireMatrixCases.OnePerShapeThatConsultsACalendar();

    [TestCaseSource(nameof(Cases))]
    public async Task AMisfireSkipsPastTheDayItsCalendarExcludes(MisfireMatrixCase testCase)
    {
        DateTimeOffset anchor = Anchor();
        DateTimeOffset scheduled = anchor - HalfPeriod;

        // Where both stores' clocks stand when their pass runs, so one expectation answers for both.
        FakeTimeProvider clock = ClockAt(anchor);

        // What the policy picks with nothing in its way. The calendar is then built to exclude exactly
        // that day, so the store has to move past it or the assertion below is not about a calendar.
        MisfireExpectation withoutCalendar = MisfireExpectation.From(Detached(testCase, anchor, clock, scheduled), calendar: null, clock);

        withoutCalendar.NextFireTimeUtc.Should().NotBeNull(
            "'{0}' has to have a slot to skip before there is anything for a calendar to exclude", testCase);

        HolidayCalendar calendar = new() { TimeZone = TimeZoneInfo.Utc };
        calendar.AddExcludedDay(DateOnly.FromDateTime(withoutCalendar.NextFireTimeUtc.Value.UtcDateTime));

        MisfireExpectation expected = MisfireExpectation.From(Detached(testCase, anchor, clock, scheduled), calendar, clock);

        expected.NextFireTimeUtc.Should().NotBe(withoutCalendar.NextFireTimeUtc,
            "the calendar excludes the day '{0}' would otherwise land on, so a stored fire time equal to "
            + "the uncalendared one would mean the calendar was never consulted", testCase);

        foreach (MisfireStoreUnderTest store in await BothStores(anchor))
        {
            TriggerKey triggerKey = new("calendar-" + Guid.NewGuid().ToString("N"), Group);
            JobKey jobKey = new(triggerKey.Name, Group);

            await store.Store.AddCalendar(CalendarName, calendar);

            IOperableTrigger trigger = (IOperableTrigger) testCase.Trigger(anchor, store.Clock)
                .WithIdentity(triggerKey)
                .ForJob(jobKey)
                .WithCalendarName(CalendarName)
                .Build();

            await Store(store, Job(jobKey), trigger, scheduled, calendar);
            store.Clock.Advance(HalfPeriod);

            store.Clock.GetUtcNow().Should().Be(clock.GetUtcNow(),
                "the expectation was computed on a clock frozen where this store's now stands, so the two "
                + "have to be the same instant for it to answer for this store");

            await store.Sweep(scheduled - TimeSpan.FromTicks(1));

            TriggerState state = await store.Store.GetTriggerState(triggerKey);
            IOperableTrigger readBack = await store.Store.GetTrigger(triggerKey);

            readBack.Should().NotBeNull("{0} must still hold '{1}' after a misfire pass", store.Name, testCase);

            expected.AssertAgainst(store.Name, testCase + " with an excluded day", state, readBack.NextFireTimeUtc);
        }
    }

    private static IOperableTrigger Detached(MisfireMatrixCase testCase, DateTimeOffset anchor, TimeProvider clock, DateTimeOffset scheduled)
    {
        TriggerKey triggerKey = new("calendar-detached", Group);
        JobKey jobKey = new("calendar-detached", Group);

        IOperableTrigger trigger = (IOperableTrigger) testCase.Trigger(anchor, clock)
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .Build();

        trigger.ComputeFirstFireTimeUtc(null);
        trigger.NextFireTimeUtc = scheduled;

        return trigger;
    }
}
