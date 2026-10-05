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

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// Thrown out of <see cref="IDriverDelegate.ApplyTriggersFired" /> when the writes of a round's fires
/// failed, saying which fire's writes did when that is known.
/// </summary>
/// <remarks>
/// <para>
/// Applied one fire at a time, the failed fire is known, and the store answers it as it answers a failed
/// fire of an acquired trigger (#3931): the round is rolled back and run again without firing that trigger.
/// Sent as one batch, it is not — a batch fails as a unit — and the store rolls the round back and fires
/// its triggers one at a time, which finds it.
/// </para>
/// <para>
/// <see cref="Exception.InnerException" /> is the failure itself, which the store classifies: a transient
/// one is retried whole, as any transient failure in the round is.
/// </para>
/// </remarks>
internal sealed class TriggerWriteFailedException : JobPersistenceException
{
    public TriggerWriteFailedException(int index, Exception failure)
        : base(index < 0
            ? "Writing the fires of a round in one batch failed: " + failure.Message
            : $"Writing fire {index} of a round failed: " + failure.Message, failure)
    {
        Index = index;
    }

    /// <summary>
    /// The position, among the updates the call was given, of the one whose writes failed; <c>-1</c> when
    /// they went as one batch and the failure cannot be pinned on one of them.
    /// </summary>
    public int Index { get; }
}
