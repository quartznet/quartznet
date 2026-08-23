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
/// One scheduler a container knows about, as reported by
/// <see cref="ISchedulerRegistry.QuerySchedulers" />.
/// </summary>
/// <param name="Name">
/// The scheduler's name, spelled as it was registered. For a named registration this is the name
/// <c>AddQuartz(name, …)</c> was given, which is also the service key and the options name; for the
/// default scheduler it is its configured <see cref="QuartzSchedulerOptions.InstanceName" />.
/// </param>
/// <param name="Origin">Where the scheduler came from.</param>
/// <param name="Status">
/// What state the scheduler is in, or <see langword="null" /> when no scheduler has been created under
/// this name yet. Null is the answer this query exists to give: the registration is there, nothing has
/// been built from it, and asking did not build it.
/// </param>
public sealed record SchedulerRegistration(string Name, SchedulerOrigin Origin, SchedulerStatus? Status)
{
    /// <summary>
    /// Whether a scheduler exists under this name. A registration nothing has resolved yet reports
    /// <see langword="false" />; <see cref="Status" /> says what state a scheduler that does exist is in.
    /// </summary>
    public bool IsCreated => Status is not null;
}
