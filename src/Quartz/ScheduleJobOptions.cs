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
/// How jobs and their triggers are stored when they are scheduled together.
/// </summary>
/// <remarks>
/// Defaults are the conservative ones: nothing is replaced. So <see langword="default"/> — which is
/// what omitting the argument gives — is "store them, replace nothing", and there is no third state
/// between "not given" and "all defaults".
/// <para>
/// This is deliberately not <see cref="AddJobOptions" />. That one also carries
/// <see cref="AddJobOptions.StoreNonDurableWhileAwaitingScheduling" />, which has no meaning here: a
/// trigger is always supplied, so the job is never awaiting scheduling. Its
/// <see cref="AddJobOptions.Replace" /> is about the job alone, where this one covers the job and its
/// triggers together.
/// </para>
/// </remarks>
/// <seealso cref="IScheduler.ScheduleJobs" />
public readonly record struct ScheduleJobOptions
{
    /// <summary>
    /// Whether already stored jobs and triggers with the same keys are over-written. When false,
    /// scheduling a job or trigger whose key already exists throws
    /// <see cref="ObjectAlreadyExistsException" />.
    /// </summary>
    public bool Replace { get; init; }
}
