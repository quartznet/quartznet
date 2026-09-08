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

using Microsoft.Extensions.Configuration;

namespace Quartz;

/// <summary>
/// What to add a scheduler with, beyond its name and the recipe that configures it.
/// </summary>
/// <remarks>
/// <para>
/// The two settings-shaped members are the runtime spellings of the two <c>AddQuartz(name, …)</c>
/// overloads that take settings, so a tenant described by a configuration section at startup is
/// described by the same section when it arrives at runtime instead.
/// </para>
/// <para>
/// Defaults are the conservative ones: no settings beyond what the recipe says, and the scheduler is
/// started — which is the whole of what adding one at runtime is for. So <see langword="default"/> —
/// which is what omitting the argument gives — is "build it from the recipe and run it".
/// </para>
/// </remarks>
/// <seealso cref="ISchedulerRuntime.Add" />
public readonly record struct SchedulerAddOptions
{
    /// <summary>
    /// Create the scheduler and leave starting it to the application. The name for
    /// <c>new SchedulerAddOptions { CreateWithoutStarting = true }</c>.
    /// </summary>
    public static SchedulerAddOptions WithoutStarting => new() { CreateWithoutStarting = true };

    /// <summary>
    /// Flat <c>quartz.*</c> properties for the scheduler, as
    /// <c>AddQuartz(name, properties, …)</c> takes them.
    /// </summary>
    /// <remarks>
    /// Checked against the keys Quartz reads, so a misspelling is reported rather than ignored. Set
    /// <c>quartz.checkConfiguration</c> to <see langword="false" /> to allow keys of your own.
    /// </remarks>
    public IEnumerable<KeyValuePair<string, string?>>? Properties { get; init; }

    /// <summary>
    /// A configuration section describing the scheduler, as <c>AddQuartz(name, configuration, …)</c>
    /// takes it.
    /// </summary>
    /// <remarks>
    /// Either the scheduler's own section or a root section containing <c>Schedulers:{name}</c>; the
    /// same resolution the registration-time overload does. Setting this <em>and</em>
    /// <see cref="Properties" /> is refused rather than resolved by precedence: a section says
    /// everything a property bag does, so one of them would be read and the other dropped without a word.
    /// </remarks>
    public IConfiguration? Configuration { get; init; }

    /// <summary>
    /// Whether the scheduler is left for the application to start, rather than started as soon as it
    /// has been created.
    /// </summary>
    /// <remarks>
    /// This is the whole of the starting policy for a scheduler added at runtime.
    /// <see cref="QuartzHostedServiceOptions.StartDelay" /> and
    /// <see cref="QuartzHostedServiceOptions.AwaitApplicationStarted" /> do not apply: both are about
    /// the window between a host starting and its application being ready, and a scheduler added at
    /// runtime arrives long after both. <see cref="QuartzHostedServiceOptions.WaitForJobsToComplete" />
    /// <em>does</em> apply, because it is about the host stopping, which is still ahead.
    /// </remarks>
    public bool CreateWithoutStarting { get; init; }
}
