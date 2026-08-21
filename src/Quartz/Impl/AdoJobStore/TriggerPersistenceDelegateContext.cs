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

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// The settings a <see cref="ITriggerPersistenceDelegate" /> works from, handed to
/// <see cref="ITriggerPersistenceDelegate.Initialize" /> once by the driver delegate before the
/// persistence delegate is used.
/// </summary>
public sealed record TriggerPersistenceDelegateContext
{
    /// <summary>
    /// Name of the scheduler whose rows the delegate reads and writes, stored in <c>SCHED_NAME</c>.
    /// </summary>
    public required string SchedulerName { get; init; }

    /// <summary>
    /// The prefix of all table names.
    /// </summary>
    public required string TablePrefix { get; init; }

    /// <summary>
    /// Command preparation and parameter binding for the type table this delegate owns, which is the
    /// driver delegate itself.
    /// </summary>
    public required IDbAccessor DbAccessor { get; init; }
}
