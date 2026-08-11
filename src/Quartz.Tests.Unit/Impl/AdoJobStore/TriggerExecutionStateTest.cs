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
using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// The value-type contract of <see cref="TriggerExecutionState" />: a struct can always be
/// default-constructed, and the type promises that doing so means "no such trigger" rather than null.
/// </summary>
public class TriggerExecutionStateTest
{
    [Test]
    public void DefaultValueReadsAsAMissingTrigger()
    {
        TriggerExecutionState state = default;

        state.State.Should().Be(StoredTriggerState.Deleted,
            "the declared non-nullable State must not surface as null for a default struct");
        state.IsExecuting.Should().BeFalse();
        state.Should().Be(TriggerExecutionState.NotFound);
    }

    [Test]
    public void SpellingOutDeletedEqualsNotFound()
    {
        // The constructor doc offers these as alternatives, so they have to compare equal.
        TriggerExecutionState spelledOut = new(StoredTriggerState.Deleted, isExecuting: false);

        spelledOut.Should().Be(TriggerExecutionState.NotFound);
        (spelledOut == TriggerExecutionState.NotFound).Should().BeTrue();
        spelledOut.GetHashCode().Should().Be(TriggerExecutionState.NotFound.GetHashCode());
    }

    [Test]
    public void CarriesTheStateAndExecutionItWasGiven()
    {
        TriggerExecutionState state = new(StoredTriggerState.Waiting, isExecuting: true);

        state.State.Should().Be(StoredTriggerState.Waiting);
        state.IsExecuting.Should().BeTrue();
        state.Should().NotBe(TriggerExecutionState.NotFound);
    }

    [Test]
    public void ValuesWithTheSameStateAndExecutionAreEqual()
    {
        new TriggerExecutionState(StoredTriggerState.Blocked, isExecuting: true)
            .Should().Be(new TriggerExecutionState(StoredTriggerState.Blocked, isExecuting: true));

        new TriggerExecutionState(StoredTriggerState.Blocked, isExecuting: true)
            .Should().NotBe(new TriggerExecutionState(StoredTriggerState.Blocked, isExecuting: false));
    }
}
