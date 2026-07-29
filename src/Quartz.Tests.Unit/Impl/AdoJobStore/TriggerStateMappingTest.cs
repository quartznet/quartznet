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

using System.Reflection;

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// The property the trigger-state design rests on: a listing's state filter selects exactly the rows that
/// same listing reports as that state. Asserted over the whole matrix rather than per case, so a change to
/// the precedence cannot satisfy one direction and quietly break the other.
/// </summary>
public class TriggerStateMappingTest
{
    /// <summary>
    /// Every state constant the store defines, discovered rather than listed, plus one entirely foreign
    /// value standing in for whatever a third-party delegate, a migration or a hand-repaired row may have
    /// left in the column.
    /// </summary>
    /// <remarks>
    /// Read off <see cref="AdoConstants" /> so that adding a state constant automatically brings it into
    /// this property — a hand-maintained copy would silently stop covering it, which is the same drift the
    /// production mapping derives its filters to avoid.
    /// </remarks>
    private static readonly string[] storedStates =
    [
        .. typeof(AdoConstants)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(x => x.IsLiteral && x.FieldType == typeof(string) && x.Name.StartsWith("State", StringComparison.Ordinal))
            .Select(x => (string) x.GetRawConstantValue()!),
        "SOME_FOREIGN_STATE"
    ];

    [Test]
    public void TheStateConstantsWereActuallyDiscovered()
    {
        // Guards the reflection above: if it silently matched nothing, every property below would pass
        // vacuously over a single foreign value.
        storedStates.Should().Contain(AdoConstants.StateWaiting).And.Contain(AdoConstants.StateExecuting)
            .And.Contain(AdoConstants.StateDeleted).And.HaveCountGreaterThan(8);
    }

    [Test]
    public void EveryFilterSelectsExactlyWhatTheListingReports()
    {
        foreach (TriggerState reported in Enum.GetValues<TriggerState>())
        {
            TriggerStateFilter filter = TriggerStateMapping.ToFilter(reported);

            foreach (string stored in storedStates)
            {
                foreach (bool isExecuting in (bool[]) [false, true])
                {
                    bool selected = Selects(filter, stored, isExecuting);
                    bool reportedAsThis = TriggerStateMapping.ToTriggerState(stored, isExecuting) == reported;

                    selected.Should().Be(reportedAsThis,
                        "a listing filtered by {0} must select TRIGGER_STATE '{1}' (executing: {2}) exactly when it reports it as {0}",
                        reported, stored, isExecuting);
                }
            }
        }
    }

    [Test]
    public void NoneFilterSelectsOnlyTheDeletedSentinel()
    {
        TriggerStateFilter filter = TriggerStateMapping.ToFilter(TriggerState.None);

        // The store never writes DELETED to the column, so in practice this matches no row — but if one
        // carries the value it reports as None, and the filter has to agree rather than leak it into the
        // Normal listing.
        Selects(filter, AdoConstants.StateDeleted, isExecuting: false).Should().BeTrue();
        Selects(filter, AdoConstants.StateWaiting, isExecuting: false).Should().BeFalse();
        Selects(filter, "SOME_FOREIGN_STATE", isExecuting: false).Should().BeFalse();
    }

    [Test]
    public void ToFilterRejectsAnUndefinedState()
    {
        Action act = () => TriggerStateMapping.ToFilter((TriggerState) 99);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// Evaluates a filter the way the generated SQL does: the state list, negated or not, and then the
    /// optional requirement on whether an execution is in flight.
    /// </summary>
    private static bool Selects(TriggerStateFilter filter, string storedState, bool isExecuting)
    {
        bool inList = filter.States.Contains(storedState);
        if (filter.Negated ? inList : !inList)
        {
            return false;
        }

        return filter.Executing is null || filter.Executing.Value == isExecuting;
    }
}
