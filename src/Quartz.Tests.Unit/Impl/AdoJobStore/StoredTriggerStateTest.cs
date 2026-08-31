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

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// The contract the typed trigger state rests on: the enum is a different spelling of the strings the
/// columns already hold, so a scheduler on either side of the change reads what the other wrote.
/// </summary>
public class StoredTriggerStateTest
{
    /// <summary>
    /// Every state constant the store defines, discovered rather than listed, so a constant added without a
    /// matching enum member fails here rather than at some call site.
    /// </summary>
    private static readonly string[] storedValues =
    [
        .. typeof(AdoConstants)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(x => x.IsLiteral && x.FieldType == typeof(string) && x.Name.StartsWith("State", StringComparison.Ordinal))
            .Select(x => (string) x.GetRawConstantValue()!)
    ];

    [Test]
    public void TheStateConstantsWereActuallyDiscovered()
    {
        // Guards the reflection above: matching nothing would make the properties below pass vacuously.
        storedValues.Should().Contain(AdoConstants.StateWaiting).And.HaveCountGreaterThan(8);
    }

    [Test]
    public void EveryStoredValueHasExactlyOneState()
    {
        foreach (string storedValue in storedValues)
        {
            StoredTriggerStates.ToStoredValue(StoredTriggerStates.FromStoredValue(storedValue)).Should().Be(storedValue,
                "the value a state maps to has to be the one it was mapped from, or a stored row changes meaning");
        }
    }

    [Test]
    public void EveryStateHasItsOwnStoredValue()
    {
        StoredTriggerState[] states = Enum.GetValues<StoredTriggerState>();

        states.Select(StoredTriggerStates.ToStoredValue).Should().BeEquivalentTo(storedValues,
            "the enum and the constants are the same set — a member with no constant would write a value no other version understands");
    }

    /// <summary>
    /// A value a third-party delegate, a migration or a hand-repaired row may have left in the column. The
    /// store has always treated one as schedulable, and reading it as a state must not change that.
    /// </summary>
    [Test]
    public void AnUnrecognisedValueReadsAsWaiting()
    {
        StoredTriggerStates.FromStoredValue("SOME_FOREIGN_STATE").Should().Be(StoredTriggerState.Waiting);
    }

    /// <summary>
    /// A read of a missing row has no value to report, which is exactly what the DELETED sentinel means.
    /// </summary>
    [Test]
    public void ANullValueReadsAsDeleted()
    {
        StoredTriggerStates.FromStoredValue(null).Should().Be(StoredTriggerState.Deleted);
    }

    [Test]
    public void WritingAnUndefinedStateFailsLoudly()
    {
        Action act = () => StoredTriggerStates.ToStoredValue((StoredTriggerState) 99);

        act.Should().Throw<ArgumentOutOfRangeException>(
            "writing an unmapped state would put a value in the column that nothing can read back");
    }

    /// <summary>
    /// The default of a struct field or an unassigned local is <c>Waiting</c>, which is the state a trigger
    /// spends most of its life in and the one an unrecognised stored value reads as.
    /// </summary>
    [Test]
    public void TheDefaultIsWaiting()
    {
        default(StoredTriggerState).Should().Be(StoredTriggerState.Waiting);
    }
}
