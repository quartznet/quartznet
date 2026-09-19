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

using Quartz.Impl;
using Quartz.Impl.Triggers;

namespace Quartz.Util;

/// <summary>
/// Reads a job's or a trigger's data map without creating one that does not exist yet.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IJobDetail.JobDataMap" /> and <see cref="ITrigger.JobDataMap" /> hand out a map a caller
/// may write into, so both implementations create one on the first read and keep it. That is the right
/// contract for a caller that is about to put something in the map, and the wrong one for the two
/// places that only want to know whether there is anything to merge — they ask on every firing, of a
/// trigger and a job detail that were cloned for that firing alone, and an empty map created there is
/// three objects nobody reads (#3802).
/// </para>
/// <para>
/// The shapes Quartz ships are asked for the field directly; anything else is asked through the
/// interface, which is exactly what the two call sites did before. So a job detail or a trigger of
/// somebody else's behaves as it always has.
/// </para>
/// </remarks>
internal static class JobDataMaps
{
    /// <summary>
    /// The job's data map, or <see langword="null" /> when it has none.
    /// </summary>
    internal static JobDataMap? OrNull(IJobDetail jobDetail)
    {
        return jobDetail is JobDetailImpl impl ? impl.JobDataMapOrNull : jobDetail.JobDataMap;
    }

    /// <summary>
    /// The trigger's data map, or <see langword="null" /> when it has none.
    /// </summary>
    internal static JobDataMap? OrNull(ITrigger trigger)
    {
        return trigger is TriggerBase impl ? impl.JobDataMapOrNull : trigger.JobDataMap;
    }
}
