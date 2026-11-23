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

namespace Quartz.Impl;

/// <summary>
/// The walk up a continuation's chain of parents that refuses a trigger whose chain leads back to it
/// (#4416).
/// </summary>
/// <remarks>
/// <para>
/// Each trigger of such a loop waits for the next one's firing, so none of them ever fires, and a
/// trigger held awaiting with nothing to release it is one nobody notices: the reason a parent that does
/// not exist is refused too. Both stores walk the chain before they write anything, asking of each key
/// the trigger the call is storing under it, and otherwise the one the store holds
/// (<see cref="ContinuationLink" />).
/// </para>
/// <para>
/// Built only for a trigger that waits for something, so storing one that does not costs nothing. A
/// trigger waits for one parent at most, so the chain is a line and the walk is as long as it is. It
/// ends at a trigger that waits for nothing, at a key nothing is stored under, or at a trigger it has
/// passed already: a loop stored before 4.9 refused one, which does not lead back to this trigger and
/// which the walk leaves rather than going round.
/// </para>
/// </remarks>
internal sealed class ContinuationChainWalk
{
    private readonly TriggerKey start;
    private readonly List<TriggerKey> path;
    private readonly HashSet<TriggerKey> passed;

    /// <summary>
    /// Starts the walk at the trigger being stored and takes it to the parent the trigger waits for.
    /// </summary>
    /// <param name="start">The trigger being stored.</param>
    /// <param name="parent">The trigger it waits for.</param>
    /// <exception cref="JobPersistenceException">When <paramref name="parent" /> is <paramref name="start" />.</exception>
    public ContinuationChainWalk(TriggerKey start, TriggerKey parent)
    {
        this.start = start;
        path = [start];
        passed = [];
        Advance(parent);
    }

    /// <summary>
    /// Takes the walk to the next trigger up the chain.
    /// </summary>
    /// <param name="parent">The trigger the one the walk is at waits for.</param>
    /// <returns>
    /// Whether the walk goes on: <see langword="false" /> when it has passed <paramref name="parent" />
    /// already.
    /// </returns>
    /// <exception cref="JobPersistenceException">
    /// When <paramref name="parent" /> is the trigger being stored, with the loop in the message.
    /// </exception>
    public bool Advance(TriggerKey parent)
    {
        path.Add(parent);
        if (parent.Equals(start))
        {
            Throw.JobPersistenceException(
                $"Unable to store Trigger: '{start}', because it would wait for itself: {string.Join(" -> ", path)}. "
                + "Each trigger of the loop waits for the next one's firing, so none of them would ever fire.");
        }

        return passed.Add(parent);
    }
}
