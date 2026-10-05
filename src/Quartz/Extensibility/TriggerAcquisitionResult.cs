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

namespace Quartz.Extensibility;

/// <summary>
/// What <see cref="IJobStore.AcquireNextTriggersAndFireDue" /> acquired: the triggers it fired as it
/// acquired them, what became of each, and the ones it left for the scheduler to fire when they are due.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Due" /> and <see cref="Fired" /> are index-aligned, as a <see cref="IJobStore.TriggersFired" />
/// batch is with its results, and the scheduler reads each pair the way it reads that batch: it runs a
/// fired bundle, releases a trigger that did not fire or failed to, and leaves a declined one alone.
/// </para>
/// <para>
/// A property left unset reads as an empty list, so a store that fires nothing on acquisition sets
/// <see cref="Pending" /> alone.
/// </para>
/// </remarks>
public sealed class TriggerAcquisitionResult
{
    /// <summary>
    /// The acquired triggers the store fired as it acquired them, in the order it fired them.
    /// </summary>
    public List<IOperableTrigger> Due
    {
        get => field ??= [];
        init;
    }

    /// <summary>
    /// What became of each trigger in <see cref="Due" />, at the same index: what
    /// <see cref="IJobStore.TriggersFired" /> would have answered for it.
    /// </summary>
    public List<TriggerFiredResult> Fired
    {
        get => field ??= [];
        init;
    }

    /// <summary>
    /// The acquired triggers not fired yet, in fire-time order. The scheduler waits for the first of
    /// them and fires them with <see cref="IJobStore.TriggersFired" />, as it does what
    /// <see cref="IJobStore.AcquireNextTriggers" /> returns.
    /// </summary>
    /// <remarks>
    /// Like that list, this one may be one the store keeps: the scheduler copies it before working with it.
    /// </remarks>
    public List<IOperableTrigger> Pending
    {
        get => field ??= [];
        init;
    }
}
