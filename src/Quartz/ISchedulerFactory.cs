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
/// Provides a mechanism for obtaining client-usable handles to <see cref="IScheduler" />
/// instances.
/// </summary>
/// <seealso cref="IScheduler" />
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
public interface ISchedulerFactory
{
    /// <summary>
    /// Returns handles to every scheduler in the container this factory belongs to.
    /// </summary>
    /// <remarks>
    /// A scheduler is only in the repository of the container that built it, so a factory lists its own
    /// container's schedulers and nobody else's.
    /// </remarks>
    ValueTask<List<IScheduler>> GetAllSchedulers(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a client-usable handle to the <see cref="IScheduler" /> this factory produces, building
    /// it if it has not been built yet.
    /// </summary>
    /// <remarks>
    /// Never returns <see langword="null" />: a factory is configured to produce exactly one scheduler,
    /// so either it can be built or the attempt throws.
    /// </remarks>
    ValueTask<IScheduler> GetScheduler(CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the scheduler with the given name, or <see langword="null" /> when this container has none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a lookup, which is why it is named for one and why it is nullable while
    /// <see cref="GetScheduler(CancellationToken)" /> is not: any name other than this factory's own
    /// belongs to a scheduler somebody else registered, and it may not exist. Asking for this factory's
    /// own scheduler by name builds it on demand, exactly as <see cref="GetScheduler(CancellationToken)" />
    /// would; the comparison ignores case, because that is how the repository indexes names.
    /// </para>
    /// <para>
    /// Only schedulers from the same container are visible. A scheduler built by a
    /// <see cref="QuartzSchedulerBuilder" /> of its own is not in this container's repository and is
    /// reported as absent.
    /// </para>
    /// </remarks>
    ValueTask<IScheduler?> LookupScheduler(string schedulerName, CancellationToken cancellationToken = default);
}