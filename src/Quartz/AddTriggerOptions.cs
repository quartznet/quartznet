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

namespace Quartz;

/// <summary>
/// How a trigger is stored on its own, without the job it fires.
/// </summary>
/// <remarks>
/// <para>
/// Defaults are the conservative ones: nothing is replaced. So <see langword="default"/> — which is
/// what omitting the argument gives — is "store it, replace nothing", and there is no third state
/// between "not given" and "all defaults" for an implementer to have to guess about.
/// </para>
/// <para>
/// This is a store-level type. <see cref="IScheduler" /> has no <c>AddTrigger</c>: a trigger reaches a
/// scheduler through <see cref="IScheduler.ScheduleJob(ITrigger, ScheduleJobOptions, CancellationToken)" />,
/// which is the same operation named for what it accomplishes. <see cref="IJobStore.AddTrigger" /> is
/// the storage half of it, and this is the storage half's options.
/// </para>
/// </remarks>
/// <seealso cref="IJobStore.AddTrigger" />
public readonly record struct AddTriggerOptions
{
    /// <summary>
    /// Over-write an already stored trigger with the same key. The name for
    /// <c>new AddTriggerOptions { Replace = true }</c>, which is what nearly every call that passes
    /// these options at all is saying.
    /// </summary>
    public static AddTriggerOptions Replacing => new() { Replace = true };

    /// <summary>
    /// Whether an already stored trigger with the same key is over-written. When false, storing a
    /// trigger whose key already exists throws <see cref="ObjectAlreadyExistsException" />.
    /// </summary>
    public bool Replace { get; init; }
}
