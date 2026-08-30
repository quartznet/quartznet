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
/// Convenience calls over the <see cref="ISchedulerFactory" /> members.
/// </summary>
public static class SchedulerFactoryExtensions
{
    /// <summary>
    /// Finds the scheduler with the given name, throwing when this container has none.
    /// </summary>
    /// <remarks>
    /// <see cref="ISchedulerFactory.LookupScheduler" /> answers <see langword="null" /> for a name it
    /// does not know, and a caller that treats absence as a bug writes the same throw around it every
    /// time. This is that throw. What it deliberately does not do is list the schedulers that
    /// <em>are</em> there: the repository holds the ones that have been built, so a named scheduler
    /// registered but not yet asked for would be missing from that list and the report would be
    /// confidently wrong about what the container holds.
    /// </remarks>
    /// <param name="schedulerFactory">The factory to ask.</param>
    /// <param name="schedulerName">The scheduler's name. Matched ignoring case, as the repository indexes it.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <exception cref="SchedulerNotFoundException">No scheduler of that name is registered in this container.</exception>
    public static async ValueTask<IScheduler> GetRequiredScheduler(
        this ISchedulerFactory schedulerFactory,
        string schedulerName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schedulerFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(schedulerName);

        IScheduler? scheduler = await schedulerFactory.LookupScheduler(schedulerName, cancellationToken).ConfigureAwait(false);
        if (scheduler is not null)
        {
            return scheduler;
        }

        throw new SchedulerNotFoundException(
            schedulerName,
            $"No scheduler named '{schedulerName}' is registered in this container. Register it with "
            + "AddQuartz(\"" + schedulerName + "\", …), or call LookupScheduler if a missing scheduler is "
            + "an answer rather than a failure. Only schedulers from the same container are visible: one "
            + "built by a QuartzSchedulerBuilder of its own is not in this container's repository and is "
            + "reported as absent.");
    }
}
