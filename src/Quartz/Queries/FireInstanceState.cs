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
/// How far along a <see cref="FireInstance" /> is: reserved by a node, or running.
/// </summary>
/// <remarks>
/// Deliberately store-neutral and much smaller than the ADO.NET store's
/// <see cref="Quartz.Extensibility.StoredTriggerState" />: the two states below are the only ones a
/// listing can meaningfully report, and every job store can answer them.
/// </remarks>
public enum FireInstanceState
{
    /// <summary>
    /// A node has reserved the firing but has not started the job yet. The job is not known at this
    /// point, so <see cref="FireInstance.JobKey" /> is <see langword="null" />.
    /// </summary>
    /// <remarks>
    /// The window is normally very short. It is visible at all because a reservation is durable: a node
    /// that dies between acquiring and firing leaves the row behind until cluster recovery clears it.
    /// </remarks>
    Acquired,

    /// <summary>
    /// The job is running.
    /// </summary>
    Executing
}
