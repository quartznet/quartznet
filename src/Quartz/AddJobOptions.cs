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
/// How a job is added to the scheduler when it is stored without a trigger.
/// </summary>
/// <remarks>
/// Defaults are the conservative ones: nothing is replaced, and a non-durable job is rejected. So
/// <see langword="default"/> — which is what omitting the argument gives — is "add it, change nothing
/// else", and there is no third state between "not given" and "all defaults" for an implementer to
/// have to guess about.
/// </remarks>
/// <seealso cref="IScheduler.AddJob" />
public readonly record struct AddJobOptions
{
    /// <summary>
    /// Over-write an already stored job with the same key. The name for
    /// <c>new AddJobOptions { Replace = true }</c>, which is what nearly every call that passes these
    /// options at all is saying.
    /// </summary>
    public static AddJobOptions Replacing => new() { Replace = true };

    /// <summary>
    /// Store a job that is not durable while it waits for the trigger that will schedule it, and
    /// over-write one already stored under the same key. The name for
    /// <c>new AddJobOptions { Replace = true, StoreNonDurableWhileAwaitingScheduling = true }</c>.
    /// </summary>
    /// <remarks>
    /// The pair is what "put this job in place, I will schedule it in a moment" needs, and the two
    /// flags are only useful together: storing a non-durable job that must not be replaced fails the
    /// second time the same start-up path runs, which is the one thing this shape exists to survive.
    /// </remarks>
    public static AddJobOptions ReplacingAndStoringNonDurable => new() { Replace = true, StoreNonDurableWhileAwaitingScheduling = true };

    /// <summary>
    /// Whether an already stored job with the same key is over-written. When false, storing a job
    /// whose key already exists throws <see cref="ObjectAlreadyExistsException" />.
    /// </summary>
    public bool Replace { get; init; }

    /// <summary>
    /// Whether a job that is not durable may be stored while it is still awaiting a trigger.
    /// Once such a job is scheduled it resumes normal non-durable behaviour, i.e. it is deleted
    /// as soon as it has no remaining triggers.
    /// </summary>
    /// <remarks>
    /// This is a scheduler-level concern only: <see cref="IJobStore.AddJob" /> stores whatever it
    /// is given, and the durability rule is enforced by <see cref="IScheduler" /> above it.
    /// </remarks>
    public bool StoreNonDurableWhileAwaitingScheduling { get; init; }
}
