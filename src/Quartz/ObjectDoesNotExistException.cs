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

namespace Quartz;

/// <summary>
/// An exception that is thrown to indicate that an attempt to store an object failed because it names
/// another object the store does not hold — a trigger whose <see cref="ITrigger.Continuation" />
/// waits for a trigger that does not exist.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of <see cref="ObjectAlreadyExistsException" />, and a
/// <see cref="JobPersistenceException" /> for the same reason that one is: the store refused the
/// write, so nothing of it was stored.
/// </para>
/// <para>
/// A continuation of a trigger that does not exist would wait for a firing that can never happen —
/// a parent whose name is misspelled, or a one-shot parent that has already fired and been deleted —
/// and a trigger that waits for ever is one nobody notices. So the store says so when the trigger is
/// stored, rather than holding it in <see cref="TriggerState.Awaiting" /> with nothing to release it.
/// </para>
/// </remarks>
public sealed class ObjectDoesNotExistException : JobPersistenceException
{
    /// <summary>
    /// Creates the exception with the given message. <see cref="TriggerKey" /> and
    /// <see cref="MissingTriggerKey" /> are both <see langword="null" />: nothing here says which
    /// objects were involved.
    /// </summary>
    /// <param name="message">What was refused, and why.</param>
    public ObjectDoesNotExistException(string message) : base(message)
    {
    }

    /// <summary>
    /// Creates the exception for a trigger that could not be stored because the trigger it continues
    /// after does not exist, naming both.
    /// </summary>
    /// <param name="offendingTrigger">The trigger that could not be stored.</param>
    /// <param name="missingTrigger">The trigger it waits for, which the store does not hold.</param>
    public ObjectDoesNotExistException(ITrigger offendingTrigger, TriggerKey missingTrigger)
        : base($"Unable to store Trigger: '{offendingTrigger.Key}', because the trigger it continues after, '{missingTrigger}', does not exist. "
               + "Schedule the continuation while its parent still exists — before a one-shot parent has fired — or check the parent's key.")
    {
        TriggerKey = offendingTrigger.Key;
        MissingTriggerKey = missingTrigger;
    }

    /// <summary>
    /// The trigger that could not be stored, when the exception was raised for one; otherwise
    /// <see langword="null" />.
    /// </summary>
    public TriggerKey? TriggerKey { get; }

    /// <summary>
    /// The trigger it named that the store does not hold, when the exception was raised for one;
    /// otherwise <see langword="null" />. It saves a caller parsing the key back out of
    /// <see cref="Exception.Message" />.
    /// </summary>
    public TriggerKey? MissingTriggerKey { get; }
}
