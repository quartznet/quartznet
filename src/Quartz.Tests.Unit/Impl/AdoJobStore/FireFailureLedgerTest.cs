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

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// The per-trigger count of failed fires in a row that a persistent store parks a trigger on (#3963).
/// </summary>
public class FireFailureLedgerTest
{
    private static readonly TriggerKey poison = new("poison", "ledger");
    private static readonly TriggerKey other = new("other", "ledger");
    private static readonly DateTimeOffset previous = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void FailuresOfOneTriggerAreCountedInARowAndApartFromOthers()
    {
        FireFailureLedger ledger = new();
        ledger.IsEmpty.Should().BeTrue("nothing has failed, which is the ordinary batch's answer");

        ledger.RecordFailure(poison, previous).Should().Be(1);
        ledger.RecordFailure(poison, previous).Should().Be(2);
        ledger.RecordFailure(other, null).Should().Be(1, "each trigger has a count of its own");
        ledger.RecordFailure(poison, previous).Should().Be(3);

        ledger.IsEmpty.Should().BeFalse();
    }

    [Test]
    public void ClearingATriggerStartsItsCountAgainAndLeavesTheOthers()
    {
        FireFailureLedger ledger = new();
        ledger.RecordFailure(poison, null);
        ledger.RecordFailure(poison, null);
        ledger.RecordFailure(other, null);

        ledger.Clear(poison);

        ledger.RecordFailure(poison, null).Should().Be(1, "a fire that committed, or a park, ends the run");
        ledger.RecordFailure(other, null).Should().Be(2);
    }

    [Test]
    public void ClearingTheLastTriggerEmptiesTheLedger()
    {
        FireFailureLedger ledger = new();
        ledger.RecordFailure(poison, null);

        ledger.Clear(other);
        ledger.IsEmpty.Should().BeFalse("clearing a trigger with no count clears nothing");

        ledger.Clear(poison);
        ledger.IsEmpty.Should().BeTrue();
        ledger.Clear(poison);
        ledger.IsEmpty.Should().BeTrue("clearing an empty ledger is a no-op");
    }

    /// <summary>
    /// A fire that committed anywhere — on another node, which this ledger never hears of — moves the
    /// trigger's previous fire time, and a rolled-back one never does.
    /// </summary>
    [Test]
    public void APreviousFireTimeThatMovedStartsTheCountAgain()
    {
        FireFailureLedger ledger = new();
        ledger.RecordFailure(poison, null);
        ledger.RecordFailure(poison, null).Should().Be(2);

        ledger.RecordFailure(poison, previous).Should().Be(1, "a fire committed since the last failure");
        ledger.RecordFailure(poison, previous).Should().Be(2);
        ledger.RecordFailure(poison, previous.AddHours(1)).Should().Be(1);
    }
}
