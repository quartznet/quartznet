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
    public static SchedulerHeaderDto Create(SchedulerRegistration registration, IScheduler? scheduler)
    {
        ArgumentNullException.ThrowIfNull(registration);

        return new SchedulerHeaderDto(
            registration.Name,
            scheduler?.SchedulerInstanceId,
            registration.Status,
            registration.Origin);
    }
}
