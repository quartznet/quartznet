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

using Microsoft.Extensions.Time.Testing;

using Quartz.Extensibility;

using Quartz.Tests.Impl;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// What a store does when a trigger misfires: every trigger shape against every misfire instruction of
/// its own family, on the in-memory store and on a real SQLite one, both asserted against the same
/// expectation.
/// </summary>
/// <remarks>
/// <para>
/// The behaviour each instruction names — fire now, reschedule now with the existing count, reschedule
/// to the next slot with the remaining count, do nothing — belongs to the trigger, and
/// <c>UpdateAfterMisfire</c> is where it lives. A store's contribution is to notice the trigger is late,
/// run that method, and record what came out. So each cell here computes what a detached copy of the
/// stored trigger produces, and then asserts that both stores arrived at exactly that; the stores are
/// compared with each other by being compared with one expectation rather than two.
/// </para>
/// <para>
/// <see cref="MisfireInstructionFamilyCases" /> in the unit tests is the other half of this: it is a
/// (stored family × requested family) matrix over the rejection of an instruction phrased in the wrong
/// family's vocabulary. That one is about the name; this one is about what the name does.
/// </para>
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class MisfireMatrixTest : MisfireThroughAStoreTestBase
{
    public static IEnumerable<MisfireMatrixCase> Cases() => MisfireMatrixCases.All();

    /// <summary>
    /// Guards the table itself. The enums are discovered from the assembly rather than listed, so a new
    /// trigger family, or a new instruction inside an existing one, fails here rather than quietly
    /// going untested against either store.
    /// </summary>
    [Test]
    public void EveryInstructionHasACase()
    {
        List<Type> families = MisfireMatrixCases.InstructionEnums().ToList();

        families.Should().NotBeEmpty("the guard is only a guard while it finds the enums it walks");

        foreach (Type family in families)
        {
            Cases()
                .Where(x => x.InstructionEnum == family)
                .Select(x => x.Instruction)
                .Distinct(StringComparer.Ordinal)
                .Should().BeEquivalentTo(Enum.GetNames(family),
                    "the matrix is only a matrix while every {0} value has a cell", family.Name);
        }
    }

    /// <summary>
    /// Guards the matrix against going vacuous, and writes out what each cell resolves to — which is
    /// the table a reader wants when one of the cells below fails.
    /// </summary>
    /// <remarks>
    /// A misfire has exactly three outcomes: the trigger fires as soon as the scheduler gets to it, it
    /// skips to a scheduled slot, or it has nothing left to fire. If a change ever collapsed the matrix
    /// onto one of them, every cell would still pass — each is asserted against the trigger's own
    /// arithmetic — and the matrix would be asserting nothing.
    /// </remarks>
    [Test]
    public void TheMatrixExercisesEveryOutcomeAMisfireCanHave()
    {
        DateTimeOffset anchor = Anchor();
        DateTimeOffset scheduled = anchor - HalfPeriod;

        // Where a store's clock stands when its pass runs. No store is built here; this walk is about
        // what the cells resolve to, not about what a store does with them.
        FakeTimeProvider clock = ClockAt(anchor);

        List<(MisfireMatrixCase Case, MisfireExpectation Expected)> outcomes = [];

        foreach (MisfireMatrixCase testCase in Cases())
        {
            TriggerKey triggerKey = new("outcome", Group);
            JobKey jobKey = new("outcome", Group);

            IOperableTrigger detached = Build(testCase, anchor, clock, triggerKey, jobKey);
            detached.ComputeFirstFireTimeUtc(null);
            detached.NextFireTimeUtc = scheduled;

            MisfireExpectation expected = testCase.IgnoresMisfires
                ? MisfireExpectation.Untouched(scheduled)
                : MisfireExpectation.From(detached, calendar: null, clock);

            outcomes.Add((testCase, expected));
            TestContext.Out.WriteLine($"{testCase} -> {expected}");
        }

        outcomes.Should().Contain(x => x.Expected.FiresNow,
            "the 'fire now' half of the misfire vocabulary has to be exercised by some cell");
        outcomes.Should().Contain(x => !x.Expected.FiresNow && x.Expected.State == TriggerState.Normal,
            "the 'reschedule to a scheduled slot' half has to be exercised by some cell");
        outcomes.Should().Contain(x => x.Expected.State == TriggerState.Complete,
            "a trigger that runs out of fire times while catching up has to be exercised by some cell");
    }

    /// <summary>
    /// The matrix. One trigger, stored with its next fire time pinned half a day in the past; the clock
    /// then moved past the threshold; then exactly one misfire pass; then the trigger's stored state and
    /// next fire time, on both stores, against what its own <c>UpdateAfterMisfire</c> said they should
    /// be.
    /// </summary>
    [TestCaseSource(nameof(Cases))]
    public async Task AMisfirePassLeavesATriggerWhereItsOwnPolicySays(MisfireMatrixCase testCase)
    {
        DateTimeOffset anchor = Anchor();
        DateTimeOffset scheduled = anchor - HalfPeriod;

        foreach (MisfireStoreUnderTest store in await BothStores(anchor))
        {
            TriggerKey triggerKey = new("matrix-" + Guid.NewGuid().ToString("N"), Group);
            JobKey jobKey = new(triggerKey.Name, Group);

            await Store(store, Job(jobKey), Build(testCase, anchor, store.Clock, triggerKey, jobKey), scheduled);

            // The trigger's own scheduled time is where the clock started, so nothing was late until
            // here. Half a day is far past the one-minute threshold, and no wall clock moved for it.
            store.Clock.Advance(HalfPeriod);

            // The copy the expectation comes from goes through the same two steps the stored one did
            // and holds the same clock, so the only difference between them is that this one never met
            // a store.
            IOperableTrigger detached = Build(testCase, anchor, store.Clock, triggerKey, jobKey);
            detached.ComputeFirstFireTimeUtc(null);
            detached.NextFireTimeUtc = scheduled;

            MisfireExpectation expected = testCase.IgnoresMisfires
                ? MisfireExpectation.Untouched(scheduled)
                : MisfireExpectation.From(detached, calendar: null, store.Clock);

            await store.Sweep(scheduled - TimeSpan.FromTicks(1));

            TriggerState state = await store.Store.GetTriggerState(triggerKey);
            IOperableTrigger readBack = await store.Store.GetTrigger(triggerKey);

            readBack.Should().NotBeNull("{0} must still hold '{1}' after a misfire pass", store.Name, testCase);

            expected.AssertAgainst(store.Name, testCase.ToString(), state, readBack.NextFireTimeUtc);
        }
    }

    /// <summary>
    /// A trigger whose instruction is <c>IgnoreMisfirePolicy</c> is not late, however late it is. Both
    /// stores implement that by never running the policy at all — the in-memory one returns from
    /// <c>ApplyMisfireNoLock</c> before it looks at the clock, and the ADO one excludes the row in SQL
    /// with <c>MISFIRE_INSTR &lt;&gt; -1</c> — so this is the one row of the matrix where a store's
    /// answer does not come from <c>UpdateAfterMisfire</c>.
    /// </summary>
    [TestCaseSource(nameof(IgnoringCases))]
    public async Task AnIgnoringTriggerIsLeftExactlyAsItWasStored(MisfireMatrixCase testCase)
    {
        DateTimeOffset anchor = Anchor();
        DateTimeOffset scheduled = anchor - HalfPeriod;

        foreach (MisfireStoreUnderTest store in await BothStores(anchor))
        {
            TriggerKey triggerKey = new("ignoring-" + Guid.NewGuid().ToString("N"), Group);
            JobKey jobKey = new(triggerKey.Name, Group);

            await Store(store, Job(jobKey), Build(testCase, anchor, store.Clock, triggerKey, jobKey), scheduled);
            store.Clock.Advance(HalfPeriod);

            await store.Sweep(scheduled - TimeSpan.FromTicks(1));

            IOperableTrigger readBack = await store.Store.GetTrigger(triggerKey);

            readBack.NextFireTimeUtc.Should().Be(scheduled,
                "{0} must leave '{1}' on the fire time it was stored with; an ignoring trigger catches up "
                + "when it is next acquired, and moving its fire time here would lose the firing it owes",
                store.Name, testCase);
            (await store.Store.GetTriggerState(triggerKey)).Should().Be(TriggerState.Normal,
                "{0} must leave '{1}' waiting", store.Name, testCase);
        }
    }

    public static IEnumerable<MisfireMatrixCase> IgnoringCases() => MisfireMatrixCases.All().Where(x => x.IgnoresMisfires);

    #region The threshold edge

    /// <summary>
    /// Where a store draws the line, to the tick. The threshold instant itself is the interesting one:
    /// both stores' comparison is <c>&lt;=</c> — the in-memory store's <c>ApplyMisfireNoLock</c> declines
    /// only a trigger whose fire time is strictly <em>after</em> <c>now - MisfireThreshold</c>, and the
    /// ADO store's recovery SELECT asks for <c>NEXT_FIRE_TIME &lt;= @nextFireTime</c> — so the instant
    /// itself is a misfire on both.
    /// </summary>
    /// <remarks>
    /// The two flags are kept apart rather than collapsed into one so that
    /// <see cref="BothStoresAgreeOnTheThresholdInstant" /> has two things to compare: a change that moved
    /// one store's comparison and not the other's would have to say so here first.
    /// </remarks>
    public sealed record ThresholdEdgeCase(string Position, long TickOffset, bool InMemoryMisfires, bool AdoMisfires)
    {
        public override string ToString() => Position;
    }

    public static IEnumerable<ThresholdEdgeCase> ThresholdEdgeCases()
    {
        yield return new ThresholdEdgeCase("one tick before the threshold", -1, InMemoryMisfires: true, AdoMisfires: true);
        yield return new ThresholdEdgeCase("exactly on the threshold", 0, InMemoryMisfires: true, AdoMisfires: true);
        yield return new ThresholdEdgeCase("one tick after the threshold", 1, InMemoryMisfires: false, AdoMisfires: false);
    }

    [TestCaseSource(nameof(ThresholdEdgeCases))]
    public async Task TheInMemoryStoreCountsTheThresholdInstantItselfAsLate(ThresholdEdgeCase testCase)
    {
        await AssertThresholdEdge(await InMemoryStore(Anchor()), testCase, testCase.InMemoryMisfires,
            "RAMJobStore misfires a trigger whose fire time is at or before now - MisfireThreshold: "
            + "ApplyMisfireNoLock returns early only when the fire time is strictly greater");
    }

    [TestCaseSource(nameof(ThresholdEdgeCases))]
    public async Task TheAdoStoreCountsTheThresholdInstantItselfAsLate(ThresholdEdgeCase testCase)
    {
        await AssertThresholdEdge(await SqliteStore(Anchor()), testCase, testCase.AdoMisfires,
            "the recovery SELECT asks for NEXT_FIRE_TIME <= now - MisfireThreshold, so the threshold "
            + "instant itself is included");
    }

    /// <summary>
    /// A trigger due at exactly <c>now - MisfireThreshold</c> means the same thing to both stores: it is
    /// late. One comparison, <c>&lt;=</c>, wherever the question is asked — the in-memory store's
    /// <c>ApplyMisfireNoLock</c>, the ADO store's recovery SELECT and its count, and its single-trigger
    /// <c>UpdateMisfiredTrigger</c> path that a resumed trigger goes through.
    /// </summary>
    /// <remarks>
    /// The two stores used to disagree here — the ADO sweep's SELECT was <c>&lt;</c> while everything
    /// else was <c>&lt;=</c>, so the ADO store did not even agree with itself. #3462 closed that.
    /// </remarks>
    [Test]
    public void BothStoresAgreeOnTheThresholdInstant()
    {
        ThresholdEdgeCase edge = ThresholdEdgeCases().Single(x => x.TickOffset == 0);

        edge.AdoMisfires.Should().Be(edge.InMemoryMisfires,
            "a trigger due at exactly now - MisfireThreshold has to mean the same thing to both stores");
        edge.InMemoryMisfires.Should().BeTrue(
            "the one rule is that a trigger is misfired when its fire time is at or before now - MisfireThreshold, "
            + "so the threshold instant itself is late rather than the last instant that is not");
    }

    /// <summary>
    /// Stores a cron trigger due at <c>now - MisfireThreshold + offset</c> on the store's own clock, runs
    /// one pass, and asserts whether the pass moved it.
    /// </summary>
    /// <remarks>
    /// Both stores arrive at the same <c>now - MisfireThreshold</c> to the tick — the in-memory store
    /// subtracts <c>MisfireThreshold.Ticks</c> and the ADO store subtracts
    /// <c>MisfireThreshold.TotalMilliseconds</c>, which are the same instant for a whole number of
    /// milliseconds — so the only thing left to differ is the comparison.
    /// </remarks>
    private async Task AssertThresholdEdge(
        MisfireStoreUnderTest store,
        ThresholdEdgeCase testCase,
        bool shouldMisfire,
        string because)
    {
        DateTimeOffset clockNow = store.Clock.GetUtcNow() + HalfPeriod;
        DateTimeOffset scheduled = clockNow - Threshold + TimeSpan.FromTicks(testCase.TickOffset);

        TriggerKey triggerKey = new("edge-" + Guid.NewGuid().ToString("N"), Group);
        JobKey jobKey = new(triggerKey.Name, Group);

        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .StartAt(clockNow - TimeSpan.FromDays(2))
            .WithCronSchedule(DailyCronAt(clockNow + HalfPeriod), x => x
                .InTimeZone(TimeZoneInfo.Utc)
                .WithMisfireInstruction(CronTriggerMisfireInstruction.DoNothing))
            .Build();

        await Store(store, Job(jobKey), trigger, scheduled);
        store.Clock.Advance(HalfPeriod);

        store.Clock.GetUtcNow().Should().Be(clockNow,
            "the whole case is a tick either side of now - MisfireThreshold, so 'now' has to be the instant the case was built from");

        await store.Sweep(scheduled - TimeSpan.FromTicks(1));

        IOperableTrigger readBack = await store.Store.GetTrigger(triggerKey);

        if (shouldMisfire)
        {
            readBack.NextFireTimeUtc.Should().NotBe(scheduled,
                "{0} treats a trigger due {1} as misfired, because {2}", store.Name, testCase.Position, because);
        }
        else
        {
            readBack.NextFireTimeUtc.Should().Be(scheduled,
                "{0} treats a trigger due {1} as still on time, because {2}", store.Name, testCase.Position, because);
        }
    }

    private static string DailyCronAt(DateTimeOffset when)
    {
        DateTime utc = when.UtcDateTime;
        return string.Create(CultureInfo.InvariantCulture, $"{utc.Second} {utc.Minute} {utc.Hour} * * ?");
    }

    #endregion

    private static IOperableTrigger Build(MisfireMatrixCase testCase, DateTimeOffset anchor, TimeProvider clock, TriggerKey triggerKey, JobKey jobKey)
    {
        return (IOperableTrigger) testCase.Trigger(anchor, clock)
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .Build();
    }
}
