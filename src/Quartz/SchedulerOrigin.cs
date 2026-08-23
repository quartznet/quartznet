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
/// Where a scheduler <see cref="ISchedulerRegistry.QuerySchedulers" /> reports came from.
/// </summary>
public enum SchedulerOrigin
{
    /// <summary>
    /// Registered with the container by <c>AddQuartz()</c> or <c>AddQuartz(name, …)</c>. The container
    /// holds its object graph, and the hosted service creates and starts it.
    /// </summary>
    Container = 0,

    /// <summary>
    /// Bound into the container's <see cref="Extensibility.ISchedulerRepository" /> rather than
    /// registered as a scheduler of this container: a scheduler built by
    /// <c>QuartzSchedulerBuilder</c> and made visible by hand, or a remote scheduler registered with
    /// <c>AddQuartzHttpClient</c>. Nothing in the container owns its lifetime.
    /// </summary>
    Runtime = 1
}
