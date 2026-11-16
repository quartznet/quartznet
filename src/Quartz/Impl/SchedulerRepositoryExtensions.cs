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

namespace Quartz.Impl;

/// <summary>
/// Reads of the repository that only this process's own schedulers can answer.
/// </summary>
internal static class SchedulerRepositoryExtensions
{
    /// <summary>
    /// The first scheduler of this process bound under <paramref name="schedulerName" />, skipping every
    /// <see cref="IProxyScheduler" />, or <see langword="null" /> when there is none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="ISchedulerRepository.Lookup" /> without an instance id answers with the first entry under
    /// the name, and a proxy shares the name of the scheduler it fronts. A dashboard agent, an
    /// <c>AddQuartzHttpClient</c> registration or a cluster's merged view can therefore be bound under the
    /// very name this container configured, and whether it or the container's own scheduler comes first
    /// depends on which was bound first (#4322).
    /// </para>
    /// <para>
    /// The readers that mean "the scheduler this container built" — its factory, a restart, the health
    /// check — ask this instead. The bare <see cref="ISchedulerRepository.Lookup" /> is left as it is: the
    /// dashboard, the HTTP API and a lookup by name resolve proxies on purpose.
    /// </para>
    /// </remarks>
    public static IScheduler? LookupLocal(this ISchedulerRepository repository, string schedulerName)
    {
        foreach (IScheduler scheduler in repository.LookupByName(schedulerName))
        {
            if (scheduler is not IProxyScheduler)
            {
                return scheduler;
            }
        }

        return null;
    }
}
