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
/// A scheduler that can say whether its next start stores a trigger again, over whatever was changed in it
/// since: the dashboard's warning that an edit to a declared trigger lasts until then (#4412).
/// </summary>
/// <remarks>
/// <para>
/// No <see cref="IScheduler" /> member says it. The scheduler Quartz builds answers from the
/// <see cref="Configuration.DeclaredContent" /> its declarations were recorded in; <c>HttpScheduler</c> asks
/// its host on the trigger-state route, which an older host answers without it.
/// </para>
/// <para>
/// Internal, as <see cref="IFiringWaitingScheduler" /> is: the scheduler Quartz builds, the container's
/// <see cref="IScheduler" />, <see cref="DelegatingScheduler" /> and <c>HttpScheduler</c> implement it, and the
/// HTTP API, the dashboard agent and the dashboard read it through <see cref="StartupDeclarations" />.
/// </para>
/// </remarks>
internal interface IStartupDeclarationsScheduler
{
    /// <summary>
    /// Whether this scheduler's start-up declarations name <paramref name="triggerKey" /> and store it again at
    /// the next start, so that a change made to the stored trigger lasts until then.
    /// </summary>
    /// <param name="triggerKey">The trigger to ask about.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>
    /// <see langword="true" /> when a declaration names the trigger and stores over the stored copy, as
    /// <see cref="SchedulingOptions.OverwriteExistingData" />, the default, does, or names it at all in a store
    /// that keeps nothing across starts; <see langword="false" /> otherwise; <see langword="null" /> when the
    /// scheduler cannot say.
    /// </returns>
    ValueTask<bool?> IsDeclaredAtStartup(TriggerKey triggerKey, CancellationToken cancellationToken = default);
}

/// <summary>
/// Whether a scheduler's start-up declarations store a trigger again at the next start, for any
/// <see cref="IScheduler" />.
/// </summary>
internal static class StartupDeclarations
{
    /// <summary>
    /// What <paramref name="scheduler" /> says about <paramref name="triggerKey" />, or <see langword="null" />
    /// for a scheduler that cannot say.
    /// </summary>
    public static ValueTask<bool?> IsDeclaredAtStartup(IScheduler scheduler, TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(triggerKey);

        return scheduler is IStartupDeclarationsScheduler declarations
            ? declarations.IsDeclaredAtStartup(triggerKey, cancellationToken)
            : new ValueTask<bool?>((bool?) null);
    }
}
