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

namespace Quartz.HttpApiContract;

/// <summary>
/// One scheduler in the listing: a registration, and the scheduler behind it when there is one.
/// </summary>
/// <remarks>
/// <see cref="Status" /> and <see cref="SchedulerInstanceId" /> are <see langword="null" /> together,
/// for a registration nothing has built. Listing the registrations is what lets an operator see a
/// tenant that has not started; building one to fill the listing in would start every tenant an
/// inventory touched.
/// </remarks>
internal record SchedulerHeaderDto(
    string Name,
    string? SchedulerInstanceId,
    SchedulerStatus? Status,
    SchedulerOrigin Origin)
{
    /// <summary>
    /// The attached store this scheduler is a window onto, or <see langword="null" /> for a scheduler
    /// of the process that answered.
    /// </summary>
    /// <remarks>
    /// An added property rather than a fifth positional parameter, so a client built against the four
    /// goes on deserializing this and a server that never attaches a store sends nothing new. A 4.1
    /// client reading a 4.2 listing sees <see cref="SchedulerOrigin" /> carry a value it has no name
    /// for, exactly as a 4.0 client did when <see cref="SchedulerOrigin.Remote" /> was added; the enum
    /// is numeric on the wire and appended to, which is what makes that safe.
    /// </remarks>
    public string? Target { get; init; }

    /// <remarks>
    /// Everything comes off the registration, which asked the scheduler once and asynchronously.
    /// Reading <see cref="IScheduler.SchedulerInstanceId" /> off the scheduler here is what used to make
    /// this listing block on a round trip per remote scheduler.
    /// </remarks>
    public static SchedulerHeaderDto Create(SchedulerRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        return new SchedulerHeaderDto(
            registration.Name,
            registration.SchedulerInstanceId,
            registration.Status,
            registration.Origin)
        {
            Target = registration.Target
        };
    }
}
